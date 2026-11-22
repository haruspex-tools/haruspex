using System.ComponentModel;
using System.Diagnostics;

namespace Haruspex.TestSupport;

/// <summary>
/// The npm package in <c>tests/emit-check</c>, shared by the emit check (Haruspex.Tests) and the differential tests'
/// Zod acceptance check, which run at the same time in separate processes. Compiled into both projects from
/// <c>tests/Shared</c>.
/// </summary>
internal static class EmitCheckPackage
{
    public const string RequireNodeVariable = "HARUSPEX_REQUIRE_NODE";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The longest command line <see cref="TryRun(string, string[], string, out string, out string)"/> starts, on every platform:
    /// cmd.exe's limit, 8,191 characters. Windows starts a program with one command line of at most 32,767 characters, and a
    /// <c>.cmd</c> such as <c>npm.cmd</c> runs through cmd.exe, whose limit is lower. A list of files that grows with the fixtures
    /// would pass the limit on Windows alone, failing there and nowhere else (observed 21 Nov 2026: "The filename or extension is
    /// too long" from <c>JsonSchemaMetaSchemaTests</c>, given every emitted document by name). Refused everywhere, a growing list is
    /// caught on the platform it is written on; such a list goes in a folder, and the program is given the folder.
    /// </summary>
    internal const int LongestCommandLine = 8191;

    /// <summary>
    /// Skips the calling test when Node.js can't be run, or fails it when <c>HARUSPEX_REQUIRE_NODE=1</c> (as CI sets
    /// it), so a runner without Node fails instead of quietly skipping.
    /// </summary>
    public static void RequireNode(string directory, string what)
    {
        if (TryRun("node", ["--version"], directory, out _, out var why))
            return;
        if (Environment.GetEnvironmentVariable(RequireNodeVariable) == "1")
            Assert.Fail($"{RequireNodeVariable}=1, but Node.js could not be run: {why}");
        Assert.Skip($"Node.js could not be run ({why}), so {what}. Install Node.js LTS to run this test.");
    }

    /// <summary>
    /// Runs <c>npm ci</c> unless <c>node_modules</c> is there and was installed since <c>package-lock.json</c> last changed,
    /// which npm's record of the install (<c>node_modules/.package-lock.json</c>) says: a package added to the lock file
    /// reaches a checkout installed before it. Two test processes may get here together, so the check and the install
    /// happen under a lock file that only one process can hold.
    /// </summary>
    public static void EnsureInstalled(string directory)
    {
        using var lockFile = Lock(Path.Combine(directory, "node_modules.lock"));
        var installed = new FileInfo(Path.Combine(directory, "node_modules", ".package-lock.json"));
        if (installed.Exists && installed.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(Path.Combine(directory, "package-lock.json")))
            return;
        Assert.True(
            TryRun(Npm(), ["ci", "--ignore-scripts", "--no-audit", "--no-fund"], directory, out var output, out var why),
            $"npm ci failed in {directory}: {why}\n{output}");
    }

    /// <summary>
    /// npm, as a program to start: on Windows, <c>npm.cmd</c> by its full path. Started by its bare name, cmd works out the folder
    /// the script reads its own files from (<c>%~dp0</c>) from the working folder rather than from where it was found on the
    /// <c>PATH</c>, so npm looked for itself in the package's <c>node_modules</c> and failed (observed on Windows, 7w).
    /// </summary>
    private static string Npm()
    {
        if (!OperatingSystem.IsWindows())
            return "npm";
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return folders.Select(folder => Path.Combine(folder.Trim('"'), "npm.cmd")).FirstOrDefault(File.Exists) ?? "npm.cmd";
    }

    private static FileStream Lock(string path)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Runs a program to completion, with its output; false when it can't start, times out or fails.</summary>
    public static bool TryRun(string file, string[] args, string workingDirectory, out string output, out string why) =>
        TryRun(file, args, workingDirectory, new Dictionary<string, string>(), out output, out why);

    /// <summary>As <c>TryRun</c>, with variables added to the program's environment, such as a secret kept off its command line.</summary>
    public static bool TryRun(string file, string[] args, string workingDirectory, IReadOnlyDictionary<string, string> environment, out string output, out string why)
    {
        if (CommandLineLength(file, args) is var length and > LongestCommandLine)
        {
            output = "";
            why = $"{file}: its command line would be {length:N0} characters, more than the {LongestCommandLine:N0} Windows allows a program run through cmd.exe, so it is refused on every platform. Write the files to a folder and pass the folder.";
            return false;
        }

        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception($"{file} did not start");
        }
        catch (Win32Exception e)
        {
            output = "";
            why = $"{file}: {e.Message}";
            return false;
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                output = "";
                why = $"{file} timed out after {Timeout}";
                return false;
            }

            output = stdout.Result + stderr.Result;
            why = $"{file} exited with code {process.ExitCode}";
            return process.ExitCode == 0;
        }
    }

    /// <summary>
    /// The length of the command line Windows would be given for <paramref name="file"/> and <paramref name="args"/>, at most, when
    /// no part holds a quote or ends with a backslash, as none the tests pass does. .NET quotes only a part that needs it (one that
    /// holds a space, for example) and puts a space before each argument; this counts every part as quoted. A backslash is doubled
    /// only before a quote, so a Windows path's backslashes add nothing.
    /// </summary>
    internal static int CommandLineLength(string file, string[] args) => file.Length + 2 + args.Sum(arg => arg.Length + 3);
}

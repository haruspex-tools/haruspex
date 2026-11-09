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
    /// Runs <c>npm ci</c> unless <c>node_modules</c> is there. Two test processes may get here together, so the
    /// check and the install happen under a lock file that only one process can hold.
    /// </summary>
    public static void EnsureInstalled(string directory)
    {
        using var lockFile = Lock(Path.Combine(directory, "node_modules.lock"));
        if (Directory.Exists(Path.Combine(directory, "node_modules")))
            return;
        var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        Assert.True(
            TryRun(npm, ["ci", "--ignore-scripts", "--no-audit", "--no-fund"], directory, out var output, out var why),
            $"npm ci failed in {directory}: {why}\n{output}");
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
    public static bool TryRun(string file, string[] args, string workingDirectory, out string output, out string why)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

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
}

using System.ComponentModel;
using System.Diagnostics;
using Haruspex.Cli;
using Haruspex.Emit.Zod;

namespace Haruspex.Tests;

/// <summary>
/// Compiles the Zod generated from every procedure of the orders and values fixtures with <c>tsc --noEmit</c>, against the zod and
/// TypeScript versions pinned in <c>tests/emit-check/package-lock.json</c>. <c>src/usage.ts</c> there also
/// checks the inferred types: that an omitted key is optional and a kept null is nullable.
/// </summary>
public class EmitCheckTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void Generated_zod_compiles_with_tsc()
    {
        if (!TryRun("node", ["--version"], TestPaths.EmitCheck, out _, out var why))
        {
            // CI sets this, so a runner without Node fails instead of quietly skipping the check.
            if (Environment.GetEnvironmentVariable("HARUSPEX_REQUIRE_NODE") == "1")
                Assert.Fail($"HARUSPEX_REQUIRE_NODE=1, but Node.js could not be run: {why}");

            Assert.Skip($"Node.js could not be run ({why}), so the generated Zod was not type-checked. Install Node.js LTS to run this test.");
        }

        var generated = Path.Combine(TestPaths.EmitCheck, "generated");
        Directory.CreateDirectory(generated);
        foreach (var (fixture, file) in new[] { ("orders", "haruspex.zod.ts"), ("values", "values.zod.ts") })
        {
            var project = SqlProject.Load(TestPaths.Fixture(fixture));
            var selection = project.Select([ObjectPattern.Parse("*.*")]);
            File.WriteAllText(Path.Combine(generated, file), ZodEmitter.Emit(project.Infer(selection.Procedures)));
        }

        if (!Directory.Exists(Path.Combine(TestPaths.EmitCheck, "node_modules")))
        {
            var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
            Assert.True(
                TryRun(npm, ["ci", "--ignore-scripts", "--no-audit", "--no-fund"], TestPaths.EmitCheck, out var npmOutput, out var npmWhy),
                $"npm ci failed in tests/emit-check: {npmWhy}\n{npmOutput}");
        }

        Assert.True(
            TryRun("node", [Path.Combine("node_modules", "typescript", "bin", "tsc"), "--noEmit", "-p", "."], TestPaths.EmitCheck, out var tscOutput, out var tscWhy),
            $"tsc rejected the generated Zod ({tscWhy}):\n{tscOutput}");
    }

    private static bool TryRun(string file, string[] args, string workingDirectory, out string output, out string why)
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

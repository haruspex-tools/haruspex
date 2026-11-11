using Haruspex.Cli;
using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Fixtures;

/// <summary>
/// Compiles the Zod generated from every procedure of every fixture with <c>tsc --noEmit</c>, against the zod and
/// TypeScript versions pinned in <c>tests/emit-check/package-lock.json</c>. <c>src/usage.ts</c> and
/// <c>src/checks-usage.ts</c> there also check the inferred types: that an omitted key is optional, a kept null is
/// nullable, and a value set is a union of literals; <c>src/shop-usage.ts</c> checks what the shop fixture's config file
/// changes.
/// </summary>
public class EmitCheckTests
{
    [Fact]
    public void Generated_zod_compiles_with_tsc()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the generated Zod was not type-checked");

        // Emptied first, so a module left by an earlier run, of a fixture since renamed or removed, can't pass for one.
        var generated = Path.Combine(RepositoryPaths.EmitCheck, "generated");
        if (Directory.Exists(generated))
            Directory.Delete(generated, recursive: true);
        Directory.CreateDirectory(generated);

        // Every fixture's procedures, as the snapshot tests and the differential tests infer them, in <name>.zod.ts.
        foreach (var fixture in Fixture.All.Where(f => !f.IsProject))
            File.WriteAllText(Path.Combine(generated, $"{fixture.Name}.zod.ts"), ZodEmitter.Emit(fixture.Ir));

        // A project fixture through the CLI, with its config file: the shop's strict objects, overrides and an import
        // that tsconfig.json maps to src/shop-types.ts.
        foreach (var fixture in Fixture.All.Where(f => f.IsProject))
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exitCode = HaruspexCli.Run(["generate", "--out", Path.Combine(generated, fixture.Name)], output, error, fixture.Folder);
            Assert.True(exitCode == 0, error.ToString());
        }

        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("node_modules", "typescript", "bin", "tsc"), "--noEmit", "-p", "."], RepositoryPaths.EmitCheck, out var tscOutput, out var tscWhy),
            $"tsc rejected the generated Zod ({tscWhy}):\n{tscOutput}");
    }

    /// <summary>
    /// The evidence for emitting <c>decimal(p, s)</c> as <c>.multipleOf(1e-s)</c>: Zod 4.6.7 must accept every
    /// decimal of each scale, as <c>JSON.parse</c> reads it, without a floating-point false failure, and must still
    /// reject a value with too many decimal places. <c>src/multiple-of.mjs</c> says how it sweeps.
    /// </summary>
    [Fact]
    public void Zod_checks_a_decimal_step_without_floating_point_false_failures()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "Zod's multipleOf was not checked");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "multiple-of.mjs")], RepositoryPaths.EmitCheck, out var output, out var why),
            $"multipleOf gave false failures ({why}):\n{output}");
    }
}

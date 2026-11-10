using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.TestSupport;

namespace Haruspex.Tests;

/// <summary>
/// Compiles the Zod generated from every procedure of the fixtures with <c>tsc --noEmit</c>, against the zod and
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
        EmitCheckPackage.RequireNode(TestPaths.EmitCheck, "the generated Zod was not type-checked");

        var generated = Path.Combine(TestPaths.EmitCheck, "generated");
        Directory.CreateDirectory(generated);
        foreach (var (fixture, file) in new[] { ("orders", "haruspex.zod.ts"), ("values", "values.zod.ts"), ("checks", "checks.zod.ts"), ("payloads", "payloads.zod.ts"), ("trees", "trees.zod.ts"), ("flow", "flow.zod.ts") })
        {
            var project = SqlProject.Load(TestPaths.Fixture(fixture));
            var selection = project.Select([ObjectPattern.Parse("*.*")]);
            File.WriteAllText(Path.Combine(generated, file), ZodEmitter.Emit(project.Infer(selection.Procedures)));
        }

        // The shop fixture through the CLI, with its config file: strict objects, overrides and an import that
        // tsconfig.json maps to src/shop-types.ts.
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            var exitCode = HaruspexCli.Run(["generate", "--out", Path.Combine(generated, "shop")], output, error, TestPaths.Fixture("shop"));
            Assert.True(exitCode == 0, error.ToString());
        }

        EmitCheckPackage.EnsureInstalled(TestPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("node_modules", "typescript", "bin", "tsc"), "--noEmit", "-p", "."], TestPaths.EmitCheck, out var tscOutput, out var tscWhy),
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
        EmitCheckPackage.RequireNode(TestPaths.EmitCheck, "Zod's multipleOf was not checked");
        EmitCheckPackage.EnsureInstalled(TestPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "multiple-of.mjs")], TestPaths.EmitCheck, out var output, out var why),
            $"multipleOf gave false failures ({why}):\n{output}");
    }
}

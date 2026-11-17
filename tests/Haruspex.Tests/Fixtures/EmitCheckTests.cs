using Haruspex.Cli;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Fixtures;

/// <summary>
/// Compiles the Zod generated from every procedure of every fixture, and the functions that use it, with <c>tsc --noEmit</c>
/// (strict, see its <c>tsconfig.json</c>), against the zod, mssql and TypeScript versions pinned in
/// <c>tests/emit-check/package-lock.json</c>, and lints them with the rules pinned in <c>tests/emit-lint</c>. <c>src/usage.ts</c> and
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
        WriteGenerated(RepositoryPaths.EmitCheck);
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("node_modules", "typescript", "bin", "tsc"), "--noEmit", "-p", "."], RepositoryPaths.EmitCheck, out var tscOutput, out var tscWhy),
            $"tsc rejected the generated Zod ({tscWhy}):\n{tscOutput}");

        // And as a Node project of ECMAScript modules compiles them, where an import must name its file.
        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("node_modules", "typescript", "bin", "tsc"), "--noEmit", "-p", "tsconfig.nodenext.json"], RepositoryPaths.EmitCheck, out var nodeOutput, out var nodeWhy),
            $"tsc rejected the generated modules under nodenext ({nodeWhy}):\n{nodeOutput}");
    }

    /// <summary>
    /// Lints the same modules with ESLint's recommended rules, typescript-eslint's strict and stylistic sets, and the two rules
    /// a team's own config commonly adds (<c>method-signature-style</c>, <c>explicit-function-return-type</c>), any warning
    /// failing, as a team that lints its own code would lint the files it commits (decision 0062). A separate package (<c>tests/emit-lint</c>), because
    /// typescript-eslint reads TypeScript through the compiler's JavaScript API, which the TypeScript the emit check compiles
    /// with no longer has.
    /// </summary>
    [Fact]
    public void Generated_modules_pass_typescript_eslint_s_strict_and_stylistic_rules()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitLint, "the generated Zod was not checked by the linter");
        WriteGenerated(RepositoryPaths.EmitLint);
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitLint);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("node_modules", "eslint", "bin", "eslint.js"), "--max-warnings", "0", "generated"], RepositoryPaths.EmitLint, out var output, out var why),
            $"ESLint reported the generated Zod ({why}):\n{output}");
    }

    /// <summary>Every fixture's module in <c>generated</c> under <paramref name="package"/>.</summary>
    private static void WriteGenerated(string package)
    {
        // Emptied first, so a module left by an earlier run, of a fixture since renamed or removed, can't pass for one.
        var generated = Path.Combine(package, "generated");
        if (Directory.Exists(generated))
            Directory.Delete(generated, recursive: true);
        Directory.CreateDirectory(generated);

        // Every fixture's procedures, as the snapshot tests and the differential tests infer them, in <name>.zod.ts, and their
        // functions, which import it, in <name>.mssql.ts.
        foreach (var fixture in Fixture.All.Where(f => !f.IsProject))
        {
            File.WriteAllText(Path.Combine(generated, $"{fixture.Name}.zod.ts"), ZodEmitter.Emit(fixture.Ir).Expected());
            var options = new MssqlOptions { SchemaModule = $"./{fixture.Name}.zod.js", IsReserved = TypeScriptNames.IsReserved };
            File.WriteAllText(Path.Combine(generated, $"{fixture.Name}.mssql.ts"), MssqlEmitter.Emit(fixture.Ir, options).Expected());
        }

        // A project fixture through the CLI, with its config file: the shop's strict objects, overrides and an import
        // that tsconfig.json maps to src/shop-types.ts.
        foreach (var fixture in Fixture.All.Where(f => f.IsProject))
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exitCode = HaruspexCli.Run(["generate", "--out", Path.Combine(generated, fixture.Name)], output, error, fixture.Folder);
            Assert.True(exitCode == 0, error.ToString());
        }
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

using Haruspex.Cli;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Fixtures;

/// <summary>
/// Compiles the Zod generated from every procedure of every fixture, and the functions that use it, with <c>tsc --noEmit</c>
/// (strict, see its <c>tsconfig.json</c>), against the zod, mssql and TypeScript versions pinned in
/// <c>tests/emit-check/package-lock.json</c>, and lints them with the rules pinned in <c>tests/emit-lint</c>. <c>src/usage.ts</c> and
/// <c>src/checks-usage.ts</c> there also check the inferred types: that an omitted key is optional, a kept null is
/// nullable, and a value set is a union of literals; <c>src/shop-usage.ts</c> checks what the shop fixture's config file
/// changes, and <c>src/mapping-usage.ts</c> that the mapping helper accepts a mapper that accounts for every key of a fixture's
/// row and refuses one that doesn't. A module combined with a baseline (<see cref="Combined"/>) is compiled and checked by the linter beside them.
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

    /// <summary>
    /// Runs a generated function against the test double <c>docs/database-module.md</c> shows (<c>src/test-double.ts</c>, which
    /// the compile above type-checks): its result and <c>OUTPUT</c> parameters come back from the double's <c>recordsets</c> and
    /// <c>output</c>, what it returns is checked by the schemas, and a double giving only <c>recordset</c> fails the call.
    /// </summary>
    [Fact]
    public void A_function_runs_against_a_double_that_returns_recordsets_output_and_return_value()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the functions were not run against a test double");
        WriteGenerated(RepositoryPaths.EmitCheck);
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "double.mjs")], RepositoryPaths.EmitCheck, out var output, out var why),
            $"A generated function did not run as documented against the test double ({why}):\n{output}");
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

        // A module combined with a baseline, of the forms only combining writes: keys that may be left out whatever their value,
        // unions of another kind, result sets that may be missing, parameters left out or narrowed.
        File.WriteAllText(Path.Combine(generated, "combined.zod.ts"), ZodEmitter.Emit(Combined()).Expected());
        File.WriteAllText(
            Path.Combine(generated, "combined.mssql.ts"),
            MssqlEmitter.Emit(Combined(), new MssqlOptions { SchemaModule = "./combined.zod.js", IsReserved = TypeScriptNames.IsReserved }).Expected());

        // Procedures named as the module's own helpers and the global types it writes, which must still compile.
        File.WriteAllText(Path.Combine(generated, "names.zod.ts"), ZodEmitter.Emit(HelperNames()).Expected());

        // The mapping helper, which is the same for every module, once: src/mapping-usage.ts checks it against the fixtures' types,
        // and the lint holds it to the rules every generated file meets.
        File.WriteAllText(Path.Combine(generated, HaruspexCli.MappingFileName), MappingModule.Text);

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
    /// Procedures each declaring a type named as a binding the module itself makes or a global type a helper could write: a module
    /// with JSON text and a canonical value set, so that both helpers are in it, whose procedures are named <c>Fold</c>,
    /// <c>Record</c>, <c>Map</c> and the other generic types of TypeScript's library, and, as a naming rule that keeps case would
    /// name them, <c>z</c>, <c>jsonText</c>, <c>canonicalOf</c>, <c>canonicalForm</c> and <c>folds</c>. A declaration's name is a
    /// type (and its schema a <c>…Schema</c> constant), so it clashes only with a type the module names: before the canonical helper
    /// named none, a procedure named <c>Fold</c> made the module fail to compile (TS2300) while haruspex reported success.
    /// </summary>
    private static IrDocument HelperNames()
    {
        const string Tables = "CREATE TABLE names.item (id int NOT NULL PRIMARY KEY, state varchar(10) COLLATE Latin1_General_100_CI_AS NOT NULL CHECK (state IN ('open', 'shut')));";
        string[] pascal = ["fold", "record", "map", "set", "array", "readonly_array", "partial", "promise", "object", "string", "date", "buffer", "json"];
        string[] camel = ["z", "json_text", "canonical_of", "canonical_form", "folds"];
        var procedures = string.Join(
            "\nGO\n",
            pascal.Concat(camel).Select(name =>
                $"CREATE PROCEDURE names.[{name}] AS SELECT i.id, i.state, (SELECT j.state FROM names.item AS j WHERE j.id = i.id FOR JSON PATH) AS card FROM names.item AS i;"));
        var document = InferProcedure.Infer("names.*", ("schema.sql", "CREATE SCHEMA names;"), ("tables.sql", Tables), ("procedures.sql", procedures));

        // Under "case": "camel", or "preserve" with these names in the SQL, the first letter is lower case.
        var lower = camel.Select(n => string.Concat(n.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]))).ToHashSet(StringComparer.Ordinal);
        return document with { Objects = [.. document.Objects.Select(o => lower.Contains(o.Name) ? o with { Name = char.ToLowerInvariant(o.Name[0]) + o.Name[1..] } : o)] };
    }

    /// <summary>
    /// Invented procedures before a change and after, combined (<see cref="IrCombination"/>): a nullable key only the candidate
    /// returns, one only the baseline does, a value of another kind in each, a trailing set in one, a JSON key left out when null
    /// in one and sent as null in the other, a rename, and parameters only one has or of another width.
    /// </summary>
    private static IrDocument Combined()
    {
        const string Tables = "CREATE TABLE blend.item (id int NOT NULL PRIMARY KEY, name nvarchar(20) NOT NULL, note nvarchar(20) NULL, qty tinyint NOT NULL);";
        const string Before = """
            CREATE PROCEDURE blend.get_item @id int, @code nvarchar(20), @old bit = 0 AS
            SELECT i.id, i.name AS label, i.qty AS amount, i.note AS gone FROM blend.item AS i WHERE i.id = @id;
            GO
            CREATE PROCEDURE blend.get_card AS SELECT i.id, i.note FROM blend.item AS i FOR JSON PATH;
            """;
        const string After = """
            CREATE PROCEDURE blend.get_item @id int, @code nvarchar(10), @new bit = 0 AS
            SELECT i.id, i.name AS title, i.name AS amount, i.note AS channel FROM blend.item AS i WHERE i.id = @id;
            SELECT i.qty FROM blend.item AS i;
            GO
            CREATE PROCEDURE blend.get_card AS SELECT i.id, i.note FROM blend.item AS i FOR JSON PATH, INCLUDE_NULL_VALUES;
            """;
        var baseline = InferProcedure.Infer("blend.*", ("schema.sql", "CREATE SCHEMA blend;"), ("tables.sql", Tables), ("procedures.sql", Before));
        var candidate = InferProcedure.Infer("blend.*", ("schema.sql", "CREATE SCHEMA blend;"), ("tables.sql", Tables), ("procedures.sql", After));
        var combined = IrCombination.Of(baseline, candidate, new IrFitOptions());
        Assert.DoesNotContain(combined.Findings, f => f.Kind == IrCombinationKind.Unbridgeable);
        return combined.Document;
    }

    /// <summary>
    /// The JSON text helper, as a generated module writes it, builds its content's schema once, on the first value it checks,
    /// however many it checks, and still checks each: <c>src/json-text-once.mjs</c> counts the calls.
    /// </summary>
    [Fact]
    public void The_json_text_helper_builds_its_content_s_schema_once_however_many_values_it_checks()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the JSON text helper was not run");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
        var document = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("PRow")), [])])],
            new Dictionary<string, IrObjectType> { ["PRow"] = new([new IrField("v", new IrJsonText(new IrAnyJson()), IrPresence.Always, [])]) },
            []);
        var module = ZodEmitter.Emit(document).Expected();
        Assert.Contains("const jsonText = ", module, StringComparison.Ordinal);

        // Under accept/, which git ignores, so that the module's import of zod resolves to the package's own.
        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "module.ts"), module + "export { jsonText }\n");
            Assert.True(
                EmitCheckPackage.TryRun("node", [Path.Combine("src", "json-text-once.mjs"), folder], RepositoryPaths.EmitCheck, out var output, out var why),
                $"The JSON text helper built its content other than once ({why}):\n{output}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
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

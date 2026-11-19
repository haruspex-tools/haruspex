using Haruspex.Cli;
using Haruspex.Ir;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex check</c>: it infers as <c>generate</c> does, writes nothing, and fails when what it would write claims something
/// other than what is committed, saying what changed; when there is nothing to compare with; and when the module leaves a
/// selected procedure's results, or with the config file's say-so any value, unknown. Each case runs <c>generate</c> on a
/// small project, changes it as a database change would, and runs <c>check</c>.
/// </summary>
public class CheckTests
{
    private const string Tables = """
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            customer nvarchar(50) NOT NULL,
            note nvarchar(200) NULL,
            status varchar(10) NOT NULL CHECK (status IN ('open', 'held')));
        """;

    private const string ListOrders = "CREATE PROCEDURE shop.list_orders AS\nSELECT o.order_id, o.customer, o.status FROM shop.orders AS o;";

    private const string Config = """{ "source": "db", "out": "generated", "schemas": { "shop": { "include": ["*"] } } }""";

    /// <summary>A folder with the project and its config file, generated, so that its output is what is committed.</summary>
    private static TempDirectory Generated(string procedures = ListOrders, string tables = Tables, string config = Config)
    {
        var folder = new TempDirectory();
        folder.File("db/tables.sql", tables);
        folder.File("db/procedures.sql", procedures);
        folder.File("haruspex.json", config);
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        return folder;
    }

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, CliOutput.Shown(output.ToString(), folder.Path), CliOutput.Shown(error.ToString(), folder.Path));
    }

    /// <summary>Every file under the output folder with its bytes, to show that a check wrote nothing.</summary>
    private static Dictionary<string, string> Written(TempDirectory folder)
    {
        var generated = Path.Combine(folder.Path, "generated");
        return Directory.EnumerateFiles(generated, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(generated, f), File.ReadAllText, StringComparer.Ordinal);
    }

    [Fact]
    public void Check_passes_when_the_project_claims_what_is_committed()
    {
        using var folder = Generated();

        var (exitCode, output, error) = Run(folder, "check");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Equal("haruspex: 1 procedure, no unknowns. generated is up to date.\n", output);
    }

    private const string EmitsBoth = """{ "source": "db", "out": "generated", "emit": ["zod", "json-schema"], "schemas": { "shop": { "include": ["*"] } } }""";

    [Fact]
    public void Check_compares_every_file_emit_names_and_says_which_differs()
    {
        using var folder = Generated(config: EmitsBoth);
        var schema = Path.Combine(folder.Path, "generated", HaruspexCli.JsonSchemaFileName);
        File.WriteAllText(schema, File.ReadAllText(schema).Replace("SQL type int.", "Edited.", StringComparison.Ordinal));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.StartsWith($"generated/{HaruspexCli.JsonSchemaFileName}: error HAR6015: line ", error, StringComparison.Ordinal);
        Assert.Single(error.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public void Check_fails_when_a_file_emit_names_is_not_committed()
    {
        using var folder = Generated(config: EmitsBoth);
        File.Delete(Path.Combine(folder.Path, "generated", HaruspexCli.JsonSchemaFileName));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains($"error HAR6011: nothing to compare with: generated/{HaruspexCli.JsonSchemaFileName} isn't there.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_passes_when_every_file_emit_names_is_as_committed()
    {
        using var folder = Generated(config: EmitsBoth);

        Assert.Equal(0, Run(folder, "check").ExitCode);
    }

    [Fact]
    public void Check_passes_when_only_locations_moved_since_they_are_not_claims()
    {
        using var folder = Generated();
        folder.File("db/procedures.sql", "-- The orders, for the list page.\n\n" + ListOrders);

        Assert.Equal(0, Run(folder, "check").ExitCode);
    }

    [Fact]
    public void Check_fails_with_each_change_at_the_procedure_and_writes_nothing()
    {
        using var folder = Generated();
        var committed = Written(folder);
        folder.File("db/tables.sql", Tables
            .Replace("customer nvarchar(50) NOT NULL", "customer nvarchar(50) NULL", StringComparison.Ordinal)
            .Replace("('open', 'held')", "('open', 'held', 'closed')", StringComparison.Ordinal));

        var (exitCode, output, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Equal("", output);
        Assert.Equal(committed, Written(folder));
        Snapshot.Match(error.ReplaceLineEndings("\n"), $"{nameof(CheckTests)}.{nameof(Check_fails_with_each_change_at_the_procedure_and_writes_nothing)}", "txt");
    }

    [Fact]
    public void A_procedure_added_or_removed_fails_the_check()
    {
        using var folder = Generated();
        folder.File("db/procedures.sql", ListOrders + "\nGO\nCREATE PROCEDURE shop.count_orders AS SELECT count_big(*) AS orders FROM shop.orders AS o;");

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("db/procedures.sql:4:1: error HAR6014: shop.count_orders: added", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_committed_fails_the_check_naming_what_is_missing_and_creates_no_folder()
    {
        using var folder = Generated();
        Directory.Delete(Path.Combine(folder.Path, "generated"), recursive: true);

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6011:", error, StringComparison.Ordinal);
        Assert.Contains(HaruspexCli.IrFileName, error, StringComparison.Ordinal);
        Assert.Contains(HaruspexCli.ZodFileName, error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "generated")));
    }

    [Fact]
    public void A_committed_module_alone_missing_fails_the_check()
    {
        using var folder = Generated();
        File.Delete(Path.Combine(folder.Path, "generated", HaruspexCli.ZodFileName));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains($"error HAR6011: nothing to compare with: generated/{HaruspexCli.ZodFileName} isn't there.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_committed_ir_of_another_version_fails_the_check_naming_both_versions()
    {
        using var folder = Generated();
        var ir = Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName);
        File.WriteAllText(ir, File.ReadAllText(ir).Replace($"\"irVersion\": \"{IrDocument.CurrentVersion}\"", "\"irVersion\": \"0.9\"", StringComparison.Ordinal));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains($"generated/{HaruspexCli.IrFileName}: error HAR6012: this file is IR 0.9, and this haruspex writes IR {IrDocument.CurrentVersion}", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_committed_ir_that_does_not_read_fails_the_check_saying_where()
    {
        using var folder = Generated();
        folder.File("generated/" + HaruspexCli.IrFileName, $$"""{ "irVersion": "{{IrDocument.CurrentVersion}}" }""");

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6013:", error, StringComparison.Ordinal);
        Assert.Contains("$.source is missing", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_that_differs_when_no_claim_does_fails_the_check()
    {
        using var folder = Generated();
        var zod = Path.Combine(folder.Path, "generated", HaruspexCli.ZodFileName);
        File.AppendAllText(zod, "// edited by hand\n");

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6015: line ", error, StringComparison.Ordinal);
        Assert.Contains("is `// edited by hand` where haruspex now writes nothing (the module ends before)", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reason_the_module_comments_on_that_changes_fails_the_check_with_the_line_that_changed()
    {
        using var folder = Generated(ListOrders.Replace("o.status", "o.gone AS status", StringComparison.Ordinal));
        folder.File("db/procedures.sql", ListOrders.Replace("o.status", "dbo.gone(1) AS status", StringComparison.Ordinal));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("HAR6014", error, StringComparison.Ordinal);
        Assert.Contains("error HAR6015: line ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("another version of haruspex wrote it", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_checked_out_with_crlf_line_endings_is_the_same_module()
    {
        using var folder = Generated();
        var zod = Path.Combine(folder.Path, "generated", HaruspexCli.ZodFileName);
        File.WriteAllText(zod, File.ReadAllText(zod).ReplaceLineEndings("\r\n"));

        Assert.Equal(0, Run(folder, "check").ExitCode);
    }

    [Fact]
    public void Results_that_are_unknown_fail_the_check_until_they_are_declared()
    {
        const string Dynamic = "CREATE PROCEDURE shop.find_orders @where nvarchar(200) AS\nDECLARE @sql nvarchar(max) = N'SELECT o.order_id FROM shop.orders AS o WHERE ' + @where;\nEXEC sp_executesql @sql;";
        using var folder = Generated(Dynamic);

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("db/procedures.sql:1:1: error HAR6016: shop.find_orders: its result sets are unknown", error, StringComparison.Ordinal);

        using var declared = Generated(Dynamic, config: """{ "source": "db", "out": "generated", "schemas": { "shop": { "include": ["*"] } }, "procedures": { "shop.find_orders": { "results": "none", "why": "it only runs the search it is given" } } }""");
        Assert.Equal(0, Run(declared, "check").ExitCode);
    }

    [Fact]
    public void Unknowns_are_noted_and_pass_unless_the_config_file_fails_them()
    {
        const string Unknown = "CREATE PROCEDURE shop.list_notes AS\nSELECT json_query(o.note) AS note FROM shop.orders AS o FOR JSON PATH;";
        using var reported = Generated(Unknown);
        var (passed, summary, quiet) = Run(reported, "check");
        var (_, _, notes) = Run(reported, "check", "--verbose");

        Assert.Equal(0, passed);
        Assert.Equal("", quiet);
        Assert.Contains("1 unknown (--verbose lists them).", summary, StringComparison.Ordinal);
        Assert.Contains("note HAR2013:", notes, StringComparison.Ordinal);

        using var failed = Generated(Unknown, config: """{ "source": "db", "out": "generated", "schemas": { "shop": { "include": ["*"] } }, "check": { "unknowns": "fail" } }""");
        var (exitCode, _, error) = Run(failed, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("note HAR2013:", error, StringComparison.Ordinal);
        Assert.Contains("haruspex.json: error HAR6017: 1 unknown in the module", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_takes_the_same_command_line_as_generate()
    {
        using var folder = Generated();

        Assert.Equal(0, Run(folder, "check", "db", "--include", "shop.*", "--out", "generated").ExitCode);
        Assert.Equal(1, Run(folder, "check", "db", "--include", "shop.list_*", "--out", "elsewhere").ExitCode);
        Assert.Equal(2, Run(folder, "check", "db", "--include", "a.b.c").ExitCode);
    }

    [Fact]
    public void A_file_in_the_output_folder_that_can_t_be_read_fails_the_check_naming_that_file()
    {
        using var folder = Generated();
        var module = Path.Combine(folder.Path, "generated", "haruspex.zod.ts");

        // Held open with no sharing, the file is there but can't be read, on every platform and whoever runs the test.
        using (new FileStream(module, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (exitCode, _, error) = Run(folder, "check");
            var (_, output, problems) = Run(folder, "generate");

            Assert.Equal(1, exitCode);
            Assert.StartsWith("generated/haruspex.zod.ts: error HAR6013: this file can't be read: ", error.ReplaceLineEndings("\n"), StringComparison.Ordinal);

            // Elsewhere generate says it can't compare and why, naming the file it couldn't read; on Windows, where a file held so
            // can't be replaced either, it can't write the folder, and says so.
            Assert.Contains(OperatingSystem.IsWindows() ? "error HAR6008: cannot write to generated: " : "Nothing to compare with: the earlier haruspex.zod.ts can't be read (", output + problems, StringComparison.Ordinal);
        }

        using var copy = Generated();
        Assert.Equal(0, Run(copy, "check").ExitCode);
    }

    [Fact]
    public void A_value_made_nullable_by_an_outer_join_is_placed_at_the_table_on_its_optional_side()
    {
        const string Lines = "CREATE TABLE shop.lines (order_id int NOT NULL, sku varchar(10) NOT NULL);";
        const string Joined = "CREATE PROCEDURE shop.list_lines AS\nSELECT o.order_id, l.sku\nFROM shop.orders AS o\nJOIN shop.lines AS l ON l.order_id = o.order_id;";
        using var folder = Generated(Joined, Tables + "\nGO\n" + Lines);
        folder.File("db/procedures.sql", Joined.Replace("JOIN shop.lines", "LEFT JOIN shop.lines", StringComparison.Ordinal));

        var (exitCode, _, error) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("db/procedures.sql:4:11: error HAR6014: shop.list_lines results[0][].sku: became nullable", error, StringComparison.Ordinal);
    }
}

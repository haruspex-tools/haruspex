using System.Text.Json;
using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex explain</c> (<see cref="Explain"/>): why a field is what it is, read from the committed IR alone, with the line of
/// SQL each step refers to when that SQL is at hand and the reference alone when it isn't. The example is the brief's,
/// <c>shop.order_summary</c>: a count, a sum and a <c>MAX</c> over a <c>LEFT JOIN</c> without <c>GROUP BY</c>.
/// </summary>
public class ExplainTests
{
    private const string Tables = """
        CREATE TABLE shop.customer_order (
            order_id int NOT NULL PRIMARY KEY,
            placed_at datetime2(0) NOT NULL);
        GO
        CREATE TABLE shop.order_line (
            line_id int NOT NULL PRIMARY KEY,
            order_id int NOT NULL,
            shipped_at datetime2(0) NULL,
            price decimal(9,2) NOT NULL);
        """;

    private const string OrderSummary = """
        CREATE PROCEDURE shop.order_summary @order_id int AS
        SELECT count(l.line_id) AS line_count,
               sum(l.price) AS total,
               max(l.shipped_at) AS last_shipped,
               o.placed_at
        FROM shop.customer_order AS o
        LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id
        WHERE o.order_id = @order_id;
        """;

    private const string Config = """{ "source": "db", "out": "generated", "schemas": { "shop": { "include": ["*"] } } }""";

    /// <summary>A project with <paramref name="procedures"/>, generated, so its IR is in <c>generated</c>.</summary>
    private static TempDirectory Generated(string procedures = OrderSummary)
    {
        var folder = new TempDirectory();
        folder.File("haruspex.json", Config);
        folder.File("db/tables.sql", Tables);
        folder.File("db/procedures.sql", procedures);
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

    [Fact]
    public void A_field_s_derivation_is_its_column_and_each_rule_with_the_line_of_sql_it_applied_at()
    {
        using var folder = Generated();

        var (exitCode, output, error) = Run(folder, "explain", "shop.order_summary", "last_shipped");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Equal(
            """
            shop.order_summary last_shipped: datetime (local), datetime2(0), nullable
              from shop.order_line.shipped_at (datetime2(0)), declared at tables.sql:8:5
                | shipped_at datetime2(0) NULL,
              null nullable-column at shop.order_line.shipped_at: the column it reads is declared nullable
                | shipped_at datetime2(0) NULL,
              null unmatched-row at procedures.sql:7:11: it comes from the optional side of an outer join, which has no row when nothing matches
                | LEFT JOIN shop.order_line AS l ON l.order_id = o.order_id
              null empty-aggregate at procedures.sql:4:8: it is an aggregate without GROUP BY, which is null over no rows
                | max(l.shipped_at) AS last_shipped,
              to change it: if the data rules it out, declare it in the config file with "nullable": false and why
              page nullable-column: https://github.com/sharpblue/haruspex/blob/main/docs/reasons/nullable-column.md
              page unmatched-row: https://github.com/sharpblue/haruspex/blob/main/docs/reasons/unmatched-row.md
              page empty-aggregate: https://github.com/sharpblue/haruspex/blob/main/docs/reasons/empty-aggregate.md

            """,
            output);
    }

    [Fact]
    public void Without_a_field_it_lists_each_surprise_a_line_each_and_leaves_out_a_field_that_is_as_its_column_declares()
    {
        using var folder = Generated();

        var (exitCode, output, _) = Run(folder, "explain", "shop.order_summary");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
            shop.order_summary: 3 fields differ from what their columns declare.
              line_count    may be null: count-overflow
              total         decimal(38,2), not its column's type; may be null: unmatched-row, empty-aggregate
              last_shipped  may be null: unmatched-row, empty-aggregate
            Run haruspex explain shop.order_summary <field> for a field in full.

            """,
            output);
    }

    [Fact]
    public void Where_the_sql_isn_t_at_hand_each_step_is_its_reference_alone()
    {
        using var folder = Generated();
        using var elsewhere = new TempDirectory();
        File.Copy(Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName), Path.Combine(elsewhere.Path, "app.ir.json"));

        var (exitCode, output, error) = Run(elsewhere, "explain", "shop.order_summary", "last_shipped", "--ir", "app.ir.json");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Contains("  null unmatched-row at procedures.sql:7:11: it comes from the optional side of an outer join, which has no row when nothing matches\n  null empty-aggregate", output, StringComparison.Ordinal);
        Assert.DoesNotContain("|", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_in_a_package_is_shown_only_when_its_sha_256_is_the_one_the_ir_recorded()
    {
        using var folder = new TempDirectory();
        File.Copy(Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "fixtures", "orders.dacpac"), Path.Combine(folder.Path, "orders.dacpac"));
        folder.File("haruspex.json", """{ "source": "orders.dacpac", "out": "generated", "schemas": { "sales": { "include": ["get_order"] } } }""");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        var document = IrJsonReader.Read(File.ReadAllText(Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName)));
        var field = IrFieldPlaces.Fields(document, document.Objects.Single(o => o.Id == "sales.get_order"))
            .First(p => p.Field.From.Count > 0 && p.Field.NullableBecause.Any(r => r.At.Contains(".dacpac/", StringComparison.Ordinal)));

        var shown = Run(folder, "explain", "sales.get_order", field.Path).Output;
        File.AppendAllText(Path.Combine(folder.Path, "orders.dacpac"), " ");
        var changed = Run(folder, "explain", "sales.get_order", field.Path).Output;

        Assert.Contains("    | ", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("|", changed, StringComparison.Ordinal);
        Assert.Equal(shown.Split('\n').Count(l => !l.Contains('|', StringComparison.Ordinal)), changed.Split('\n').Length);
    }

    [Fact]
    public void A_parameter_is_its_declared_type_which_a_caller_may_pass_as_null()
    {
        using var folder = Generated();

        var (exitCode, output, _) = Run(folder, "explain", "shop.order_summary", "@order_id");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("shop.order_summary @order_id: number (int32), int, nullable\n  declared in shop.order_summary, at procedures.sql:1:1; a caller may pass it as null\n", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shop.nothing", null, "error HAR6038: shop.nothing is neither an object nor a type in generated/haruspex.ir.json.")]
    [InlineData("shop.order_summary", "last_shipping", "error HAR6038: shop.order_summary has no field last_shipping; its fields are line_count, total, last_shipped and placed_at.")]
    [InlineData("shop.order_summary", "@id", "error HAR6038: shop.order_summary has no parameter @id; its parameters are @order_id.")]
    public void What_isn_t_in_the_ir_is_a_usage_error_naming_what_is(string target, string? field, string expected)
    {
        using var folder = Generated();

        var (exitCode, _, error) = Run(folder, field is null ? ["explain", target] : ["explain", target, field]);

        Assert.Equal(HaruspexCli.ExitCodes.Usage, exitCode);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_ir_written_yet_it_says_to_generate_first()
    {
        using var folder = new TempDirectory();
        folder.File("haruspex.json", Config);

        var (exitCode, _, error) = Run(folder, "explain", "shop.order_summary");

        Assert.Equal(HaruspexCli.ExitCodes.Failed, exitCode);
        Assert.Contains("error HAR6037: no IR at generated/haruspex.ir.json: run haruspex generate first, or name the file with --ir.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_carries_its_version_and_the_ir_s_codes_with_each_step_s_line()
    {
        using var folder = Generated();

        var (exitCode, output, _) = Run(folder, "explain", "shop.order_summary", "line_count", "--json");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var field = Assert.Single(root.GetProperty("fields").EnumerateArray());
        Assert.Equal("line_count", field.GetProperty("path").GetString());
        Assert.True(field.GetProperty("surprise").GetBoolean());
        var reason = Assert.Single(field.GetProperty("nullableBecause").EnumerateArray());
        Assert.Equal("count-overflow", reason.GetProperty("rule").GetString());
        Assert.Equal("https://github.com/sharpblue/haruspex/blob/main/docs/reasons/count-overflow.md", reason.GetProperty("page").GetString());
        Assert.Equal("SELECT count(l.line_id) AS line_count,", reason.GetProperty("line").GetString());
        Assert.Equal("haruspex explain shop.order_summary line_count", field.GetProperty("explain").GetString());
        Assert.Contains("COUNT_BIG", field.GetProperty("waysOut")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_combined_field_shows_each_database_s_side_labelled_with_its_source()
    {
        using var folder = Generated();
        folder.File("release/db/tables.sql", Tables);
        folder.File("release/db/procedures.sql", OrderSummary.Replace("o.placed_at\n", "o.order_id\n", StringComparison.Ordinal));
        Assert.Equal(0, Run(folder, "generate", "--baseline", "release/db").ExitCode);

        var (exitCode, output, _) = Run(folder, "explain", "shop.order_summary", "placed_at", "--config", "haruspex.json");

        Assert.Equal(0, exitCode);
        Assert.Contains("  baseline (the baseline): doesn't return the key\n", output, StringComparison.Ordinal);
        Assert.Contains("  candidate (db): datetime2(0), always, never null\n    from shop.customer_order.placed_at (datetime2(0)), declared at tables.sql:3:5\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_read_from_an_ir_older_than_0_25_is_said_to_have_no_derivations()
    {
        using var folder = Generated();
        var ir = Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName);
        var old = IrJsonReader.Read(File.ReadAllText(ir));
        var stripped = old with { Types = old.Types.ToDictionary(t => t.Key, t => t.Value with { Fields = [.. t.Value.Fields.Select(f => f with { From = [] })] }) };
        folder.File("release/haruspex.ir.json", IrJsonWriter.Write(stripped));
        Assert.Equal(IrDocument.OneDatabaseVersion, stripped.Version);
        Assert.Equal(0, Run(folder, "generate", "--baseline", "release/haruspex.ir.json").ExitCode);

        var (_, output, _) = Run(folder, "explain", "shop.order_summary", "last_shipped");

        Assert.Contains("  baseline (the baseline): datetime2(0), always, may be null\n    the baseline was read from an IR older than 0.25, which doesn't say where its fields came from\n", output, StringComparison.Ordinal);
    }
}

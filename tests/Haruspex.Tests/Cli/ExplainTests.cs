using System.Text.Json;
using Haruspex.Cli;
using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex explain</c> (<see cref="Explain"/>): why a field is what it is, read from the committed IR alone, with the line of
/// SQL each step refers to when that SQL is at hand and the reference alone when it isn't. The example is
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
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path, FixedBuild.Terminal);
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
              page nullable-column: https://haruspex.tools/reasons/nullable-column
              page unmatched-row: https://haruspex.tools/reasons/unmatched-row
              page empty-aggregate: https://haruspex.tools/reasons/empty-aggregate

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

    [Theory]
    [InlineData("SELECT max(l.shipped_at) AS last_shipped FROM shop.order_line AS l FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;")]
    [InlineData("SELECT JSON_OBJECT('last_shipped': max(l.shipped_at) ABSENT ON NULL) AS summary FROM shop.order_line AS l;")]
    public void A_key_left_out_when_null_names_both_constructs_that_leave_one_out(string query)
    {
        // The IR doesn't say which construct built the object, so the line names both, as the Zod module's header does.
        using var folder = Generated($"CREATE PROCEDURE shop.order_summary AS\n{query}");

        var (exitCode, output, _) = Run(folder, "explain", "shop.order_summary", query.Contains("JSON_OBJECT", StringComparison.Ordinal) ? "summary<json>.last_shipped" : "last_shipped");

        Assert.Equal(0, exitCode);
        Assert.Contains("  absent when null: FOR JSON, and JSON_OBJECT with ABSENT ON NULL, leave out a key whose value is null\n", output, StringComparison.Ordinal);
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
        Assert.Equal("shop.order_summary @order_id: number (int32), int, nullable\n  a parameter of shop.order_summary, which is defined at procedures.sql:1:1; a caller may pass it as null\n", output);
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
    public void A_value_read_from_json_at_a_path_names_the_step_that_may_find_nothing_in_text_and_in_json()
    {
        using var folder = Generated(OrderSummary + """

            GO
            CREATE PROCEDURE shop.last_line AS
            SELECT JSON_VALUE((SELECT TOP (1) l.shipped_at FROM shop.order_line AS l ORDER BY l.line_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '$.shipped_at') AS shipped;
            """);

        var text = Run(folder, "explain", "shop.last_line", "shipped").Output;
        using var json = JsonDocument.Parse(Run(folder, "explain", "shop.last_line", "shipped", "--json").Output);

        Assert.Contains("null json-null-or-absent at procedures.sql:", text, StringComparison.Ordinal);
        Assert.Contains("it is read from JSON, which may hold null or nothing there: `$.shipped_at` is left out when it is null", text, StringComparison.Ordinal);
        var reason = Assert.Single(Assert.Single(json.RootElement.GetProperty("fields").EnumerateArray()).GetProperty("nullableBecause").EnumerateArray(), r => r.GetProperty("rule").GetString() == "json-null-or-absent");
        Assert.Equal("`$.shipped_at` is left out when it is null", reason.GetProperty("detail").GetString());
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
        Assert.Equal("https://haruspex.tools/reasons/count-overflow", reason.GetProperty("page").GetString());
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
        Assert.Contains("  baseline (release/db): doesn't return the key\n", output, StringComparison.Ordinal);
        Assert.Contains("  candidate (db): datetime2(0), always, never null\n    from shop.customer_order.placed_at (datetime2(0)), declared at tables.sql:3:5\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_read_from_an_ir_older_than_0_25_is_said_to_have_no_derivations()
    {
        using var folder = Generated();
        var ir = Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName);
        var old = IrJsonReader.Read(File.ReadAllText(ir));
        var stripped = old with
        {
            Source = old.Source with { Files = [], Path = null, WrittenBy = null },
            Types = old.Types.ToDictionary(t => t.Key, t => t.Value with { Fields = [.. t.Value.Fields.Select(f => f with { From = [] })] }),
        };
        folder.File("release/haruspex.ir.json", IrJsonWriter.Write(stripped));
        Assert.Equal(IrDocument.OneDatabaseVersion, stripped.Version);
        Assert.Equal(0, Run(folder, "generate", "--baseline", "release/haruspex.ir.json").ExitCode);

        var (_, output, _) = Run(folder, "explain", "shop.order_summary", "last_shipped");

        Assert.Contains("  baseline (release/haruspex.ir.json): datetime2(0), always, may be null\n    the baseline was read from an IR older than 0.25, which doesn't say where its fields came from\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_parameter_is_said_to_be_one()
    {
        using var folder = Generated(OrderSummary + "\nGO\nCREATE PROCEDURE shop.count_lines @order_id int, @lines int OUTPUT AS SELECT @lines = count(*) FROM shop.order_line AS l WHERE l.order_id = @order_id;");

        var output = Run(folder, "explain", "shop.count_lines", "@lines").Output;

        Assert.Contains("  an OUTPUT parameter: the procedure may set it, and its function returns the value it ends with, which may be null\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_edited_since_the_ir_was_written_shows_no_line_only_the_reference()
    {
        using var folder = Generated();
        folder.File("db/procedures.sql", "-- A line added above the procedure moves every line of it down.\n" + OrderSummary);

        var (exitCode, output, _) = Run(folder, "explain", "shop.order_summary", "last_shipped");

        Assert.Equal(0, exitCode);
        Assert.Contains("  null unmatched-row at procedures.sql:7:11: it comes from the optional side of an outer join, which has no row when nothing matches\n  null empty-aggregate", output, StringComparison.Ordinal);
        Assert.Contains("    | shipped_at datetime2(0) NULL,\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_location_outside_the_project_or_absolute_is_never_followed_even_with_its_hash_recorded()
    {
        using var folder = Generated();
        using var outside = new TempDirectory();
        var secret = outside.File("secret.sql", "the line nobody should see");
        var ir = Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName);
        var document = IrJsonReader.Read(File.ReadAllText(ir));
        var relative = Path.GetRelativePath(Path.Combine(folder.Path, "db"), secret).Replace('\\', '/');
        var hash = Assert.IsType<string>(ScriptFiles.Sha256(secret));
        string[] places = [relative, secret.Replace('\\', '/')];
        foreach (var place in places)
        {
            var edited = document with
            {
                Source = document.Source with { Files = document.Source.Files.Concat([new KeyValuePair<string, string>(place, hash)]).ToEquatableMap(f => f.Key, f => f.Value) },
                Types = document.Types.ToDictionary(t => t.Key, t => t.Value with { Fields = [.. t.Value.Fields.Select(f => f with { NullableBecause = [.. f.NullableBecause.Select(r => r with { At = place + ":1:1" })] })] }),
            };
            File.WriteAllText(ir, IrJsonWriter.Write(edited));

            var output = Run(folder, "explain", "shop.order_summary", "last_shipped").Output;

            Assert.DoesNotContain("nobody", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_control_character_from_the_sql_is_printed_as_an_escape_never_raw()
    {
        using var folder = Generated(OrderSummary.Replace("max(l.shipped_at) AS last_shipped,", "max(l.shipped_at) AS last_shipped, -- \u001b]0;title\u0007\u001b[31m", StringComparison.Ordinal));

        var output = Run(folder, "explain", "shop.order_summary", "last_shipped").Output;

        Assert.Contains("| max(l.shipped_at) AS last_shipped, -- \\x1B]0;title\\x07\\x1B[31m\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', output);
    }

    [Fact]
    public void An_ir_written_before_0_25_is_said_to_say_nothing_of_where_its_fields_came_from()
    {
        using var folder = Generated();
        var ir = Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName);
        var document = IrJsonReader.Read(File.ReadAllText(ir));
        File.WriteAllText(ir, IrJsonWriter.Write(document with
        {
            Source = document.Source with { Files = [], Path = null, WrittenBy = null },
            Types = document.Types.ToDictionary(t => t.Key, t => t.Value with { Fields = [.. t.Value.Fields.Select(f => f with { From = [] })] }),
        }));

        var output = Run(folder, "explain", "shop.order_summary", "last_shipped").Output;

        Assert.Contains("  this IR is 0.23, which doesn't say where a field came from: run haruspex generate to write it as 0.30 or later\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("|", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[shop].[order_summary]")]
    [InlineData("Shop.Order_Summary")]
    [InlineData("OrderSummary")]
    public void An_object_is_named_as_sql_server_reads_a_name_in_brackets_or_any_case_or_by_its_generated_name(string name)
    {
        using var folder = Generated();

        var (exitCode, output, _) = Run(folder, "explain", name, "line_count");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("shop.order_summary line_count: ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_objects_that_differ_only_in_case_are_refused_by_a_name_that_matches_both_as_written_by_neither()
    {
        // A case-sensitive database could hold both; this one can't, so the IR is given the second by hand.
        using var folder = Generated();
        var ir = Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName);
        var document = IrJsonReader.Read(File.ReadAllText(ir));
        File.WriteAllText(ir, IrJsonWriter.Write(document with { Objects = [.. document.Objects, document.Objects[0] with { Id = "shop.Order_Summary", Name = "OrderSummaryAgain" }] }));

        var (exitCode, _, error) = Run(folder, "explain", "SHOP.ORDER_SUMMARY");

        Assert.Equal(HaruspexCli.ExitCodes.Usage, exitCode);
        Assert.Contains("error HAR6038: SHOP.ORDER_SUMMARY names shop.Order_Summary and shop.order_summary, which differ only in case.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_combined_field_both_databases_give_alike_is_said_to_be_so()
    {
        using var folder = Generated();
        folder.File("release/db/tables.sql", Tables);
        folder.File("release/db/procedures.sql", OrderSummary);
        Assert.Equal(0, Run(folder, "generate", "--baseline", "release/db").ExitCode);

        var output = Run(folder, "explain", "shop.order_summary", "line_count").Output;

        Assert.Contains("  both databases (release/db and db) give it exactly as it is\n", output, StringComparison.Ordinal);
    }
}

using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex check --baseline</c> (<see cref="BaselineCheck"/>): the database being checked meets the application already
/// running, built against the baseline. Each case keeps the project as released in <c>release/db</c>, changes the project as a
/// database change would, generates it as a developer would, with the release as its baseline (so the module is the combined one
/// that <c>check</c> with the same baseline compares with the output folder), and checks it against the release.
/// </summary>
public class BaselineCheckTests
{
    private const string Tables = """
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            customer nvarchar(50) NOT NULL,
            note nvarchar(200) NULL);
        """;

    private const string ListOrders = "CREATE PROCEDURE shop.list_orders AS\nSELECT o.order_id, o.customer FROM shop.orders AS o;";

    private const string Config = """{ "source": "db", "out": "generated", "schemas": { "shop": { "include": ["*"] } } }""";

    /// <summary>
    /// A project released as <paramref name="released"/> (kept in <c>release/db</c>, with its IR in <c>release/haruspex.ir.json</c>),
    /// then changed to <paramref name="now"/> and generated with <paramref name="baseline"/>, so that the output folder is up to date
    /// for a check with the same baseline.
    /// </summary>
    private static TempDirectory Released(string released, string now, string config = Config, string tables = Tables, string? tablesNow = null, string baseline = "release/db")
    {
        var folder = new TempDirectory();
        folder.File("haruspex.json", config);
        folder.File("db/tables.sql", tables);
        folder.File("db/procedures.sql", released);
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("release/db/tables.sql", tables);
        folder.File("release/db/procedures.sql", released);
        folder.File("release/haruspex.ir.json", File.ReadAllText(Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName)));

        folder.File("db/tables.sql", tablesNow ?? tables);
        folder.File("db/procedures.sql", now);
        Assert.Equal(0, Run(folder, "generate", "--baseline", baseline).ExitCode);
        return folder;
    }

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, CliOutput.Shown(output.ToString(), folder.Path), CliOutput.Shown(error.ToString(), folder.Path));
    }

    private static string Edit(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_that_fits_passes_and_says_so()
    {
        using var folder = Released(ListOrders, Edit(ListOrders, "o.customer", "o.customer, o.order_id AS id"));

        var (exitCode, output, error) = Run(folder, "check", "--baseline", "release/db");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "db/procedures.sql:1:1: warning HAR6027: shop.list_orders results[0][].id: added (number (int32), int), and the application's schema strips a key it doesn't list. The application already running sets it aside until it is redeployed.\n",
            error);
        Assert.Equal("haruspex: 1 procedure, no unknowns. generated is up to date, and fits the baseline release/db.\n", output);
    }

    [Fact]
    public void A_change_the_running_application_would_fail_on_fails_the_check_at_the_procedure()
    {
        using var folder = Released(ListOrders, Edit(ListOrders, "o.customer", "o.note AS customer"));

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/db");

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "db/procedures.sql:1:1: error HAR6026: shop.list_orders results[0][].customer: became nullable, and the application's schema refuses null. The application already running, built against the baseline, would fail: ship a change it can take first, and this one once it is redeployed.\n",
            error,
            StringComparison.Ordinal);
        Assert.Contains("error HAR6026: shop.list_orders results[0][].customer: length changed from at most 50 to at most 200", error, StringComparison.Ordinal);
        Assert.EndsWith("haruspex: 2 changes the application already running, built against release/db, would fail on. Ship each in two steps: first a change it can take, then the rest once it is redeployed.\n", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_strict_objects_a_new_column_fails_too()
    {
        using var folder = Released(
            ListOrders,
            Edit(ListOrders, "o.customer", "o.customer, o.order_id AS id"),
            config: Edit(Config, "\"out\"", "\"objects\": \"strict\", \"out\""));

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/db");

        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6026: shop.list_orders results[0][].id: added (number (int32), int), and the application's schema, with \"objects\": \"strict\", refuses a key it doesn't list.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_baseline_may_be_the_ir_file_the_running_application_was_built_with()
    {
        using var folder = Released(ListOrders, Edit(ListOrders, "o.customer", "o.note AS customer"), baseline: "release/haruspex.ir.json");

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6026: shop.list_orders results[0][].customer: became nullable", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_config_file_s_baseline_is_used_and_the_command_line_s_wins()
    {
        using var folder = Released(ListOrders, Edit(ListOrders, "o.customer", "o.note AS customer"));
        folder.File("haruspex.json", Edit(Config, "\"out\"", "\"baseline\": \"release/db\", \"out\""));
        folder.File("now/tables.sql", Tables);
        folder.File("now/procedures.sql", Edit(ListOrders, "o.customer", "o.note AS customer"));

        // The fit alone, since the module in the output folder is combined with one baseline and not the other.
        Assert.Equal(1, Run(folder, "check", "--fit-only").ExitCode);
        Assert.Equal(0, Run(folder, "check", "--fit-only", "--baseline", "now").ExitCode);
    }

    [Fact]
    public void Fit_only_judges_the_fit_alone_as_a_database_s_pipeline_does_against_an_application_s_committed_ir()
    {
        // The module isn't regenerated: a change that fits passes with --fit-only, though the comparison with the output folder,
        // which a baseline otherwise adds the fit to, fails on it.
        using var folder = Released(ListOrders, ListOrders);
        folder.File("db/procedures.sql", Edit(ListOrders, "o.customer", "o.customer, o.note"));

        var (exitCode, output, _) = Run(folder, "check", "--baseline", "generated/haruspex.ir.json", "--fit-only");

        Assert.Equal(0, exitCode);
        Assert.Equal("haruspex: 1 procedure, no unknowns. It fits the baseline generated/haruspex.ir.json.\n", output);
        Assert.Equal(1, Run(folder, "check", "--baseline", "generated/haruspex.ir.json").ExitCode);
    }

    [Fact]
    public void Fit_only_without_a_baseline_is_a_usage_error()
    {
        using var folder = Released(ListOrders, ListOrders);

        var (exitCode, _, error) = Run(folder, "check", "--fit-only");

        Assert.Equal(2, exitCode);
        Assert.StartsWith("haruspex: error HAR6032: --fit-only judges the module against a baseline alone, and none is given.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_that_isn_t_there_is_a_usage_error_saying_haruspex_never_fetches_it()
    {
        using var folder = Released(ListOrders, ListOrders);

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/Shop.dacpac");

        Assert.Equal(2, exitCode);
        Assert.Contains("error HAR6029: the baseline release/Shop.dacpac isn't there. haruspex never fetches it", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("0.22")] // the version before set names
    public void A_baseline_ir_of_another_version_is_refused_naming_both_versions(string version)
    {
        using var folder = Released(ListOrders, ListOrders);
        var ir = Path.Combine(folder.Path, "release", "haruspex.ir.json");
        File.WriteAllText(ir, Edit(File.ReadAllText(ir), $"\"irVersion\": \"{Haruspex.Ir.IrDocument.CurrentVersion}\"", $"\"irVersion\": \"{version}\""));

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(1, exitCode);
        Assert.Contains($"error HAR6030: the baseline release/haruspex.ir.json is IR {version}, and this haruspex reads IR 0.25, 0.24 and 0.23", error, StringComparison.Ordinal);
        Assert.DoesNotContain("HAR6026", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_function_whose_return_shape_flips_is_a_warning_about_the_next_build()
    {
        const string Functions = """{ "source": "db", "out": "generated", "emit": ["zod", "mssql"], "schemas": { "shop": { "include": ["*"] } } }""";
        using var folder = Released(ListOrders, ListOrders + "\nSELECT count(*) AS orders FROM shop.orders;", config: Functions);

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/db");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning HAR6027: shop.list_orders results[1]: added, and the application ignores a result set after those it knows.", error, StringComparison.Ordinal);
        Assert.Contains("warning HAR6028: shop.list_orders: its function now returns { result1, result2 } where it returned the result itself, so each call of it changes when the application is next built", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_renamed_in_the_config_file_is_a_return_shape_warning_not_a_refusal()
    {
        // The database sends sets by place; the name is the key the application's own function returns a set under, which the
        // running application keeps. Its next build reads the set under the new name.
        const string Named = """{ "source": "db", "out": "generated", "emit": ["zod", "mssql"], "schemas": { "shop": { "include": ["*"] } }, "procedures": { "shop.list_orders": { "sets": ["orders"] } } }""";
        using var folder = Released(ListOrders, ListOrders, config: Named);
        folder.File("haruspex.json", Edit(Named, "[\"orders\"]", "[\"rows\"]"));
        Assert.Equal(0, Run(folder, "generate", "--baseline", "release/haruspex.ir.json").ExitCode);

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("HAR6026", error, StringComparison.Ordinal);
        Assert.Contains("warning HAR6028: shop.list_orders: its function now returns { rows } where it returned { orders }", error, StringComparison.Ordinal);
    }

    private const string WithQueries = """{ "source": "db", "out": "generated", "queries": "sql", "schemas": { "shop": { "include": ["*"] } } }""";

    [Fact]
    public void A_query_file_is_judged_against_an_ir_baseline_only_while_its_text_is_the_one_the_running_application_sends()
    {
        using var folder = new TempDirectory();
        folder.File("haruspex.json", WithQueries);
        folder.File("db/tables.sql", Tables);
        folder.File("db/procedures.sql", ListOrders);
        folder.File("sql/names.sql", "SELECT o.customer FROM shop.orders AS o;");
        folder.File("sql/notes.sql", "SELECT o.order_id FROM shop.orders AS o;");
        folder.File("sql/gone.sql", "SELECT o.order_id FROM shop.orders AS o;");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("release/haruspex.ir.json", File.ReadAllText(Path.Combine(folder.Path, "generated", HaruspexCli.IrFileName)));

        // The table changes under the unchanged query; another query's text changes; a third is deleted.
        folder.File("db/tables.sql", Edit(Tables, "customer nvarchar(50) NOT NULL", "customer nvarchar(50) NULL"));
        folder.File("sql/notes.sql", "SELECT o.note FROM shop.orders AS o;");
        File.Delete(Path.Combine(folder.Path, "sql", "gone.sql"));
        Assert.Equal(0, Run(folder, "generate").ExitCode);

        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6026: sql/names.sql results[0][].customer: became nullable", error, StringComparison.Ordinal);
        Assert.Contains("warning HAR6031: sql/notes.sql: its text changed since the baseline", error, StringComparison.Ordinal);
        Assert.Contains("warning HAR6031: sql/gone.sql: the file is gone", error, StringComparison.Ordinal);
        Assert.DoesNotContain("HAR6026: sql/notes.sql", error, StringComparison.Ordinal);
        Assert.DoesNotContain("HAR6026: sql/gone.sql", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_is_for_one_module_and_is_refused_with_several()
    {
        using var folder = Released(ListOrders, ListOrders);

        var (exitCode, _, error) = Run(folder, "check", "--all", "--baseline", "release/db");

        Assert.Equal(2, exitCode);
        Assert.Contains("error HAR6019: a source, --include, --out and --baseline are for one module", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Fit_only_is_check_s_alone()
    {
        using var folder = Released(ListOrders, ListOrders);

        Assert.Equal(2, Run(folder, "generate", "--fit-only").ExitCode);
    }
}

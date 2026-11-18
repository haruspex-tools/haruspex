using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex generate --baseline</c> (<see cref="CombinedTypes"/>): types that work against the database before the change is
/// deployed (the baseline, kept in <c>release/db</c>) and after it (the source, <c>db</c>). The combining rules are
/// <c>IrCombinationTests</c>'; here, what the command writes and says, and how <c>check</c> agrees with it.
/// </summary>
public class GenerateBaselineTests
{
    private const string Tables = """
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            status varchar(10) NOT NULL CHECK (status IN ('open', 'closed')),
            channel varchar(10) NULL);
        """;

    private const string GetOrder = "CREATE PROCEDURE shop.get_order @order_id int AS\nSELECT o.order_id, o.status FROM shop.orders AS o WHERE o.order_id = @order_id;";

    private const string ListOrders = "CREATE PROCEDURE shop.list_orders AS\nSELECT o.order_id FROM shop.orders AS o;";

    private const string Config = """{ "source": "db", "out": "generated", "emit": ["zod", "mssql"], "schemas": { "shop": { "include": ["*"] } } }""";

    /// <summary>A project released as <paramref name="released"/> (in <c>release/db</c>), and now <paramref name="now"/> (in <c>db</c>).</summary>
    private static TempDirectory Project(string released, string now, string config = Config, string tablesNow = Tables)
    {
        var folder = new TempDirectory();
        folder.File("haruspex.json", config);
        folder.File("release/db/tables.sql", Tables);
        folder.File("release/db/procedures.sql", released);
        folder.File("db/tables.sql", tablesNow);
        folder.File("db/procedures.sql", now);
        return folder;
    }

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, CliOutput.Shown(output.ToString(), folder.Path), CliOutput.Shown(error.ToString(), folder.Path));
    }

    private static string Written(TempDirectory folder, string file) => File.ReadAllText(Path.Combine(folder.Path, "generated", file));

    private static string Edit(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    [Fact]
    public void The_module_is_written_from_the_combined_document_which_names_its_baseline()
    {
        // The brief's example: channel only in the new database, and a status the new database allows.
        using var folder = Project(GetOrder, Edit(GetOrder, "o.status FROM", "o.status, o.channel FROM"), tablesNow: Edit(Tables, "('open', 'closed')", "('open', 'closed', 'held')"));

        var (exitCode, output, error) = Run(folder, "generate", "--baseline", "release/db");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.StartsWith("haruspex: 1 procedure, no unknowns. Wrote generated/haruspex.ir.json, generated/haruspex.zod.ts and generated/haruspex.mssql.ts. Its types work against the baseline release/db too.\n", output, StringComparison.Ordinal);
        var document = IrJsonReader.Read(Written(folder, HaruspexCli.IrFileName));
        Assert.Equal(new IrSource("tsql"), document.Source.Baseline);
        Assert.Equal("0.24", document.Version);
        var zod = Written(folder, HaruspexCli.ZodFileName);
        Assert.Contains("channel: z.string().max(10).nullable().optional()", zod, StringComparison.Ordinal);
        Assert.Contains("One of \"open\", \"closed\", \"held\", as the database compares strings.", zod, StringComparison.Ordinal);
        Assert.Contains("// Combined with a baseline: results accept what either database returns, and parameters take only what both accept.", Written(folder, HaruspexCli.MssqlFileName), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_baseline_the_module_is_what_it_was()
    {
        using var folder = Project(GetOrder, GetOrder);

        Assert.Equal(0, Run(folder, "generate").ExitCode);

        Assert.StartsWith("{\n  \"irVersion\": \"0.23\",\n", Written(folder, HaruspexCli.IrFileName), StringComparison.Ordinal);
        Assert.DoesNotContain("baseline", Written(folder, HaruspexCli.IrFileName), StringComparison.Ordinal);
        Assert.DoesNotContain("Combined", Written(folder, HaruspexCli.ZodFileName), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_that_can_t_be_bridged_fails_the_run_at_its_procedure_and_the_module_is_written_for_the_rest()
    {
        var released = GetOrder + "\nGO\n" + ListOrders;
        using var folder = Project(released, Edit(released, "@order_id int AS", "@order_id int, @channel varchar(10) AS"));

        var (exitCode, _, error) = Run(folder, "generate", "--baseline", "release/db");

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "db/procedures.sql:1:1: error HAR6033: shop.get_order parameters[\"@channel\"]: gained the required parameter @channel, which the baseline's procedure doesn't take: give it a default first, or ship the change in two releases. Until then the module leaves out its function and types, since none works against both databases.\n"
            + "haruspex: 1 change no one function bridges, each procedure left out of the module written. Ship each in two releases.\n",
            error);
        var functions = Written(folder, HaruspexCli.MssqlFileName);
        Assert.Contains("export async function listOrders(", functions, StringComparison.Ordinal);
        Assert.DoesNotContain("getOrder", functions, StringComparison.Ordinal);
        Assert.DoesNotContain("shop.get_order", Written(folder, HaruspexCli.IrFileName), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_with_the_same_baseline_finds_the_combined_module_up_to_date_and_fails_on_what_can_t_be_bridged()
    {
        using var folder = Project(GetOrder, Edit(GetOrder, "o.status FROM", "o.status, o.channel FROM"));
        Assert.Equal(0, Run(folder, "generate", "--baseline", "release/db").ExitCode);

        Assert.Equal(0, Run(folder, "check", "--baseline", "release/db").ExitCode);
        Assert.Equal(1, Run(folder, "check").ExitCode);

        folder.File("db/procedures.sql", Edit(GetOrder, "@order_id int AS", "@order_id int, @channel varchar(10) AS"));
        var (exitCode, _, error) = Run(folder, "check", "--baseline", "release/db");
        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6033: shop.get_order parameters[\"@channel\"]: gained the required parameter @channel", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_config_file_s_baseline_is_used_by_generate_and_the_command_line_s_wins()
    {
        using var folder = Project(GetOrder, Edit(GetOrder, "o.status FROM", "o.status, o.channel FROM"), config: Edit(Config, "\"out\"", "\"baseline\": \"release/db\", \"out\""));

        Assert.Equal(0, Run(folder, "generate").ExitCode);
        Assert.Contains("channel: z.string().max(10).nullable().optional()", Written(folder, HaruspexCli.ZodFileName), StringComparison.Ordinal);

        Assert.Equal(0, Run(folder, "generate", "--baseline", "db").ExitCode);
        Assert.Contains("channel: z.string().max(10).nullable(),", Written(folder, HaruspexCli.ZodFileName), StringComparison.Ordinal);
    }

    [Fact]
    public void A_possible_rename_and_a_procedure_in_one_database_are_warnings_and_a_return_shape_that_changes_too()
    {
        using var folder = Project(
            GetOrder + "\nGO\n" + ListOrders,
            Edit(GetOrder, "o.status FROM", "o.status AS state FROM") + "\nSELECT count(*) AS orders FROM shop.orders;");

        var (exitCode, _, error) = Run(folder, "generate", "--baseline", "release/db");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning HAR6034: shop.get_order results[0][]: status went and state appeared; if it was renamed, read both until the baseline moves on.", error, StringComparison.Ordinal);
        Assert.Contains("warning HAR6035: shop.list_orders: the candidate's database doesn't have it, so the application can't call it once the change is deployed", error, StringComparison.Ordinal);
        Assert.Contains("warning HAR6028: shop.get_order: its function now returns { result1, result2 } where it returned the result itself", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_combined_ir_is_refused_as_the_database_to_combine_with_but_not_by_a_fit_alone()
    {
        using var folder = Project(GetOrder, Edit(GetOrder, "o.status FROM", "o.status, o.channel FROM"));
        Assert.Equal(0, Run(folder, "generate", "--baseline", "release/db").ExitCode);
        folder.File("release/haruspex.ir.json", Written(folder, HaruspexCli.IrFileName));

        var (exitCode, _, error) = Run(folder, "generate", "--baseline", "release/haruspex.ir.json");

        Assert.Equal(1, exitCode);
        Assert.StartsWith("haruspex: error HAR6036: the baseline release/haruspex.ir.json is a combined IR", error, StringComparison.Ordinal);
        Assert.Equal(0, Run(folder, "check", "--baseline", "release/haruspex.ir.json", "--fit-only").ExitCode);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    public void Named_sets_a_change_adds_to_or_takes_from_are_read_from_a_source_baseline_without_stopping_the_run(int released, int now)
    {
        // The config file names the source's sets; read as a baseline, a list of another length names what the baseline returns.
        static string Procedure(int sets) => "CREATE PROCEDURE shop.get_sheet AS\n" + string.Join("\n", Enumerable.Range(1, sets).Select(i => $"SELECT CAST({i} AS int) AS v{i};"));
        string[] names = ["head", "lines", "notes"];
        var config = Edit(Config, "\"schemas\"", $"\"procedures\": {{ \"shop.get_sheet\": {{ \"sets\": [{string.Join(", ", names.Take(now).Select(n => $"\"{n}\""))}] }} }}, \"schemas\"");
        using var folder = Project(Procedure(released), Procedure(now), config);

        var (exitCode, _, error) = Run(folder, "generate", "--baseline", "release/db");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("HAR4112", error, StringComparison.Ordinal);
        Assert.Equal([.. names.Take(now)], IrJsonReader.Read(Written(folder, HaruspexCli.IrFileName)).Objects.Single().SetNames);

        // The fit is judged, not stopped: a set gone is one the application already running counts on.
        var (checkExit, _, checkError) = Run(folder, "check", "--baseline", "release/db", "--fit-only");
        Assert.Equal(now < released ? 1 : 0, checkExit);
        Assert.Equal(now < released, checkError.Contains("error HAR6026: shop.get_sheet results[2]: removed", StringComparison.Ordinal));
    }
}

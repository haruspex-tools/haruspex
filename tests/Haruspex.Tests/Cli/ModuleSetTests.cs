using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// Several modules in one run: <c>generate</c> and <c>check</c> take several <c>--config</c> files, or find every
/// <c>haruspex.json</c> below a folder with <c>--all</c>. Each is a module of its own, run and reported apart, and the run fails
/// if any does. An application with two plugins, <c>plugins/orders</c> and <c>plugins/returns</c>, each with its own config.
/// </summary>
public class ModuleSetTests
{
    private const string Tables = """
        CREATE TABLE shop.orders (order_id int NOT NULL PRIMARY KEY, customer nvarchar(50) NOT NULL, note nvarchar(200) NULL);
        CREATE TABLE shop.returns (return_id int NOT NULL PRIMARY KEY, order_id int NOT NULL, reason varchar(20) NULL);
        """;

    private const string Procedures = """
        CREATE PROCEDURE shop.list_orders AS SELECT o.order_id, o.customer FROM shop.orders AS o;
        GO
        CREATE PROCEDURE shop.list_returns AS SELECT r.return_id, r.reason FROM shop.returns AS r;
        """;

    /// <summary>The application: the database in <c>db</c>, and a plugin for orders and one for returns, each selecting its procedure.</summary>
    private static TempDirectory Application()
    {
        var folder = new TempDirectory();
        folder.File("db/tables.sql", Tables);
        folder.File("db/procedures.sql", Procedures);
        folder.File("plugins/orders/haruspex.json", """{ "source": "../../db", "out": "generated", "schemas": { "shop": { "include": ["list_orders"] } } }""");
        folder.File("plugins/returns/haruspex.json", """{ "source": "../../db", "out": "generated", "schemas": { "shop": { "include": ["list_returns"] } } }""");
        return folder;
    }

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, Shown(output, folder), Shown(error, folder));
    }

    private static string Shown(StringWriter writer, TempDirectory folder) => CliOutput.Shown(writer.ToString(), folder.Path);

    [Fact]
    public void Generate_with_two_config_files_writes_each_module_beside_its_config()
    {
        using var folder = Application();

        var (exitCode, output, error) = Run(folder, "generate", "--config", "plugins/orders/haruspex.json", "-c", "plugins/returns/haruspex.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
            haruspex: module plugins/orders/haruspex.json
            haruspex: module plugins/returns/haruspex.json

            """,
            error);
        Assert.Equal(
            """
            haruspex: 1 procedure, no unknowns. Wrote plugins/orders/generated/haruspex.ir.json and plugins/orders/generated/haruspex.zod.ts.
            First output in plugins/orders/generated, so nothing to compare with.
            haruspex: 1 procedure, no unknowns. Wrote plugins/returns/generated/haruspex.ir.json and plugins/returns/generated/haruspex.zod.ts.
            First output in plugins/returns/generated, so nothing to compare with.
            haruspex: 2 modules, none failed.

            """,
            output);
        Assert.Contains("ListOrders", File.ReadAllText(Path.Combine(folder.Path, "plugins", "orders", "generated", HaruspexCli.ZodFileName)), StringComparison.Ordinal);
        Assert.DoesNotContain("ListReturns", File.ReadAllText(Path.Combine(folder.Path, "plugins", "orders", "generated", HaruspexCli.ZodFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public void All_finds_every_config_below_the_folder_in_path_order_and_leaves_out_hidden_folders_and_node_modules()
    {
        using var folder = Application();
        folder.File("node_modules/some-package/haruspex.json", "{ not even JSON");
        folder.File(".cache/haruspex.json", "{ not even JSON");

        var (exitCode, output, _) = Run(folder, "generate", "--all");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "haruspex: 1 procedure, no unknowns. Wrote plugins/orders/generated/haruspex.ir.json and plugins/orders/generated/haruspex.zod.ts.",
                "First output in plugins/orders/generated, so nothing to compare with.",
                "haruspex: 1 procedure, no unknowns. Wrote plugins/returns/generated/haruspex.ir.json and plugins/returns/generated/haruspex.zod.ts.",
                "First output in plugins/returns/generated, so nothing to compare with.",
                "haruspex: 2 modules, none failed.",
            ],
            output.TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public void All_leaves_out_a_repository_and_a_worktree_nested_below_since_they_are_other_checkouts()
    {
        using var folder = Application();
        folder.File("plugins/vendored/.git/HEAD", "ref: refs/heads/main\n");
        folder.File("plugins/vendored/haruspex.json", "{ not even JSON");
        folder.File("plugins/worktree/.git", "a worktree's pointer to its repository\n");
        folder.File("plugins/worktree/haruspex.json", "{ not even JSON");

        var (exitCode, output, _) = Run(folder, "generate", "--all");

        Assert.Equal(0, exitCode);
        Assert.EndsWith("haruspex: 2 modules, none failed.\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_modules_that_write_to_one_folder_are_refused_before_either_runs()
    {
        using var folder = Application();
        folder.File("plugins/returns/haruspex.json", """{ "source": "../../db", "out": "../orders/generated", "schemas": { "shop": { "include": ["list_returns"] } } }""");

        var (exitCode, _, error) = Run(folder, "generate", "--all");

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "haruspex: error HAR6020: plugins/orders/haruspex.json and plugins/returns/haruspex.json both write to plugins/orders/generated, so one module would overwrite the other's. Give each module its own \"out\".\n",
            error);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "plugins", "orders", "generated")));
    }

    [Fact]
    public void All_takes_the_folder_to_search_below()
    {
        using var folder = Application();
        folder.File("haruspex.json", """{ "source": "db", "out": "generated", "schemas": { "shop": { "include": ["*"] } } }""");

        var (exitCode, output, _) = Run(folder, "generate", "--all", "plugins");

        Assert.Equal(0, exitCode);
        Assert.EndsWith("haruspex: 2 modules, none failed.\n", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "generated")));
        Assert.EndsWith("haruspex: 1 module, none failed.\n", Run(folder, "generate", "--all", "plugins/orders").Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_of_every_module_fails_when_one_does_and_names_it()
    {
        using var folder = Application();
        Assert.Equal(0, Run(folder, "generate", "--all").ExitCode);
        folder.File("db/tables.sql", Tables.Replace("reason varchar(20) NULL", "reason varchar(40) NULL", StringComparison.Ordinal));

        var (exitCode, output, error) = Run(folder, "check", "--all");

        Assert.Equal(1, exitCode);
        Assert.Contains("haruspex: module plugins/returns/haruspex.json\n", error, StringComparison.Ordinal);
        Assert.Contains("error HAR6014: shop.list_returns results[0][].reason: SQL type changed from varchar(20) to varchar(40)", error, StringComparison.Ordinal);
        Assert.EndsWith("haruspex: 2 modules; 1 failed: plugins/returns/haruspex.json.\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_exit_code_of_several_modules_is_the_worst_of_theirs()
    {
        using var folder = Application();
        Assert.Equal(0, Run(folder, "generate", "--all").ExitCode);
        folder.File("db/tables.sql", Tables.Replace("reason varchar(20) NULL", "reason varchar(40) NULL", StringComparison.Ordinal));
        folder.File("plugins/orders/haruspex.json", """{ "source": "../../db", "out": "generated", "colour": "blue" }""");

        var (exitCode, output, _) = Run(folder, "check", "--all");

        Assert.Equal(2, exitCode);
        Assert.EndsWith("haruspex: 2 modules; 2 failed: plugins/orders/haruspex.json, plugins/returns/haruspex.json.\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void One_config_file_runs_as_it_always_has_with_no_module_lines()
    {
        using var folder = Application();

        var (exitCode, output, error) = Run(folder, "generate", "--config", "plugins/orders/haruspex.json");

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.DoesNotContain("module", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--all", "--config", "plugins/orders/haruspex.json")]
    [InlineData("--all", "--out", "generated")]
    [InlineData("--all", "--include", "shop.*")]
    [InlineData("db", "--config", "plugins/orders/haruspex.json", "--config", "plugins/returns/haruspex.json")]
    public void An_option_for_one_module_with_several_is_a_usage_error(params string[] args)
    {
        using var folder = Application();

        var (exitCode, _, error) = Run(folder, ["generate", .. args]);

        Assert.Equal(2, exitCode);
        Assert.Contains("error HAR6019:", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "plugins", "orders", "generated")));
    }

    [Theory]
    [InlineData("db")]
    [InlineData("nowhere")]
    public void All_with_no_config_below_the_folder_is_a_usage_error(string below)
    {
        using var folder = Application();

        var (exitCode, _, error) = Run(folder, "check", "--all", below);

        Assert.Equal(2, exitCode);
        Assert.Contains("error HAR6018:", error, StringComparison.Ordinal);
    }
}

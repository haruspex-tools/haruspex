using System.Text;
using Haruspex.Diagnostics;

namespace Haruspex.Tests.Config;

/// <summary>
/// Which query files <c>queries</c> names: a folder's <c>.sql</c> files at any depth, a file, or a pattern, each of the module
/// whose config file is nearest above it; an entry that names none is an error. An application with its queries in <c>sql</c>,
/// a report beside them, and a plugin of its own in <c>plugins/returns</c>.
/// </summary>
public class QuerySelectionTests
{
    private static TempDirectory Application()
    {
        var folder = new TempDirectory();
        folder.File("sql/get_order.sql", "SELECT 1 AS one;");
        folder.File("sql/orders/list_orders.sql", "SELECT 1 AS one;");
        folder.File("sql/orders/notes.txt", "not a query");
        folder.File("reports/monthly.sql", "SELECT 1 AS one;");
        folder.File("plugins/returns/haruspex.json", "{}");
        folder.File("plugins/returns/sql/list_returns.sql", "SELECT 1 AS one;");
        folder.File(".cache/old.sql", "SELECT 1 AS one;");
        folder.File("node_modules/pkg/q.sql", "SELECT 1 AS one;");
        return folder;
    }

    private static Outcome<IReadOnlyList<QueryFileFound>> Select(string queries)
    {
        using var folder = Application();
        var config = ConfigReader.Read(Encoding.UTF8.GetBytes($$"""{ "queries": {{queries}} }"""), "haruspex.json", folder.Path).Expected();
        return QuerySelection.Select(config);
    }

    private static IReadOnlyList<string> Selected(string queries) => [.. Select(queries).Expected().Select(f => f.Relative)];

    [Fact]
    public void A_folder_names_its_sql_files_at_any_depth_in_path_order() =>
        Assert.Equal(["sql/get_order.sql", "sql/orders/list_orders.sql"], Selected("\"sql\""));

    [Fact]
    public void A_pattern_matches_relative_paths_with_double_star_for_any_folders() =>
        Assert.Equal(["sql/orders/list_orders.sql"], Selected("\"sql/**/list_*.sql\""));

    [Fact]
    public void A_single_star_stays_in_its_folder() =>
        Assert.Equal(["sql/get_order.sql"], Selected("\"sql/*.sql\""));

    [Fact]
    public void Entries_together_name_each_file_once() =>
        Assert.Equal(["reports/monthly.sql", "sql/get_order.sql", "sql/orders/list_orders.sql"], Selected("[\"sql\", \"sql/get_order.sql\", \"reports/monthly.sql\"]"));

    [Fact]
    public void The_config_file_s_own_folder_leaves_out_another_module_s_files_hidden_folders_and_node_modules() =>
        Assert.Equal(["reports/monthly.sql", "sql/get_order.sql", "sql/orders/list_orders.sql"], Selected("\".\""));

    [Fact]
    public void An_entry_that_names_another_module_s_files_is_refused_naming_that_module()
    {
        var problem = Assert.Single(Select("\"plugins/returns/sql\"").Diagnostics);

        Assert.Equal(
            "haruspex.json:1:14: error HAR4007: queries: \"plugins/returns/sql\" names no query file of this module: plugins/returns holds a haruspex.json of its own, so the query files below it are that module's. Correct it, or remove it.",
            problem.ToString());
    }

    [Fact]
    public void An_entry_that_names_nothing_is_refused() =>
        Assert.Equal(
            "haruspex.json:1:22: error HAR4007: queries[1]: \"sql/**/*.query\" names no query file of this module. Correct it, or remove it.",
            Assert.Single(Select("[\"sql\", \"sql/**/*.query\"]").Diagnostics).ToString());
}

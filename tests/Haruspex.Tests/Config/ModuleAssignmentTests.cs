using System.Text;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Config;

/// <summary>
/// Which module each selected procedure and query file is in, by the config file's <c>split</c>
/// (docs/decisions/0074-a-config-may-split-its-output-into-modules.md).
/// </summary>
public class ModuleAssignmentTests
{
    private static HaruspexConfig Config(string split) =>
        ConfigReader.Read(Encoding.UTF8.GetBytes($$"""{ {{split}} }"""), "haruspex.json", Path.GetTempPath()).Expected();

    private static readonly ObjectName[] Procedures = [new("sales", "get_order"), new("shop", "get_product"), new("admin", "list_users")];

    private static QueryFile Query(string path, string text = "SELECT 1 AS one;\n") => QueryFileReader.Parse(text, path).Expected();

    private static Outcome<EquatableMap<string, string>> Assign(string split, ObjectName[]? procedures = null, params QueryFile[] queries) =>
        ModuleAssignment.Assign(Config(split), procedures ?? Procedures, queries);

    [Fact]
    public void By_schema_each_procedure_is_in_its_schema_s_module_and_a_query_file_in_the_one_it_names_or_queries()
    {
        var modules = Assign("\"split\": \"schema\"", null, Query("sql/a.sql"), Query("sql/b.sql", "-- @module sales\nSELECT 1 AS one;\n")).Expected();

        Assert.Equal("sales", modules["sales.get_order"]);
        Assert.Equal("shop", modules["shop.get_product"]);
        Assert.Equal("queries", modules["sql/a.sql"]);
        Assert.Equal("sales", modules["sql/b.sql"]);
    }

    [Fact]
    public void Listed_modules_hold_their_schemas_compared_ignoring_case()
    {
        var modules = Assign("\"split\": { \"sales\": [\"Sales\", \"shop\"], \"admin\": [\"admin\"], \"reports\": [] }", null, Query("sql/r.sql", "-- @module reports\nSELECT 1 AS one;\n")).Expected();

        Assert.Equal(["admin", "sales", "sales", "reports"], modules.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Value));
    }

    [Fact]
    public void Without_a_split_nothing_is_assigned_and_a_module_comment_is_a_warning()
    {
        var assigned = Assign("", null, Query("sql/r.sql", "-- @module reports\nSELECT 1 AS one;\n"));

        Assert.Empty(assigned.Expected());
        Assert.Equal(
            ["sql/r.sql:1:1: warning HAR4009: -- @module reports: the config file doesn't split its output into modules, so the comment does nothing. Add \"split\" to the config file, or remove the comment."],
            assigned.Diagnostics.Select(d => d.ToString()));
    }

    [Fact]
    public void A_procedure_whose_schema_no_module_lists_is_refused_naming_it()
    {
        var assigned = Assign("\"split\": { \"sales\": [\"sales\", \"shop\"] }");

        Assert.IsType<Failed<EquatableMap<string, string>>>(assigned);
        Assert.Equal(
            ["haruspex.json:1:12: error HAR4008: split: the procedure admin.list_users is in no module: no module lists its schema, admin. Add the schema to a module's list, or leave the procedure out of the selection."],
            assigned.Diagnostics.Select(d => d.ToString()));
    }

    [Theory]
    [InlineData("\"split\": { \"sales\": [\"sales\", \"shop\", \"admin\"] }", "-- @module sale: no module is named sale; the modules are queries and sales.")]
    [InlineData("\"split\": \"schema\"", "-- @module shared: no module is named shared; the modules are admin, queries, sales and shop.")]
    public void A_query_file_naming_a_module_the_split_does_not_have_is_refused_at_its_comment(string split, string why)
    {
        var name = why[11..why.IndexOf(':', StringComparison.Ordinal)];

        var assigned = Assign(split, null, Query("sql/q.sql", $"-- @module {name}\nSELECT 1 AS one;\n"));

        Assert.Equal([$"sql/q.sql:1:1: error HAR4008: {why}"], assigned.Diagnostics.Select(d => d.ToString()));
    }

    [Fact]
    public void By_schema_a_schema_that_can_t_name_a_module_is_refused()
    {
        var assigned = Assign("\"split\": \"schema\"", [new("sales team", "get_order")]);

        Assert.Equal(
            ["haruspex.json:1:12: error HAR3024: split: the schema 'sales team' can't name a module: a module's name starts with a letter and has only letters, digits, '_' and '-', since it names the module's files. List the modules instead, with a name for its module."],
            assigned.Diagnostics.Select(d => d.ToString()));
    }

    [Fact]
    public void By_schema_two_modules_that_differ_only_in_case_are_refused()
    {
        var assigned = Assign("\"split\": \"schema\"", [new("sales", "get_order"), new("Sales", "get_total")]);

        Assert.Equal(
            ["haruspex.json:1:12: error HAR3024: split: the modules Sales and sales differ only in case, and a file system that ignores case would hold one file for both. List the modules instead, naming one of them otherwise."],
            assigned.Diagnostics.Select(d => d.ToString()));
    }
}

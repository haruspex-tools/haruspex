using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Cli;

/// <summary>
/// Query files through the command line: <c>queries</c> in the config file names them, each is read against the project, named
/// from its file by the config file's naming rules, and typed with the procedures, its locations under the config file's folder.
/// </summary>
public partial class CliTests
{
    private const string ShopTables = """
        CREATE TABLE shop.orders (order_id int NOT NULL PRIMARY KEY, customer_id int NOT NULL, note nvarchar(200) NULL);
        GO
        CREATE PROCEDURE shop.get_order @order_id int AS SELECT o.order_id, o.note FROM shop.orders AS o WHERE o.order_id = @order_id;
        """;

    /// <summary>An application with its database in <c>db</c> and its query files in <c>sql</c>, and a config file of <paramref name="settings"/>.</summary>
    private static TempDirectory Shop(string settings, params (string File, string Sql)[] queries)
    {
        var folder = new TempDirectory();
        folder.File("db/shop.sql", ShopTables);
        foreach (var (file, sql) in queries)
            folder.File(file, sql);
        folder.File("haruspex.json", $$"""{ "source": "db", "out": "out", {{settings}} }""");
        return folder;
    }

    private static IrDocument Written(TempDirectory folder) => IrJsonReader.Read(File.ReadAllText(Path.Combine(folder.Path, "out", HaruspexCli.IrFileName)));

    [Fact]
    public void Generate_types_the_query_files_beside_the_procedures_named_by_the_naming_rules()
    {
        using var folder = Shop(
            "\"queries\": \"sql\", \"schemas\": { \"shop\": { \"include\": [\"*\"] } }",
            ("sql/list_orders_of.sql", "SELECT o.order_id FROM shop.orders AS o WHERE o.customer_id = @customer_id;"),
            ("sql/notes/orderNote.sql", "SELECT o.note FROM shop.orders AS o WHERE o.order_id = @order_id;"));

        var (exitCode, output, error) = RunIn(folder.Path, "generate");

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Equal("haruspex: 1 procedure, 2 query files, no unknowns. Wrote out/haruspex.ir.json and out/haruspex.zod.ts.\n", CliOutput.Shown(output, folder.Path));
        Assert.Equal(
            [("shop.get_order", "GetOrder", IrObjectKind.Procedure), ("sql/list_orders_of.sql", "ListOrdersOf", IrObjectKind.Query), ("sql/notes/orderNote.sql", "OrderNote", IrObjectKind.Query)],
            Written(folder).Objects.Select(o => (o.Id, o.Name, o.Kind)));
    }

    [Fact]
    public void Query_files_alone_are_a_selection_and_camel_case_names_them_as_written()
    {
        using var folder = Shop("\"queries\": [\"sql/*.sql\"], \"naming\": { \"case\": \"camel\" }", ("sql/getOrderCard.sql", "SELECT o.note FROM shop.orders AS o WHERE o.order_id = @id;"));

        var (exitCode, _, error) = RunIn(folder.Path, "generate");

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Equal(["getOrderCard"], Written(folder).Objects.Select(o => o.Name));
    }

    [Fact]
    public void A_query_file_named_as_a_procedure_is_is_an_error_naming_both()
    {
        using var folder = Shop("\"queries\": \"sql\", \"schemas\": { \"shop\": { \"include\": [\"*\"] } }", ("sql/get-order.sql", "SELECT 1 AS one;"));

        var (exitCode, _, error) = RunIn(folder.Path, "generate");

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "haruspex.json:1:44: error HAR4303: queries: \"GetOrder\" would name both the procedure shop.get_order and the query file sql/get-order.sql. Generated names must not clash: rename the query file, or give the procedure its own name with a \"name\" override in \"procedures\", or leave it out of \"schemas\".\n",
            CliOutput.Shown(error, folder.Path));
    }

    [Fact]
    public void A_query_file_the_rules_name_as_a_reserved_word_is_told_to_rename_the_file()
    {
        using var folder = Shop("\"queries\": \"sql\", \"naming\": { \"case\": \"camel\" }", ("sql/delete.sql", "SELECT 1 AS one;"));

        var (exitCode, _, error) = RunIn(folder.Path, "generate");

        Assert.Equal(2, exitCode);
        Assert.EndsWith("the query file sql/delete.sql cannot be named: the rules give 'delete', which is a reserved word in TypeScript. Rename the query file.\n", CliOutput.Shown(error, folder.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_query_file_that_does_not_read_is_reported_where_it_is_and_nothing_is_written()
    {
        using var folder = Shop("\"queries\": \"sql\"", ("sql/broken.sql", "SELECT 1 AS one;\nGO\nSELECT 2 AS two;\n"));

        var (exitCode, _, error) = RunIn(folder.Path, "generate");

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "sql/broken.sql:1:1: error HAR1014: sql/broken.sql isn't one batch: GO splits it into 2 batches; GO is no part of SQL, but a client's separator, and a query file is run as one batch.\nharuspex: 1 error; nothing was written.\n",
            CliOutput.Shown(error, folder.Path));
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "out")));
    }

    [Fact]
    public void An_undecided_parameter_is_a_note_at_its_first_use_in_the_query_file()
    {
        using var folder = Shop("\"queries\": \"sql\"", ("sql/label.sql", "SELECT @label AS label;"));

        var (exitCode, _, error) = RunIn(folder.Path, "generate", "--verbose");

        Assert.Equal(0, exitCode);
        Assert.StartsWith(
            "sql/label.sql (2 unknowns)\nsql/label.sql:1:8: note HAR2024: sql/label.sql parameters.@label: No use of @label decides its type:",
            CliOutput.Shown(error, folder.Path),
            StringComparison.Ordinal);
    }
}

using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

// A column or star qualified by its table's schema and name, the rules Binder.Resolve and Binder.Star follow for one.
public partial class BindingObservationTests
{
    /// <summary>A schema of its own, which CREATE SCHEMA needs a batch for.</summary>
    private const string Shops = "CREATE SCHEMA obs_shop;";

    /// <summary>Two tables of one name in two schemas, and one only obs_shop has.</summary>
    private const string ShopTables = """
        CREATE TABLE obs_shop.obs_orders (order_id int NOT NULL PRIMARY KEY, status varchar(10) NOT NULL);
        INSERT INTO obs_shop.obs_orders VALUES (1, 'open'), (2, 'shut');
        CREATE TABLE dbo.obs_orders (order_id int NOT NULL PRIMARY KEY, note int NULL);
        INSERT INTO dbo.obs_orders VALUES (7, NULL);
        CREATE TABLE obs_shop.only_here (n int NOT NULL);
        INSERT INTO obs_shop.only_here VALUES (5);
        """;

    private Task<CallOutcome> Shop(string call, params string[] more) => Run([Shops, ShopTables, .. more], call);

    [Fact]
    public async Task A_column_of_three_parts_binds_to_the_table_named_so_in_from() =>
        Assert.Equal(
            "order_id,status: 1,open",
            (await Shop("SELECT obs_shop.obs_orders.order_id, obs_shop.obs_orders.status FROM obs_shop.obs_orders WHERE obs_shop.obs_orders.status = 'open' ORDER BY obs_shop.obs_orders.order_id;")).ToString());

    [Theory]
    [InlineData("SELECT obs_shop.obs_orders.order_id FROM obs_shop.obs_orders AS o;", " ! 4104")]
    [InlineData("SELECT obs_orders.order_id FROM obs_shop.obs_orders AS o;", " ! 4104")]
    [InlineData("SELECT obs_shop.obs_orders.* FROM obs_shop.obs_orders AS o;", " ! 107")]
    // A source with an alias is known by its alias alone: neither its full name nor its table's name binds.
    public async Task An_alias_hides_a_tables_full_name(string call, string outcome) =>
        Assert.Equal(outcome, (await Shop(call)).ToString());

    [Fact]
    public async Task A_full_name_reaches_past_an_aliased_source_to_the_enclosing_query() =>
        // The inner obs_shop.obs_orders has an alias, so the name is the outer one's: each outer row gives its own id.
        Assert.Equal(
            "inner_id: 1;2",
            (await Shop("SELECT (SELECT TOP (1) obs_shop.obs_orders.order_id FROM obs_shop.obs_orders AS i WHERE i.order_id = 2) AS inner_id FROM obs_shop.obs_orders ORDER BY obs_shop.obs_orders.order_id;")).ToString());

    [Fact]
    public async Task A_full_name_passes_an_inner_table_of_its_name_in_another_schema() =>
        // The inner dbo.obs_orders, without an alias, is exposed as dbo.obs_orders, so obs_shop.obs_orders.order_id is the
        // outer table's: each outer row gives its own id, where the inner table's would be 7.
        Assert.Equal(
            "inner_id: 1;2",
            (await Shop("SELECT (SELECT TOP (1) obs_shop.obs_orders.order_id FROM dbo.obs_orders) AS inner_id FROM obs_shop.obs_orders ORDER BY obs_shop.obs_orders.order_id;")).ToString());

    [Fact]
    public async Task A_full_name_both_queries_expose_is_the_inner_ones() =>
        // The inner copy, without an alias, is exposed under the same full name, and the innermost scope wins: 1 for both rows.
        Assert.Equal(
            "inner_id: 1;1",
            (await Shop("SELECT (SELECT TOP (1) obs_shop.obs_orders.order_id FROM obs_shop.obs_orders ORDER BY 1) AS inner_id FROM obs_shop.obs_orders ORDER BY obs_shop.obs_orders.order_id;")).ToString());

    [Theory]
    [InlineData("WITH obs_orders AS (SELECT 9 AS order_id) SELECT (SELECT TOP (1) obs_shop.obs_orders.order_id FROM obs_orders) AS inner_id FROM obs_shop.obs_orders ORDER BY obs_shop.obs_orders.order_id;")]
    [InlineData("SELECT (SELECT TOP (1) obs_shop.obs_orders.order_id FROM (SELECT 9 AS order_id) AS obs_orders) AS inner_id FROM obs_shop.obs_orders ORDER BY obs_shop.obs_orders.order_id;")]
    // A CTE or derived table of the table's name has no full name, so it doesn't hide the outer table's: no 9.
    public async Task A_full_name_passes_an_inner_cte_or_derived_table_of_its_name(string call) =>
        Assert.Equal("inner_id: 1;2", (await Shop(call)).ToString());

    [Fact]
    public async Task A_full_name_star_on_an_optional_side_is_null_where_nothing_matches() =>
        Assert.Equal(
            "order_id,order_id,status: 1,null,null;7,1,open",
            (await Shop("SELECT d.order_id, obs_shop.obs_orders.* FROM dbo.obs_orders AS d LEFT JOIN obs_shop.obs_orders ON obs_shop.obs_orders.order_id = d.order_id - 6 ORDER BY d.order_id;", "INSERT INTO dbo.obs_orders VALUES (1, 4);")).ToString());

    [Theory]
    [InlineData("DECLARE @v TABLE (c int); INSERT INTO @v VALUES (1); SELECT dbo.[@v].c FROM @v;", " ! 4104")]
    [InlineData("DECLARE @v TABLE (c int); INSERT INTO @v VALUES (1); SELECT [@v].c FROM @v;", "c: 1")]
    // A table variable is exposed under its own name, bracketed, but under no full name; without brackets, dbo.@v.c doesn't parse
    // (102).
    public async Task A_table_variable_has_no_full_name(string call, string outcome) =>
        Assert.Equal(outcome, (await Run([], call)).ToString());

    [Fact]
    public async Task A_full_name_passes_a_table_of_another_database_to_the_enclosing_query() =>
        // master.sys.objects is exposed under no full name of this database, so sys.objects.name in the subquery is the outer
        // query's, the one row named obs_t, never a name from master.
        Assert.Equal(
            "inner_name: obs_t",
            (await Run([Rows], "SELECT (SELECT TOP (1) sys.objects.name FROM master.sys.objects) AS inner_name FROM sys.objects WHERE sys.objects.name = 'obs_t';")).ToString());

    [Theory]
    [InlineData("SELECT dbo.obs_orders.note FROM obs_orders;", "note: null")]
    [InlineData("SELECT obs_shop.obs_orders.status FROM obs_orders;", " ! 4104")]
    [InlineData("EXECUTE AS USER = 'obs_shopper'; EXEC sp_executesql N'SELECT obs_shop.obs_orders.status FROM obs_orders ORDER BY 1;'; REVERT;", "status: open;shut")]
    [InlineData("EXECUTE AS USER = 'obs_shopper'; EXEC sp_executesql N'SELECT dbo.obs_orders.note FROM obs_orders;'; REVERT;", " ! 4104")]
    [InlineData("EXECUTE AS USER = 'obs_shopper'; EXEC sp_executesql N'SELECT obs_shop.only_here.n FROM only_here;'; REVERT;", "n: 5")]
    // A one-part name in FROM is exposed under the schema it resolves to for the caller: their default schema's object, then
    // dbo's. The test runs as dbo, whose default schema is dbo; obs_shopper's is obs_shop. Dynamic SQL is compiled as the
    // user it runs as, where the batch around it is compiled before EXECUTE AS runs.
    public async Task A_one_part_name_is_exposed_under_the_schema_it_resolves_to(string call, string outcome) =>
        Assert.Equal(outcome, (await Shop(call, "CREATE USER obs_shopper WITHOUT LOGIN WITH DEFAULT_SCHEMA = obs_shop; GRANT SELECT TO obs_shopper;")).ToString());

    [Theory]
    [InlineData("SELECT 1 AS x FROM obs_shop.obs_orders, dbo.obs_orders;", " ! 1013")]
    [InlineData("SELECT 1 AS x FROM obs_shop.obs_orders CROSS JOIN obs_shop.obs_orders;", " ! 1013")]
    [InlineData("SELECT 1 AS x FROM obs_shop.obs_orders, dbo.obs_orders AS obs_orders;", " ! 1012")]
    // Two sources exposed under one table name are refused, even in two schemas, so no full name ever matches two sources of
    // one query. The procedure is refused when it is created (checked by hand), unless a name is deferred.
    public async Task Two_sources_of_one_name_without_aliases_are_refused(string call, string outcome) =>
        Assert.Equal(outcome, (await Shop(call)).ToString());

    [Fact]
    public async Task Bracketed_names_containing_dots_bind_as_three_parts() =>
        Assert.Equal(
            "qty,line_id,qty: 3,1,3",
            (await Run(
                ["CREATE SCHEMA [obs.eu];", "CREATE TABLE [obs.eu].[order.line] (line_id int NOT NULL, qty int NOT NULL); INSERT INTO [obs.eu].[order.line] VALUES (1, 3);"],
                "SELECT [obs.eu].[order.line].qty, [obs.eu].[order.line].* FROM [obs.eu].[order.line];")).ToString());

    [Theory]
    [InlineData("SELECT obs_shop.open_orders.order_id FROM obs_shop.open_orders;", "order_id: 1")]
    [InlineData("SELECT obs_shop.ord.order_id FROM obs_shop.ord WHERE obs_shop.ord.order_id = 1;", "order_id: 1")]
    [InlineData("SELECT obs_shop.obs_orders.order_id FROM obs_shop.ord;", " ! 4104")]
    [InlineData("SELECT obs_shop.ids.order_id FROM obs_shop.ids() ORDER BY 1;", "order_id: 1;2")]
    // A view, a synonym and a table-valued function are each known by their own full name; a synonym not by its target's.
    public async Task A_view_a_synonym_and_a_function_bind_by_their_own_full_names(string call, string outcome) =>
        Assert.Equal(outcome, (await Shop(
            call,
            "CREATE VIEW obs_shop.open_orders AS SELECT order_id FROM obs_shop.obs_orders WHERE status = 'open';",
            "CREATE SYNONYM obs_shop.ord FOR obs_shop.obs_orders;",
            "CREATE FUNCTION obs_shop.ids() RETURNS TABLE AS RETURN SELECT order_id FROM obs_shop.obs_orders;")).ToString());

    [Fact]
    public async Task A_temporary_tables_full_name_takes_any_schema() =>
        Assert.Equal(
            "a,a: 1,1",
            (await Run([], "CREATE TABLE #obs (a int); INSERT INTO #obs VALUES (1); SELECT dbo.#obs.a, nowhere.#obs.a FROM #obs;")).ToString());

    [Fact]
    public async Task A_cte_has_no_full_name() =>
        Assert.Equal(" ! 4104", (await Run([], "WITH c AS (SELECT 1 AS x) SELECT dbo.c.x FROM c;")).ToString());

    [Theory]
    [InlineData("DECLARE @q nvarchar(400) = N'SELECT obs_shop.obs_orders.order_id FROM ' + QUOTENAME(DB_NAME()) + N'.obs_shop.obs_orders ORDER BY 1;'; EXEC sp_executesql @q;", "order_id: 1;2")]
    [InlineData("SELECT TOP (1) sys.objects.name FROM master.sys.objects;", " ! 4104")]
    // A source named with a database is exposed under its full name only when that database is the one the query runs in.
    public async Task A_full_name_binds_a_source_named_with_this_databases_name(string call, string outcome) =>
        Assert.Equal(outcome, (await Shop(call)).ToString());

    [Fact]
    public async Task A_four_part_column_reference_binds_in_this_database() =>
        // So leaving it unknown claims less than SQL Server does, never more.
        Assert.Equal(
            "order_id: 1;2",
            (await Shop("DECLARE @q nvarchar(400) = N'SELECT ' + QUOTENAME(DB_NAME()) + N'.obs_shop.obs_orders.order_id FROM obs_shop.obs_orders ORDER BY 1;'; EXEC sp_executesql @q;")).ToString());

    [Theory]
    [InlineData("UPDATE obs_shop.obs_orders SET status = 'done' OUTPUT obs_shop.obs_orders.status FROM obs_shop.obs_orders WHERE order_id = 1;", " ! 4104")]
    [InlineData("UPDATE obs_shop.obs_orders SET status = 'done' OUTPUT obs_orders.status FROM obs_shop.obs_orders WHERE order_id = 1;", " ! 4104")]
    [InlineData("UPDATE obs_shop.obs_orders SET status = 'done' OUTPUT obs_shop.obs_orders.status WHERE order_id = 1;", " ! 4104")]
    [InlineData("DELETE obs_shop.obs_orders OUTPUT obs_shop.obs_orders.status WHERE order_id = 1;", " ! 4104")]
    // OUTPUT reads the target through inserted and deleted only: never by its table's name, even when FROM lists it without
    // an alias.
    public async Task Output_cannot_name_the_target_by_its_table_name(string call, string outcome) =>
        Assert.Equal(outcome, (await Shop(call)).ToString());

    [Fact]
    public async Task Set_and_where_name_the_target_by_its_full_name() =>
        Assert.Equal(
            "status: x",
            (await Shop("UPDATE obs_shop.obs_orders SET obs_shop.obs_orders.status = 'x' OUTPUT inserted.status WHERE obs_shop.obs_orders.order_id = 1;")).ToString());
}

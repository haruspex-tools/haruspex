using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with <c>SELECT *</c> and <c>alias.*</c>, observed and kept as tests: the rules haruspex's expansion of
/// a star rests on (Haruspex.SqlServer.Inference, README "SELECT *"; decision 0047). Which columns a star gives and in what
/// order, which it leaves out, when each kind of module binds it, and what happens to two columns of one name. Each test
/// creates what it needs in a write session, rolled back. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class SelectStarObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call) => ObservedCall.Run(server, setup, call);

    /// <summary>Two tables that share the column names id and name: one row each, matching.</summary>
    private const string Pair = """
        CREATE TABLE dbo.obs_left (id int NOT NULL, name nvarchar(10) NULL);
        CREATE TABLE dbo.obs_right (id int NOT NULL, name nvarchar(10) NULL);
        INSERT INTO dbo.obs_left VALUES (1, N'x'); INSERT INTO dbo.obs_right VALUES (1, N'y');
        """;

    // ---- which columns, in what order --------------------------------------------------------------------------------

    [Fact]
    public async Task A_star_gives_a_tables_columns_in_column_id_order()
    {
        // A column dropped and added again goes to the end: the order is column_id's, which ALTER TABLE … ADD extends.
        string[] setup =
        [
            "CREATE TABLE dbo.obs_t (a int NOT NULL, b int NULL, c int NULL); INSERT INTO dbo.obs_t VALUES (1, 2, 3);",
            "ALTER TABLE dbo.obs_t DROP COLUMN b; ALTER TABLE dbo.obs_t ADD d int NULL, b int NULL;",
        ];
        Assert.Equal("a,c,d,b: 1,3,null,null", (await Run(setup, "SELECT * FROM dbo.obs_t;")).ToString());
    }

    [Fact]
    public async Task A_star_leaves_out_hidden_period_columns()
    {
        // Hidden from *, from alias.* and from a read FOR SYSTEM_TIME ALL; a period column not declared HIDDEN is kept.
        string[] setup =
        [
            """
            CREATE TABLE dbo.obs_price (id int NOT NULL PRIMARY KEY, amount int NULL,
                valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, valid_to datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
                PERIOD FOR SYSTEM_TIME (valid_from, valid_to)) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.obs_price_history));
            """,
            "INSERT INTO dbo.obs_price (id, amount) VALUES (1, 5);",
        ];
        var outcome = await Run(setup, """
            SELECT * FROM dbo.obs_price;
            SELECT p.* FROM dbo.obs_price AS p;
            SELECT * FROM dbo.obs_price FOR SYSTEM_TIME ALL;
            """);
        Assert.All(outcome.Sets, set => Assert.StartsWith("id,amount,valid_to: 1,5,", set, StringComparison.Ordinal));
        Assert.Equal(3, outcome.Sets.Count);
    }

    [Fact]
    public async Task A_column_set_stands_for_its_sparse_columns_in_star()
    {
        // The sparse columns are left out, and the column set, xml, gives their values in their place, where it is declared.
        string[] setup =
        [
            "CREATE TABLE dbo.obs_parcel (id int NOT NULL, weight int SPARSE NULL, label nvarchar(5) NULL, extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS);",
            "INSERT INTO dbo.obs_parcel (id, weight, label) VALUES (1, 5, N'a');",
        ];
        Assert.Equal("id,label,extras: 1,a,<weight>5</weight>", (await Run(setup, "SELECT * FROM dbo.obs_parcel;")).ToString());
    }

    [Fact]
    public async Task A_graph_tables_star_adds_pseudo_columns_named_by_the_server()
    {
        // $node_id_ and 32 hex digits the server chooses when it creates the table: a name the project doesn't hold.
        string[] setup = ["CREATE TABLE dbo.obs_depot (id int NOT NULL) AS NODE; INSERT INTO dbo.obs_depot VALUES (1);"];
        var set = Assert.Single((await Run(setup, "SELECT * FROM dbo.obs_depot;")).Sets);
        Assert.Matches("^\\$node_id_[0-9A-F]{32},id: ", set);
    }

    [Fact]
    public async Task Output_stars_leave_out_what_a_star_over_the_target_leaves_out()
    {
        // inserted.* and deleted.*: no HIDDEN period column, and a column set in place of its sparse columns. A hidden column
        // named outright is still given.
        string[] setup =
        [
            """
            CREATE TABLE dbo.obs_price (id int NOT NULL PRIMARY KEY, amount int NULL,
                valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, valid_to datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
                PERIOD FOR SYSTEM_TIME (valid_from, valid_to)) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.obs_price_history));
            CREATE TABLE dbo.obs_parcel (id int NOT NULL, weight int SPARSE NULL, extras xml COLUMN_SET FOR ALL_SPARSE_COLUMNS);
            INSERT INTO dbo.obs_parcel (id, weight) VALUES (1, 5);
            """,
        ];
        var outcome = await Run(setup, """
            INSERT INTO dbo.obs_price (id, amount) OUTPUT inserted.* VALUES (1, 5);
            UPDATE dbo.obs_price SET amount = 6 OUTPUT deleted.*, inserted.amount AS now WHERE id = 1;
            DELETE FROM dbo.obs_price OUTPUT deleted.*, CASE WHEN deleted.valid_from IS NULL THEN 0 ELSE 1 END AS named WHERE id = 1;
            UPDATE dbo.obs_parcel SET weight = 7 OUTPUT inserted.*, deleted.* WHERE id = 1;
            """);
        Assert.Equal("id,amount: 1,5 | id,amount,now: 1,5,6 | id,amount,named: 1,6,1 | id,extras,id,extras: 1,<weight>7</weight>,1,<weight>5</weight>", outcome.ToString());
    }

    [Fact]
    public async Task A_temporary_tables_star_is_its_columns_when_the_statement_runs()
    {
        // In one procedure: an ALTER TABLE between two reads adds its column to the second only.
        string[] setup =
        [
            "CREATE TABLE dbo.obs_t (a int NOT NULL, b int NULL); INSERT INTO dbo.obs_t VALUES (1, 2);",
            """
            CREATE PROCEDURE dbo.obs_p AS
            BEGIN
                SELECT * INTO #x FROM dbo.obs_t;
                SELECT * FROM #x;
                ALTER TABLE #x ADD z int NULL;
                SELECT * FROM #x;
                DECLARE @v TABLE (p int NOT NULL, q int NULL);
                INSERT INTO @v VALUES (7, NULL);
                SELECT * FROM @v;
            END;
            """,
        ];
        Assert.Equal("a,b: 1,2 | a,b,z: 1,2,null | p,q: 7,null", (await Run(setup, "EXEC dbo.obs_p;")).ToString());
    }

    [Fact]
    public async Task Select_star_into_copies_an_identity_from_one_table()
    {
        // The copy's id is an identity, so an insert without a column list skips it and the copy numbers the row.
        string[] setup = ["CREATE TABLE dbo.obs_t (id int IDENTITY(5, 1) NOT NULL, v int NULL); INSERT INTO dbo.obs_t (v) VALUES (1);"];
        Assert.Equal(
            "id,v: 5,1;6,2",
            (await Run(setup, "SELECT * INTO #c FROM dbo.obs_t; INSERT INTO #c VALUES (2); SELECT * FROM #c ORDER BY id;")).ToString());
    }

    // ---- when a star is bound ----------------------------------------------------------------------------------------

    /// <summary>A table, then a module over it created, then a column added: what the module returns now says when it bound its star.</summary>
    private static string[] Changed(string module) =>
    [
        "CREATE TABLE dbo.obs_t (a int NOT NULL, b int NULL); INSERT INTO dbo.obs_t VALUES (1, 2);",
        module,
        "ALTER TABLE dbo.obs_t ADD c int NULL;",
    ];

    [Fact]
    public async Task A_procedures_star_is_bound_when_it_runs()
    {
        // Run before the change too, so a plan was cached: the change recompiles it.
        string[] setup = ["CREATE TABLE dbo.obs_t (a int NOT NULL, b int NULL); INSERT INTO dbo.obs_t VALUES (1, 2);", "CREATE PROCEDURE dbo.obs_p AS SELECT * FROM dbo.obs_t;", "EXEC dbo.obs_p;", "ALTER TABLE dbo.obs_t ADD c int NULL;"];
        Assert.Equal("a,b,c: 1,2,null", (await Run(setup, "EXEC dbo.obs_p;")).ToString());
    }

    [Fact]
    public async Task A_scalar_functions_star_is_bound_when_it_runs()
    {
        var setup = Changed("CREATE FUNCTION dbo.obs_f() RETURNS nvarchar(max) AS BEGIN RETURN (SELECT * FROM dbo.obs_t FOR JSON PATH, INCLUDE_NULL_VALUES); END;");
        Assert.Equal("""j: [{"a":1,"b":2,"c":null}]""", (await Run(setup, "SELECT dbo.obs_f() AS j;")).ToString());
    }

    [Fact]
    public async Task A_multi_statement_functions_star_is_bound_when_it_runs()
    {
        // Its INSERT … SELECT * now gives three columns for a table of two: 213, as a procedure's would.
        var setup = Changed("CREATE FUNCTION dbo.obs_f() RETURNS @r TABLE (a int, b int) AS BEGIN INSERT INTO @r SELECT * FROM dbo.obs_t; RETURN; END;");
        Assert.Equal(213, (await Run(setup, "SELECT * FROM dbo.obs_f();")).Error);
    }

    [Fact]
    public async Task An_inline_functions_star_is_bound_when_it_is_created()
    {
        // As a view's (A_view_binds_select_star_when_it_is_created): the new column appears only once the module is refreshed.
        var setup = Changed("CREATE FUNCTION dbo.obs_f() RETURNS TABLE AS RETURN SELECT * FROM dbo.obs_t;");
        Assert.Equal("a,b: 1,2", (await Run(setup, "SELECT * FROM dbo.obs_f();")).ToString());
        Assert.Equal("a,b,c: 1,2,null", (await Run(setup, "EXEC sp_refreshsqlmodule 'dbo.obs_f'; SELECT * FROM dbo.obs_f();")).ToString());
    }

    [Fact]
    public async Task A_star_in_a_subquery_of_a_view_or_an_inline_function_is_bound_when_it_runs()
    {
        // Only the module's own select list is fixed when it is created (An_inline_functions_star_is_bound_when_it_is_created):
        // a star in a subquery of it reads the table as it is when the module runs, whether or not it ran before the change.
        const string Nested = "SELECT (SELECT * FROM dbo.obs_t FOR JSON PATH) AS j";
        var function = Changed($"CREATE FUNCTION dbo.obs_f() RETURNS TABLE AS RETURN {Nested};");
        var view = Changed($"CREATE VIEW dbo.obs_v AS {Nested};");
        Assert.Equal("""j: [{"a":1,"b":2,"c":3}]""", (await Run([.. function, "UPDATE dbo.obs_t SET c = 3;"], "SELECT j FROM dbo.obs_f();")).ToString());
        Assert.Equal("""j: [{"a":1,"b":2,"c":3}]""", (await Run([.. view, "UPDATE dbo.obs_t SET c = 3;"], "SELECT j FROM dbo.obs_v;")).ToString());
    }

    [Fact]
    public async Task A_schema_bound_module_refuses_a_star()
    {
        // 1054, for a view or a function, even inside EXISTS: so no module whose star could not go stale has one. COUNT(*) is
        // no star and is allowed. A natively compiled procedure, which must be schema-bound, refuses one the same way
        // (observed by hand: it needs a memory-optimised filegroup, which a test's transaction can't add).
        string[] table = ["CREATE TABLE dbo.obs_t (a int NOT NULL);"];
        Assert.Equal(1054, (await Run(table, "EXEC (N'CREATE VIEW dbo.obs_v WITH SCHEMABINDING AS SELECT * FROM dbo.obs_t;');")).Error);
        Assert.Equal(1054, (await Run(table, "EXEC (N'CREATE FUNCTION dbo.obs_f() RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT * FROM dbo.obs_t;');")).Error);
        Assert.Equal(1054, (await Run(table, "EXEC (N'CREATE VIEW dbo.obs_v WITH SCHEMABINDING AS SELECT t.a FROM dbo.obs_t AS t WHERE EXISTS (SELECT * FROM dbo.obs_t AS u WHERE u.a = t.a);');")).Error);
        Assert.Equal("n: 0", (await Run([.. table, "CREATE VIEW dbo.obs_v WITH SCHEMABINDING AS SELECT COUNT_BIG(*) AS n FROM dbo.obs_t;"], "SELECT n FROM dbo.obs_v;")).ToString());
    }

    [Fact]
    public async Task Only_a_natively_compiled_procedure_can_be_schema_bound() =>
        Assert.Equal(10796, (await Run(["CREATE TABLE dbo.obs_t (a int NOT NULL);"], "EXEC (N'CREATE PROCEDURE dbo.obs_p WITH SCHEMABINDING AS SELECT a FROM dbo.obs_t;');")).Error);

    // ---- two columns of one name -------------------------------------------------------------------------------------

    // What a driver makes of them is its own (README "SELECT *").
    [Fact]
    public async Task A_rowset_returns_both_columns_of_one_name() =>
        Assert.Equal("id,name,id,name: 1,x,1,y", (await Run([Pair], "SELECT * FROM dbo.obs_left AS l JOIN dbo.obs_right AS r ON r.id = l.id;")).ToString());

    [Fact]
    public async Task For_json_refuses_two_columns_of_one_name()
    {
        // 13601 when the procedure runs, not when it is created: it is created, and never returns its JSON.
        string[] setup = [Pair, "CREATE PROCEDURE dbo.obs_p AS SELECT * FROM dbo.obs_left AS l JOIN dbo.obs_right AS r ON r.id = l.id FOR JSON PATH;"];
        Assert.Equal(13601, (await Run(setup, "EXEC dbo.obs_p;")).Error);
    }

    [Fact]
    public async Task A_derived_table_or_cte_refuses_two_columns_of_one_name()
    {
        Assert.Equal(8156, (await Run([Pair], "SELECT d.name FROM (SELECT * FROM dbo.obs_left AS l JOIN dbo.obs_right AS r ON r.id = l.id) AS d;")).Error);
        Assert.Equal(8156, (await Run([Pair], "WITH c AS (SELECT * FROM dbo.obs_left AS l JOIN dbo.obs_right AS r ON r.id = l.id) SELECT c.name FROM c;")).Error);
    }

    [Fact]
    public async Task A_star_without_from_is_refused() =>
        Assert.Equal(263, (await Run([], "EXEC (N'CREATE PROCEDURE dbo.obs_p AS SELECT *;');")).Error);

    [Fact]
    public async Task Select_into_refuses_two_columns_of_one_name() =>
        Assert.Equal(2705, (await Run([Pair], "SELECT * INTO #both FROM dbo.obs_left AS l JOIN dbo.obs_right AS r ON r.id = l.id;")).Error);
}

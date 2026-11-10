using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with a procedure's statements in order, observed and kept as tests: the rules the statement walk
/// in Haruspex.SqlServer.Inference (<c>StatementFlow</c>, <c>FlowState</c>) rests on. Which result sets a client receives
/// when a branch is not taken, a path returns early or an error is raised; what temporary tables, table variables and
/// SELECT INTO give; and how a recursive CTE's columns are typed. Each test creates what it needs in a write session,
/// rolled back, alone in the test database while it lasts. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class StatementFlowObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, setup, call, errorsAsMessages);

    /// <summary>The error number <paramref name="batch"/> fails with after <paramref name="setup"/>, or null when it succeeds; rolled back.</summary>
    private async Task<int?> Fails(string[] setup, string batch) => (await Run(setup, batch)).Error;

    private const string Rows = """
        CREATE TABLE dbo.obs_row (id int NOT NULL PRIMARY KEY, n int NOT NULL, label nvarchar(10) NULL);
        INSERT INTO dbo.obs_row VALUES (1, 1, N'one'), (2, 0, NULL);
        """;

    // ---- which result sets a client receives ----------------------------------------------------------

    [Fact]
    public async Task A_branch_not_taken_returns_no_result_set_at_all_so_later_ones_move_up()
    {
        // The IR's results are positional, so a result inside a branch may be missing altogether, not just empty, and a
        // result after it can arrive at its position: results[i] is the union of what each path returns i-th.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_branch @a int AS
            IF @a = 1 SELECT 1 AS x;
            ELSE IF @a = 2 SELECT 1 AS j WHERE 1 = 0 FOR JSON PATH;
            SELECT 2 AS y;
            """];

        Assert.Equal("x: 1 | y: 2", (await Run(setup, "EXEC dbo.obs_branch 1")).ToString());
        Assert.Equal("y: 2", (await Run(setup, "EXEC dbo.obs_branch 0")).ToString());

        // A FOR JSON query that runs and matches no rows is a result set with no row; one that does not run is no set.
        Assert.Equal("JSON_F52E2B61-18A1-11d1-B105-00805F49916B:  | y: 2", (await Run(setup, "EXEC dbo.obs_branch 2")).ToString());
    }

    [Fact]
    public async Task An_early_return_leaves_out_every_result_after_it()
    {
        string[] setup = ["CREATE PROCEDURE dbo.obs_return @a int AS SELECT 1 AS x; IF @a = 1 RETURN; SELECT 2 AS y;"];

        Assert.Equal("x: 1", (await Run(setup, "EXEC dbo.obs_return 1")).ToString());
        Assert.Equal("x: 1 | y: 2", (await Run(setup, "EXEC dbo.obs_return 0")).ToString());
    }

    [Fact]
    public async Task Throw_ends_the_procedure_and_the_call_fails()
    {
        // So a path that throws describes no call that succeeds, and the IR, which describes calls that succeed, drops it.
        string[] setup = ["CREATE PROCEDURE dbo.obs_throw AS SELECT 1 AS x; THROW 50000, N'stop', 1; SELECT 2 AS y;"];

        Assert.Equal("x: 1 ! 50000", (await Run(setup, "EXEC dbo.obs_throw")).ToString());
        Assert.Equal("x: 1 ~ 50000", (await Run(setup, "EXEC dbo.obs_throw", errorsAsMessages: true)).ToString());
    }

    [Fact]
    public async Task Raiserror_of_severity_11_or_more_carries_on_but_the_call_fails()
    {
        // Outside TRY the procedure goes on and returns y, but the client is handed the error: the call does not succeed,
        // so for the IR the path ends there all the same.
        string[] setup = ["CREATE PROCEDURE dbo.obs_raise @s int AS SELECT 1 AS x; RAISERROR(N'stop', @s, 1); SELECT 2 AS y;"];

        Assert.Equal("x: 1 ! 50000", (await Run(setup, "EXEC dbo.obs_raise 16")).ToString());
        Assert.Equal("x: 1 | y: 2 ~ 50000", (await Run(setup, "EXEC dbo.obs_raise 16", errorsAsMessages: true)).ToString());
        Assert.Equal("x: 1 ! 50000", (await Run(setup, "EXEC dbo.obs_raise 11")).ToString());

        // Severity 10 and below is a message, not an error.
        Assert.Equal("x: 1 | y: 2", (await Run(setup, "EXEC dbo.obs_raise 10")).ToString());
    }

    [Fact]
    public async Task An_error_in_try_goes_to_catch_and_the_results_before_it_still_arrive()
    {
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_try @how int AS
            BEGIN TRY
                SELECT 1 AS x;
                IF @how = 1 THROW 50000, N'stop', 1;
                IF @how = 2 RAISERROR(N'stop', 16, 1);
                SELECT 2 AS y;
            END TRY
            BEGIN CATCH
                SELECT ERROR_NUMBER() AS caught;
            END CATCH
            """];

        Assert.Equal("x: 1 | y: 2", (await Run(setup, "EXEC dbo.obs_try 0")).ToString());
        Assert.Equal("x: 1 | caught: 50000", (await Run(setup, "EXEC dbo.obs_try 1")).ToString());
        Assert.Equal("x: 1 | caught: 50000", (await Run(setup, "EXEC dbo.obs_try 2")).ToString());
    }

    [Fact]
    public async Task A_result_cut_short_by_an_error_in_try_still_arrives_with_the_rows_sent_before_it()
    {
        // So a result whose statement may fail part-way still takes its position before CATCH's results.
        string[] setup = [Rows, """
            CREATE PROCEDURE dbo.obs_partial AS
            BEGIN TRY
                SELECT 10 / r.n AS q FROM dbo.obs_row AS r ORDER BY r.id;
            END TRY
            BEGIN CATCH
                SELECT ERROR_NUMBER() AS caught;
            END CATCH
            """];

        Assert.Equal("q: 10 | caught: 8134", (await Run(setup, "EXEC dbo.obs_partial")).ToString());
    }

    [Fact]
    public async Task A_missing_table_is_not_caught_by_catch_in_the_same_procedure()
    {
        // Deferred name resolution fails when the statement is compiled, which a CATCH at the same level does not see.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_missing AS
            BEGIN TRY
                SELECT m.id FROM #obs_missing AS m;
            END TRY
            BEGIN CATCH
                SELECT 1 AS caught;
            END CATCH
            """];

        Assert.Equal(" ! 208", (await Run(setup, "EXEC dbo.obs_missing")).ToString());
    }

    // ---- temporary tables -----------------------------------------------------------------------------

    [Fact]
    public async Task A_procedure_reads_a_temporary_table_its_caller_created()
    {
        // So a #table the procedure does not create has whatever shape the caller gave it.
        string[] setup = ["CREATE PROCEDURE dbo.obs_reader AS SELECT t.v FROM #obs_t AS t;"];

        Assert.Equal("v: a", (await Run(setup, "CREATE TABLE #obs_t (v nvarchar(5)); INSERT INTO #obs_t VALUES (N'a'); EXEC dbo.obs_reader;")).ToString());
        Assert.Equal("v: 7", (await Run(setup, "CREATE TABLE #obs_t (v int); INSERT INTO #obs_t VALUES (7); EXEC dbo.obs_reader;")).ToString());
    }

    [Fact]
    public async Task A_temporary_table_a_procedure_creates_hides_the_callers_of_the_same_name()
    {
        string[] setup = ["CREATE PROCEDURE dbo.obs_own AS CREATE TABLE #obs_t (w int NOT NULL); INSERT INTO #obs_t VALUES (5); SELECT t.w FROM #obs_t AS t;"];

        // The procedure's own table is the one read, though the caller's has a column of the same name and another type.
        Assert.Equal("w: 5", (await Run(setup, "CREATE TABLE #obs_t (w nvarchar(5)); INSERT INTO #obs_t VALUES (N'x'); EXEC dbo.obs_own;")).ToString());

        // But the procedure is compiled while the caller's table exists, so a column only its own table has fails the
        // call (207): a failed call, which the IR does not describe.
        Assert.Equal(207, (await Run(setup, "CREATE TABLE #obs_t (v nvarchar(5)); EXEC dbo.obs_own;")).Error);
    }

    [Fact]
    public async Task A_procedure_cannot_create_a_temporary_table_twice_even_after_dropping_it()
    {
        // Error 2714 when the procedure is created: re-creation after a drop never reaches the server as a procedure.
        // Two SELECT … INTO the same name are refused the same way, and so is a CREATE in each branch of an IF.
        Assert.Equal(2714, await Fails([], "CREATE PROCEDURE dbo.obs_twice AS CREATE TABLE #t (a int); DROP TABLE #t; CREATE TABLE #t (b int); SELECT b FROM #t;"));
        Assert.Equal(2714, await Fails([Rows], "CREATE PROCEDURE dbo.obs_twice AS SELECT r.id INTO #t FROM dbo.obs_row AS r; DROP TABLE #t; SELECT r.n INTO #t FROM dbo.obs_row AS r;"));
        Assert.Equal(2714, await Fails([], "CREATE PROCEDURE dbo.obs_twice @a int AS IF @a = 1 CREATE TABLE #t (a int); ELSE CREATE TABLE #t (b int);"));
    }

    [Fact]
    public async Task Select_into_a_column_from_a_not_null_column_refuses_null_but_others_take_it()
    {
        // SELECT … INTO decides each column's nullability by SQL Server's own rules, which haruspex does not reproduce, so
        // a later write joins with what the column held rather than relying on either.
        string[] setup = [Rows, """
            CREATE PROCEDURE dbo.obs_into @which int AS
            SELECT r.n, r.label, json_object('a': r.n) AS j INTO #t FROM dbo.obs_row AS r;
            IF @which = 1 INSERT INTO #t (n) VALUES (NULL);
            IF @which = 2 INSERT INTO #t (n, j) VALUES (3, NULL);
            SELECT COUNT(*) AS c FROM #t;
            """];

        Assert.Equal(515, (await Run(setup, "EXEC dbo.obs_into 1")).Error);
        Assert.Equal("c: 3", (await Run(setup, "EXEC dbo.obs_into 2")).ToString());
    }

    [Fact]
    public async Task Select_into_a_column_from_a_variable_takes_null()
    {
        // Whatever the variable holds, the column SQL Server makes from it is nullable.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_from_variable AS
            DECLARE @v int = 5;
            SELECT @v AS v INTO #t;
            INSERT INTO #t (v) VALUES (NULL);
            SELECT ISNULL(CONVERT(nvarchar(10), t.v), N'null') AS v FROM #t AS t ORDER BY t.v;
            """];

        Assert.Equal("v: null;5", (await Run(setup, "EXEC dbo.obs_from_variable")).ToString());
    }

    [Fact]
    public async Task A_string_type_without_a_length_is_30_long_in_a_cast_and_1_in_a_declaration()
    {
        var outcome = await Run([], """
            DECLARE @s nvarchar(40) = N'abcdefghijklmnopqrstuvwxyz0123456789';
            DECLARE @d varchar = 'abc';
            SELECT CAST(@s AS varchar) AS a, CONVERT(nvarchar, @s) AS b, LEN(CAST(@s AS char)) AS c, DATALENGTH(CAST(N'x' AS char)) AS cpad, @d AS d;
            """);

        Assert.Equal("a,b,c,cpad,d: abcdefghijklmnopqrstuvwxyz0123,abcdefghijklmnopqrstuvwxyz0123,30,30,a", outcome.ToString());
    }

    [Fact]
    public async Task Json_stored_in_a_temporary_table_is_text_but_json_through_a_cte_stays_json()
    {
        // A value of nvarchar(max) holding JSON escapes once it is stored, as through a variable; a CTE is part of its
        // query, so a constructor in it nests as JSON.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_json @how int AS
            IF @how = 1
            BEGIN
                SELECT json_object('a': 1) AS j INTO #t;
                SELECT t.j FROM #t AS t FOR JSON PATH;
            END
            ELSE
            BEGIN
                WITH c AS (SELECT json_object('a': 1) AS j) SELECT c.j FROM c FOR JSON PATH;
            END
            """];

        Assert.Equal("""JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"j":"{\"a\":1}"}]""", (await Run(setup, "EXEC dbo.obs_json 1")).ToString());
        Assert.Equal("""JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"j":{"a":1}}]""", (await Run(setup, "EXEC dbo.obs_json 2")).ToString());
    }

    [Fact]
    public async Task A_rollback_undoes_a_temporary_tables_creation()
    {
        // Temporary tables take part in transactions: rolled back, a table created inside is gone (and one dropped
        // inside is back), so after a ROLLBACK what exists is not known from the statements alone.
        var outcome = await Run([], """
            SAVE TRANSACTION obs;
            CREATE TABLE #obs_rb (a int);
            ROLLBACK TRANSACTION obs;
            SELECT CASE WHEN OBJECT_ID('tempdb..#obs_rb') IS NULL THEN 'gone' ELSE 'there' END AS t;
            """);

        Assert.Equal("t: gone", outcome.ToString());
    }

    [Fact]
    public async Task A_temporary_table_cannot_use_the_databases_alias_types_but_a_table_variable_can()
    {
        // A #table is created in tempdb, where the database's alias types are not, so a column of one is refused (2715).
        // A table variable takes them from the database. chk.short_code is the checks fixture's, deployed with the seed:
        // an alias type created in the test's own transaction would leave the table variable waiting on its lock.
        Assert.Equal(2715, await Fails([], "CREATE TABLE #obs_alias (c chk.short_code);"));
        Assert.Equal("c: ab", (await Run([], "DECLARE @v TABLE (c chk.short_code); INSERT INTO @v VALUES (N'ab'); SELECT v.c FROM @v AS v;")).ToString());

        // The alias type's NOT NULL holds in the table variable, as in a table.
        Assert.Equal(515, await Fails([], "DECLARE @v TABLE (c chk.short_code); INSERT INTO @v VALUES (NULL);"));
    }

    [Fact]
    public async Task A_variable_or_table_variable_declared_in_a_branch_not_taken_still_exists()
    {
        // Declarations are compiled, not run: the scope is the whole batch from the DECLARE on, and the value is NULL.
        var outcome = await Run([], """
            IF 1 = 0
            BEGIN
                DECLARE @v int = 5;
                DECLARE @t TABLE (a int NOT NULL);
            END
            INSERT INTO @t VALUES (1);
            SELECT ISNULL(CONVERT(nvarchar(10), @v), N'null') AS v, (SELECT COUNT(*) FROM @t) AS n;
            """);

        Assert.Equal("v,n: null,1", outcome.ToString());
    }

    // ---- recursive CTEs -------------------------------------------------------------------------------

    private const string Tree = """
        CREATE TABLE dbo.obs_node (id int NOT NULL PRIMARY KEY, parent_id int NULL, name nvarchar(10) NOT NULL, big bigint NOT NULL, code varchar(20) NOT NULL);
        INSERT INTO dbo.obs_node VALUES (1, NULL, N'root', 1, 'r'), (2, 1, N'a', 2, 'a'), (3, 2, N'b', 3, 'b');
        """;

    [Theory]
    // int anchor, bigint recursive member.
    [InlineData("SELECT n.id AS v FROM dbo.obs_node AS n WHERE n.parent_id IS NULL", "SELECT c.big FROM dbo.obs_node AS c JOIN w ON c.parent_id = w.v")]
    // The same base type with another length.
    [InlineData("SELECT n.code AS v FROM dbo.obs_node AS n WHERE n.parent_id IS NULL", "SELECT CAST(c.code AS varchar(10)) FROM dbo.obs_node AS c JOIN w ON 1 = 0")]
    // nvarchar(10) against the nvarchar(4000) a concatenation gives.
    [InlineData("SELECT n.name AS v FROM dbo.obs_node AS n WHERE n.parent_id IS NULL", "SELECT w.v + N'/' + c.name FROM dbo.obs_node AS c JOIN w ON 1 = 0")]
    public async Task A_recursive_member_of_another_type_than_the_anchor_is_an_error(string anchor, string recursive) =>
        // Error 240: "Types don't match between the anchor and the recursive part". So a recursive CTE's column has
        // exactly its anchor's type, whatever the recursive member computes, and only its nullability needs a fixed point.
        Assert.Equal(240, await Fails([Tree], $"WITH w AS ({anchor} UNION ALL {recursive}) SELECT w.v FROM w;"));

    [Fact]
    public async Task A_recursive_member_may_differ_from_the_anchor_in_nullability()
    {
        // The anchor's names are never null; the recursive member's parent_id-derived value is. The result has null.
        var outcome = await Run([Tree], """
            WITH w AS (
                SELECT n.id, CAST(N'root' AS nvarchar(10)) AS up, 0 AS depth FROM dbo.obs_node AS n WHERE n.parent_id IS NULL
                UNION ALL
                SELECT c.id, CAST(NULL AS nvarchar(10)), w.depth + 1 FROM dbo.obs_node AS c JOIN w ON c.parent_id = w.id)
            SELECT w.id, w.up, w.depth FROM w ORDER BY w.id;
            """);

        Assert.Equal("id,up,depth: 1,root,0;2,null,1;3,null,2", outcome.ToString());
    }
}

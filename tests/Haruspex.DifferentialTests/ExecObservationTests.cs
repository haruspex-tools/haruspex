using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does when a procedure calls another, observed and kept as tests: the rules that following
/// <c>EXEC</c> in Haruspex.SqlServer.Inference rests on (its README.md, "Calls"). Where a callee's result sets arrive,
/// what a callee can do to its caller's temporary tables, what <c>INSERT … EXEC</c> accepts, how deep calls nest, and
/// which procedure a name runs. Each test creates what it needs in a write session, rolled back, alone in the test
/// database while it lasts. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class ExecObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, setup, call, errorsAsMessages);

    // ---- result sets through a call ---------------------------------------------------------------------

    [Fact]
    public async Task A_callees_result_sets_arrive_in_the_callers_stream_where_it_is_called()
    {
        // So the callee's results are spliced into the caller's at the EXEC, in order, and a callee that returns none adds
        // none.
        string[] setup = [
            "CREATE PROCEDURE dbo.obs_inner @a int AS SELECT 2 AS b; IF @a = 1 SELECT 3 AS c;",
            "CREATE PROCEDURE dbo.obs_quiet AS DECLARE @x int = 1;",
            "CREATE PROCEDURE dbo.obs_outer @a int AS SELECT 1 AS a; EXEC dbo.obs_inner @a; EXECUTE dbo.obs_quiet; SELECT 4 AS d;",
        ];

        Assert.Equal("a: 1 | b: 2 | c: 3 | d: 4", (await Run(setup, "EXEC dbo.obs_outer 1")).ToString());
        Assert.Equal("a: 1 | b: 2 | d: 4", (await Run(setup, "EXEC dbo.obs_outer 0")).ToString());
    }

    [Fact]
    public async Task The_return_code_is_never_null()
    {
        // EXEC @rc = p: a RETURN without a value, no RETURN at all, and RETURN NULL (a warning) all give 0. So the variable is
        // a non-null int after a call that succeeds.
        string[] setup = [
            "CREATE PROCEDURE dbo.obs_rc @a int AS IF @a = 1 RETURN; IF @a = 2 RETURN NULL; IF @a = 3 RETURN 7;",
            """
            CREATE PROCEDURE dbo.obs_rc_all AS
            DECLARE @r1 int = -1, @r2 int = -1, @r3 int = -1, @r4 int = -1;
            EXEC @r1 = dbo.obs_rc 1; EXEC @r2 = dbo.obs_rc 2; EXEC @r3 = dbo.obs_rc 3; EXEC @r4 = dbo.obs_rc 4;
            SELECT @r1 AS r1, @r2 AS r2, @r3 AS r3, @r4 AS r4;
            """,
        ];

        Assert.Equal("r1,r2,r3,r4: 0,0,7,0", (await Run(setup, "EXEC dbo.obs_rc_all")).ToString());
    }

    [Fact]
    public async Task An_output_parameter_the_callee_does_not_set_keeps_the_value_passed_in()
    {
        // So after a call a variable passed as OUTPUT holds whatever it held or whatever the callee set: its declared type,
        // and null unless both are proved not to be.
        string[] setup = [
            "CREATE PROCEDURE dbo.obs_out @a int, @v int OUTPUT AS IF @a = 1 SET @v = 5;",
            "CREATE PROCEDURE dbo.obs_out_all AS DECLARE @x int = 3, @y int = 3; EXEC dbo.obs_out 0, @x OUTPUT; EXEC dbo.obs_out 1, @y OUTPUT; SELECT @x AS x, @y AS y;",
        ];

        Assert.Equal("x,y: 3,5", (await Run(setup, "EXEC dbo.obs_out_all")).ToString());
    }

    // ---- temporary tables across a call ------------------------------------------------------------------

    [Fact]
    public async Task A_callee_writes_its_callers_temporary_table_and_the_rows_stay()
    {
        // INSERT and UPDATE in the callee change the caller's table, and what they wrote is there after the call.
        string[] setup = [
            "CREATE PROCEDURE dbo.obs_fill AS INSERT INTO #obs_t (v, w) VALUES (NULL, N'b'); UPDATE #obs_t SET w = N'x' WHERE v IS NULL;",
            "CREATE PROCEDURE dbo.obs_fill_caller AS DECLARE @one int = 1; SELECT @one AS v, N'a' AS w INTO #obs_t; EXEC dbo.obs_fill; SELECT ISNULL(CONVERT(nvarchar(5), t.v), N'null') AS v, t.w FROM #obs_t AS t ORDER BY t.v;",
        ];

        Assert.Equal("v,w: null,x;1,a", (await Run(setup, "EXEC dbo.obs_fill_caller")).ToString());
    }

    [Fact]
    public async Task A_callee_can_alter_and_drop_its_callers_temporary_table()
    {
        // ALTER TABLE in the callee adds a column the caller then reads; DROP TABLE removes the caller's table, so the caller
        // reading it afterwards fails (208). So a callee's ALTER and DROP apply to the caller's state after the call.
        string[] setup = [
            "CREATE PROCEDURE dbo.obs_widen AS ALTER TABLE #obs_t ADD extra nvarchar(3) NULL;",
            "CREATE PROCEDURE dbo.obs_drop AS DROP TABLE #obs_t;",
        ];

        Assert.Equal("v,extra: 1,null", (await Run(setup, "CREATE TABLE #obs_t (v int); INSERT INTO #obs_t (v) VALUES (1); EXEC dbo.obs_widen; EXEC (N'SELECT * FROM #obs_t');")).ToString());
        Assert.Equal(208, (await Run(setup, "CREATE TABLE #obs_t (v int); EXEC dbo.obs_drop; SELECT v FROM #obs_t;")).Error);
    }

    [Fact]
    public async Task A_temporary_table_a_callee_creates_is_gone_when_it_returns()
    {
        // So what a callee creates never reaches its caller: a read of it afterwards fails (208).
        string[] setup = ["CREATE PROCEDURE dbo.obs_make AS CREATE TABLE #obs_made (v int); INSERT INTO #obs_made VALUES (1);"];

        Assert.Equal(208, (await Run(setup, "EXEC dbo.obs_make; SELECT v FROM #obs_made;")).Error);
    }

    [Fact]
    public async Task Dynamic_sql_can_change_its_callers_temporary_tables()
    {
        // Dynamic SQL runs as a nested scope with the caller's temporary tables in reach, so after it nothing is known of them.
        Assert.Equal("v,extra: 1,null", (await Run([], "CREATE TABLE #obs_t (v int); INSERT INTO #obs_t (v) VALUES (1); EXEC (N'ALTER TABLE #obs_t ADD extra int NULL'); EXEC sp_executesql N'SELECT * FROM #obs_t';")).ToString());
    }

    // ---- INSERT … EXEC ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Insert_exec_inserts_the_rows_of_every_result_set_and_returns_none_to_the_client()
    {
        string[] setup = ["CREATE PROCEDURE dbo.obs_two @a int AS SELECT 1 AS v, N'one' AS w; IF @a = 1 SELECT 2, N'two';"];

        Assert.Equal("n: 2", (await Run(setup, "CREATE TABLE #obs_t (v int, w nvarchar(5)); INSERT INTO #obs_t EXEC dbo.obs_two 1; SELECT COUNT(*) AS n FROM #obs_t;")).ToString());
        Assert.Equal("n: 1", (await Run(setup, "CREATE TABLE #obs_t (v int, w nvarchar(5)); INSERT INTO #obs_t EXEC dbo.obs_two 0; SELECT COUNT(*) AS n FROM #obs_t;")).ToString());
    }

    [Fact]
    public async Task Insert_exec_fails_on_a_result_set_with_the_wrong_number_of_columns_even_with_no_rows()
    {
        // Error 213. So a successful INSERT … EXEC never received a result set of another width, and nothing of it is kept.
        string[] setup = ["CREATE PROCEDURE dbo.obs_wide @rows int AS SELECT 1 AS v; SELECT 1 AS v, 2 AS w WHERE @rows = 1;"];

        Assert.Equal(213, (await Run(setup, "CREATE TABLE #obs_t (v int); INSERT INTO #obs_t EXEC dbo.obs_wide 1;")).Error);
        Assert.Equal(213, (await Run(setup, "CREATE TABLE #obs_t (v int); INSERT INTO #obs_t EXEC dbo.obs_wide 0;")).Error);
    }

    [Fact]
    public async Task Insert_exec_converts_each_value_to_the_columns_type_or_fails()
    {
        // As INSERT does: a string that reads as a number goes into int, one that does not fails (8114), a null fails a NOT
        // NULL column (515), and a longer string fails (8152, or 2628 with verbose truncation messages) rather than being cut
        // short. So a declared column holds its
        // declaration after INSERT … EXEC, as after INSERT.
        string[] setup = ["CREATE PROCEDURE dbo.obs_conv @v varchar(10) AS SELECT @v AS v;"];
        const string Table = "CREATE TABLE #obs_t (v int NOT NULL);";

        Assert.Equal("v: 12", (await Run(setup, Table + " INSERT INTO #obs_t EXEC dbo.obs_conv '12'; SELECT v FROM #obs_t;")).ToString());
        Assert.Equal(8114, (await Run(setup, Table + " INSERT INTO #obs_t EXEC dbo.obs_conv 'x';")).Error);
        Assert.Equal(515, (await Run(setup, Table + " INSERT INTO #obs_t EXEC dbo.obs_conv NULL;")).Error);
        Assert.Contains((await Run(setup, "CREATE TABLE #obs_s (v varchar(2)); INSERT INTO #obs_s EXEC dbo.obs_conv 'abc';")).Error, new int?[] { 8152, 2628 });
    }

    [Fact]
    public async Task Insert_exec_of_a_procedure_that_runs_for_json_fails()
    {
        // Error 13602: FOR JSON is not allowed in an INSERT statement, and inside INSERT … EXEC the callee's queries are part of
        // one. It is raised when the FOR JSON query runs, not when one is only in the callee: a branch not taken is fine. So
        // a successful INSERT … EXEC received no JSON result set, and the result sets it received are the rowsets.
        string[] setup = ["CREATE PROCEDURE dbo.obs_json @a int AS SELECT 1 AS v; IF @a = 1 SELECT 2 AS v FOR JSON PATH;"];

        Assert.Equal(13602, (await Run(setup, "CREATE TABLE #obs_t (v nvarchar(max)); INSERT INTO #obs_t EXEC dbo.obs_json 1;")).Error);
        Assert.Equal("n: 1", (await Run(setup, "CREATE TABLE #obs_t (v nvarchar(max)); INSERT INTO #obs_t EXEC dbo.obs_json 0; SELECT COUNT(*) AS n FROM #obs_t;")).ToString());
    }

    [Fact]
    public async Task Insert_exec_cannot_be_nested()
    {
        // Error 8164 when a procedure run by INSERT … EXEC runs INSERT … EXEC itself.
        string[] setup = [
            "CREATE PROCEDURE dbo.obs_leaf AS SELECT 1 AS v;",
            "CREATE PROCEDURE dbo.obs_mid AS CREATE TABLE #obs_m (v int); INSERT INTO #obs_m EXEC dbo.obs_leaf; SELECT v FROM #obs_m;",
        ];

        Assert.Equal(8164, (await Run(setup, "CREATE TABLE #obs_t (v int); INSERT INTO #obs_t EXEC dbo.obs_mid;")).Error);
    }

    [Fact]
    public async Task Insert_exec_into_a_table_variable_works()
    {
        string[] setup = ["CREATE PROCEDURE dbo.obs_leaf AS SELECT 1 AS v;"];

        Assert.Equal("v: 1", (await Run(setup, "DECLARE @t TABLE (v int); INSERT INTO @t EXEC dbo.obs_leaf; SELECT v FROM @t;")).ToString());
    }

    // ---- nesting and recursion ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Calls_nest_at_most_32_deep()
    {
        // Error 217 at the 33rd level. A recursion that has not ended by then fails the call, so it returns nothing the IR
        // describes; the IR does not express the bound.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_deep @n int AS
            IF @n > 0 BEGIN DECLARE @m int = @n - 1; EXEC dbo.obs_deep @m; END
            ELSE SELECT @@NESTLEVEL AS lvl;
            """];

        Assert.Equal("lvl: 32", (await Run(setup, "EXEC dbo.obs_deep 31")).ToString());
        Assert.Equal(217, (await Run(setup, "EXEC dbo.obs_deep 32")).Error);
    }

    // ---- which procedure a name runs ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_system_procedures_name_runs_the_system_procedure_whatever_the_project_defines()
    {
        // A procedure of the database named like a system one (sp_help) is never what EXEC sp_help runs, nor EXEC
        // dbo.sp_help. One whose name no system procedure has runs as any other.
        string[] setup = [
            "CREATE PROCEDURE dbo.sp_help @objname nvarchar(776) = NULL AS SELECT N'mine' AS who;",
            "CREATE PROCEDURE dbo.sp_obs_own AS SELECT N'mine' AS who;",
        ];

        Assert.NotEqual("who: mine", (await Run(setup, "EXEC sp_help N'dbo.sp_obs_own'")).ToString());
        Assert.NotEqual("who: mine", (await Run(setup, "EXEC dbo.sp_help N'dbo.sp_obs_own'")).ToString());
        Assert.Equal("who: mine", (await Run(setup, "EXEC sp_obs_own")).ToString());
        Assert.Equal("who: mine", (await Run(setup, "EXEC dbo.sp_obs_own")).ToString());
    }
}

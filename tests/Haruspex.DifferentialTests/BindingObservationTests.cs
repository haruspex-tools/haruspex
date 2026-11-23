using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// How SQL Server resolves the names in a query, observed and kept as tests: the scoping rules the binder in
/// Haruspex.SqlServer.Semantics (<c>Binder</c>) rests on, beyond those the <c>scopes</c> fixture shows in whole procedures.
/// Each test creates what it needs in a write session, rolled back. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
/// <remarks>
/// The rules for full names (a column or star qualified by its table's schema and name) were also run by hand at every
/// compatibility level from 100 to 170, on 15 Nov 2026, with the same outcome at each. A test can't change the level: it runs
/// in a transaction, which <c>ALTER DATABASE</c> can't, and the level is the whole database's.
/// </remarks>
public partial class BindingObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call) => ObservedCall.Run(server, setup, call, errorsAsMessages: false);

    /// <summary>A table of two rows, whose codes are not their ids, so a column read from the wrong source shows.</summary>
    private const string Rows = """
        CREATE TABLE dbo.obs_t (id int NOT NULL PRIMARY KEY, code int NOT NULL);
        INSERT INTO dbo.obs_t VALUES (1, 10), (2, 20);
        """;

    private async Task<string> Select(string query) => (await Run([Rows], query)).ToString();

    [Fact]
    public async Task An_unqualified_column_a_cte_lacks_is_the_enclosing_query_s_whatever_the_cte_is()
    {
        // The CTE has one column, n, so code in the subquery reading it is the enclosing query's: SQL Server binds a name in
        // the innermost scope that has it, by the names its sources have, and a recursive CTE of two anchors, which haruspex
        // doesn't read the rows of, has its names all the same.
        Assert.Equal(
            "id,outer_code: 1,10;2,20",
            await Select("""
                WITH c AS (SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT c.n + 1 FROM c WHERE c.n < 5)
                SELECT t.id, (SELECT TOP (1) code FROM c) AS outer_code FROM dbo.obs_t AS t ORDER BY t.id;
                """));
    }

    [Theory]
    [InlineData("CREATE SYNONYM dbo.obs_a FOR dbo.obs_b;", "CREATE SYNONYM dbo.obs_b FOR dbo.obs_t;")]
    [InlineData("CREATE SYNONYM dbo.obs_a FOR dbo.obs_b;", "CREATE SYNONYM dbo.obs_b FOR dbo.obs_a;")]
    [InlineData("CREATE SYNONYM dbo.obs_a FOR dbo.obs_a;", "SELECT 1 AS nothing_more;")]
    // A synonym's target isn't checked when it is created, so a chain, a cycle and a synonym for itself are all created; each is
    // refused when read (Msg 470), so haruspex reads one as unknown rather than following it (Binder.Through).
    public async Task Synonym_chaining_is_refused_when_read(string first, string second) =>
        Assert.Equal(470, (await Run([Rows, first, second], "SELECT x.id FROM dbo.obs_a AS x;")).Error);

    [Fact]
    public async Task A_temporary_table_named_with_a_schema_database_or_server_is_the_sessions_own()
    {
        // SQL Server ignores every part of a temporary table's name but the last ("Database name 'tempdb' ignored"), even a
        // server it has no link to, for every statement that makes, writes, reads or drops it: so Binder.Temporary reads
        // dbo.#t, tempdb..#t and srv.tempdb.dbo.#t as #t, and a write through any of them changes the table.
        Assert.Equal(
            "a: 2;3;4;10",
            await Select("""
                CREATE TABLE nowhere.#obs (a int NOT NULL);
                INSERT INTO dbo.#obs VALUES (1);
                INSERT INTO master.dbo.#obs VALUES (2);
                INSERT INTO tempdb..#obs VALUES (3);
                INSERT INTO srv.tempdb.dbo.#obs VALUES (4);
                UPDATE tempdb..#obs SET a = a * 10 WHERE a = 1;
                SELECT * INTO dbo.#copy FROM tempdb.dbo.#obs;
                DROP TABLE tempdb..#obs;
                SELECT a FROM #copy ORDER BY a;
                """));
    }

    [Theory]
    // Two names that differ only in case are refused where names are compared without case: two columns in a table of this
    // database or of tempdb (2705), two variables in a batch under this server's collation (134). On a database of
    // Latin1_General_100_CS_AS the table is made, and its columns code and Code are two (observed by hand, 22 Nov 2026). So a pair
    // proves names are compared by case, and haruspex reads a reference to either as unknown (decision 0107).
    [InlineData("CREATE TABLE dbo.obs_case (Code varchar(10) NULL, code int NULL);", 2705)]
    [InlineData("CREATE TABLE #obs_case (Code varchar(10) NULL, code int NULL);", 2705)]
    [InlineData("DECLARE @Total int; DECLARE @total varchar(10);", 134)]
    public async Task Names_that_differ_only_in_case_are_refused_where_names_are_compared_without_case(string batch, int error) =>
        Assert.Equal(error, (await Run([Rows], batch)).Error);
}

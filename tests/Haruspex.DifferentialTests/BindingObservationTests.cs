using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// How SQL Server resolves the names in a query, observed and kept as tests: the scoping rules the binder in
/// Haruspex.SqlServer.Semantics (<c>Binder</c>) rests on, beyond those the <c>scopes</c> fixture shows in whole procedures.
/// Each test creates what it needs in a write session, rolled back. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class BindingObservationTests(SqlServerFixture server)
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
}

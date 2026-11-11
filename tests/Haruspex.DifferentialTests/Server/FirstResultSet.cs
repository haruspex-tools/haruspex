namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// What SQL Server says a query's first result set is, before running it (<c>sys.dm_exec_describe_first_result_set</c>):
/// how the observation tests read the engine's typing rules, which no value can show.
/// </summary>
/// <remarks>
/// Here rather than in <c>tests/Shared</c> because it needs the server, which only this project can reach.
/// </remarks>
public static class FirstResultSet
{
    /// <summary>The server's description of a query's columns: <c>name type</c>, comma-separated.</summary>
    /// <param name="server">The server to ask.</param>
    /// <param name="select">The query, as it would be run.</param>
    /// <param name="setup">Statements run first in the same session, such as creating a temporary table the query reads.</param>
    public static async Task<string> Describe(SqlServerFixture server, string select, string[]? setup = null)
    {
        var query = select.Replace("'", "''", StringComparison.Ordinal);
        var outcome = await ObservedCall.Run(server, setup ?? [], $"SELECT name + ' ' + system_type_name AS c FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;");
        Assert.Null(outcome.Error);
        return outcome.Sets[0]["c: ".Length..].Replace(";", ", ", StringComparison.Ordinal);
    }
}

using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// How far an isolation level a procedure sets reaches, which visible read-uncommitted rests on (slice 6f; Haruspex.SqlServer.Inference's
/// README.md, "Cardinality"). That a scan under read-uncommitted can return a row twice is not observed here: it needs another
/// session splitting pages during the scan. It is documented by Microsoft for the <c>NOLOCK</c> and <c>READUNCOMMITTED</c> hints and
/// the isolation level, and haruspex takes it as decided (haruspex-design.md, "What can and cannot be typed", 10 Nov 2026), which
/// only claims less. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class ReadUncommittedObservationTests(SqlServerFixture server)
{
    /// <summary>The session's isolation level as sys.dm_exec_sessions gives it: 1 read uncommitted, 2 read committed.</summary>
    private const string Level = "SELECT transaction_isolation_level AS level FROM sys.dm_exec_sessions WHERE session_id = @@SPID;";

    [Fact]
    public async Task A_procedures_isolation_level_holds_in_the_procedures_it_calls_and_ends_when_it_returns()
    {
        string[] setup = [
            $"CREATE PROCEDURE dbo.obs_level AS {Level}",
            $"CREATE PROCEDURE dbo.obs_dirty AS SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; EXEC dbo.obs_level; {Level}",
        ];

        // The callee sees its caller's level; the caller's own is back once the procedure that set another returns.
        Assert.Equal(
            "level: 2 | level: 1 | level: 1 | level: 2",
            (await ObservedCall.Run(server, setup, $"SET TRANSACTION ISOLATION LEVEL READ COMMITTED; EXEC dbo.obs_level; EXEC dbo.obs_dirty; {Level}")).ToString());
    }
}

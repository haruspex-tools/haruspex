using System.Text.RegularExpressions;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A run that writes must never overlap a run that reads the same database (README.md, "Runs that write"). GitHub's
/// CI lost four tests to SQL Server error 1205 when a run's setup deleted rows that another run was reading.
/// </summary>
public partial class IsolationTests(SqlServerFixture server)
{
    /// <summary>
    /// The deadlock, made certain: a writing run's setup is held open while a reading run of the same tables starts.
    /// Before the gate the reader waited on the writer's locks every time (LCK_M_S on bom.part's key), the first half
    /// of the 1205 seen in CI. Now it must wait at the gate instead, never touch the tables while they are being
    /// written, and see the seeded rows once the writer has rolled back.
    /// </summary>
    [Fact]
    public async Task A_run_that_writes_never_overlaps_a_run_that_reads()
    {
        server.Require();
        var token = TestContext.Current.CancellationToken;
        var writing = Runs.Named("trees: list_parts, no parts");
        var reading = Runs.Named("trees: list_parts");

        Task<IReadOnlyList<ObservedResultSet>> read;
        string? blocked;
        await using (var writer = await server.WriteAsync(token))
        {
            // The writing run's setup, held open: its locks stay until the session rolls back.
            await using (var setup = writer.Command(writing.Setup!))
                await setup.ExecuteNonQueryAsync(token);

            read = server.RunAsync(reading, token);
            await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(1), token));

            await using var waiting = writer.Command(
                "SELECT TOP (1) CONCAT('session ', session_id, ' waits on ', wait_type, ' for ', wait_resource) FROM sys.dm_exec_requests WHERE blocking_session_id = @@SPID;");
            blocked = await waiting.ExecuteScalarAsync(token) as string;
            Assert.False(read.IsCompleted, "The reading run ended while the writing run was still open, so the two overlapped.");
        }

        var rows = Assert.Single(await read).Rows;
        Assert.True(blocked is null, $"A run that reads waited on the locks of a run that writes ({blocked}); with another lock the other way round, that is a deadlock.");
        Assert.NotEmpty(rows);
    }

    /// <summary>
    /// A read session cannot write, so a test that writes through one fails here and now, not as a deadlock in some
    /// other test, some other day. Each statement is in a transaction that is rolled back and touches no row, so a
    /// read session that could write would change nothing while this test failed.
    /// </summary>
    [Theory]
    [InlineData("BEGIN TRANSACTION; DELETE FROM bom.part WHERE 1 = 0; ROLLBACK;", 229)]
    [InlineData("BEGIN TRANSACTION; INSERT INTO cat.item (item_id) SELECT 0 WHERE 1 = 0; ROLLBACK;", 229)]
    [InlineData("BEGIN TRANSACTION; CREATE TABLE dbo.obs_never (i int NULL); ROLLBACK;", 262)]
    public async Task A_read_session_cannot_write(string sql, int error)
    {
        server.Require();
        await using var session = await server.ReadAsync(TestContext.Current.CancellationToken);
        await using var command = session.Command(sql);

        var e = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal(error, e.Number);
    }

    [Fact]
    public async Task A_read_session_may_use_a_temporary_table_of_its_own()
    {
        server.Require();
        await using var session = await server.ReadAsync(TestContext.Current.CancellationToken);
        await using var command = session.Command("CREATE TABLE #t (i int NOT NULL); INSERT INTO #t VALUES (1); SELECT i FROM #t;");

        Assert.Equal(1, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The fixture gives no connection string away, but a test could still build one; this keeps every connection
    /// to the test database inside the fixture, where the gate is.
    /// </summary>
    [Fact]
    public void No_test_opens_a_connection_of_its_own()
    {
        var project = Path.Combine(Paths.RepoRoot, "tests", "Haruspex.DifferentialTests");
        string[] allowed = [Path.Combine("Server", "SqlServerFixture.cs"), Path.Combine("Server", "Deployer.cs")];

        var offenders = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(project, f))
            .Where(f => !f.StartsWith("bin", StringComparison.Ordinal) && !f.StartsWith("obj", StringComparison.Ordinal))
            .Where(f => !allowed.Contains(f, StringComparer.Ordinal))
            .Where(f => NewConnection().IsMatch(File.ReadAllText(Path.Combine(project, f))))
            .ToList();

        Assert.True(offenders.Count == 0, "These open a connection of their own, around the gate. Use SqlServerFixture.ReadAsync or WriteAsync:\n" + string.Join("\n", offenders));
    }

    [GeneratedRegex(@"new\s+SqlConnection\s*\(")]
    private static partial Regex NewConnection();
}

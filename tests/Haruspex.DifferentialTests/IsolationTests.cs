using System.Text.RegularExpressions;
using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A run that writes must never share a database with a run that reads (README.md, "Runs that write"). GitHub's CI lost
/// four tests to SQL Server error 1205 when a run's setup deleted rows that another run was reading.
/// </summary>
public class IsolationTests(SqlServerFixture server)
{
    /// <summary>
    /// The deadlock, made impossible: a writing run's setup is held open while a reading run of the same tables runs.
    /// With one database the reader waited on the writer's locks every time (LCK_M_S on bom.part's key), the first half
    /// of the 1205 seen in CI; then a gate made every write wait for every read, and the suite nearly serial. Now the
    /// writer is in a database of its own, so the reader runs to its end while the writer is still open, waits on no
    /// lock of it, and sees the seeded rows the writer deleted.
    /// </summary>
    [Fact]
    public async Task A_run_that_writes_never_shares_a_database_with_a_run_that_reads()
    {
        await server.RequireAsync();
        var token = TestContext.Current.CancellationToken;
        var writing = Runs.Named("trees: list_parts, no parts");
        var reading = Runs.Named("trees: list_parts");

        await using var writer = await server.WriteAsync(token);
        // The writing run's setup, held open: its locks stay until the session rolls back.
        await using (var setup = writer.Command(writing.Setup!))
            await setup.ExecuteNonQueryAsync(token);
        await using (var empty = writer.Command("SELECT COUNT(*) FROM bom.part;"))
            Assert.Equal(0, await empty.ExecuteScalarAsync(token));

        var read = server.RunAsync(reading, token);
        var ended = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(30), token));

        await using var waiting = writer.Command(
            "SELECT TOP (1) CONCAT('session ', session_id, ' waits on ', wait_type, ' for ', wait_resource) FROM sys.dm_exec_requests WHERE blocking_session_id = @@SPID;");
        var blocked = await waiting.ExecuteScalarAsync(token) as string;
        Assert.True(blocked is null, $"A run that reads waited on the locks of a run that writes ({blocked}); with another lock the other way round, that is a deadlock.");
        Assert.True(ended == read, "The reading run didn't end within 30 seconds while the writing run was open, so it waited for it.");
        Assert.NotEmpty(Assert.Single(await read).Rows);
    }

    /// <summary>
    /// A read session cannot write, so a test that writes through one fails here and now, not as a deadlock in some
    /// other test, some other day. Each statement touches no row, so even a read session that could write would change
    /// nothing while this test failed. 229 is the reader's missing permission; 3906, that the snapshot is read-only.
    /// </summary>
    [Theory]
    [InlineData("DELETE FROM bom.part WHERE 1 = 0;", 229)]
    [InlineData("INSERT INTO cat.item (item_id) SELECT 0 WHERE 1 = 0;", 229)]
    [InlineData("CREATE TABLE dbo.obs_never (i int NULL);", 3906)]
    public async Task A_read_session_cannot_write(string sql, int error)
    {
        await server.RequireAsync();
        await using var session = await server.ReadAsync(TestContext.Current.CancellationToken);
        await using var command = session.Command(sql);

        var e = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal(error, e.Number);
    }

    /// <summary>
    /// A dbo procedure writes even when the reader calls it, because ownership chaining skips the permission check
    /// (dbo.gate_probe_write, made for this by the fixture). But a read session reads a snapshot, which no one can
    /// write, so a run whose procedure writes fails without a setup, at once, with 3906.
    /// </summary>
    [Fact]
    public async Task A_run_whose_procedure_writes_fails_without_a_setup()
    {
        await server.RequireAsync();
        var token = TestContext.Current.CancellationToken;
        var run = new ProcedureRun("probe: writes", "trees", "dbo.gate_probe_write", new Dictionary<string, RunArgument>());

        var e = await Assert.ThrowsAsync<SqlException>(() => server.RunAsync(run, token));

        Assert.Equal(3906, e.Number);
        // With a setup it runs in a write session, where it may.
        Assert.Empty(await server.RunAsync(run with { Setup = "SET NOCOUNT ON;" }, token));
    }

    /// <summary>
    /// The switch to the reader was made WITH COOKIE, which only the session holds, so a test cannot REVERT to the
    /// login's own user (15591).
    /// </summary>
    [Fact]
    public async Task A_read_session_cannot_switch_back_to_the_login_user()
    {
        await server.RequireAsync();
        var token = TestContext.Current.CancellationToken;
        await using var session = await server.ReadAsync(token);
        await using (var revert = session.Command("REVERT;"))
            Assert.Equal(15591, (await Assert.ThrowsAsync<SqlException>(() => revert.ExecuteNonQueryAsync(token))).Number);

        await using var who = session.Command("SELECT USER_NAME();");
        Assert.Equal("haruspex_reader", await who.ExecuteScalarAsync(token));
    }

    /// <summary>An impersonated user is confined to its database, so a test cannot USE the writable one (916).</summary>
    [Fact]
    public async Task A_read_session_cannot_reach_the_test_database_itself()
    {
        await server.RequireAsync();
        var token = TestContext.Current.CancellationToken;
        await using var session = await server.ReadAsync(token);
        await using var escape = session.Command($"USE [{server.Database}];");

        Assert.Equal(916, (await Assert.ThrowsAsync<SqlException>(() => escape.ExecuteNonQueryAsync(token))).Number);
    }

    [Fact]
    public async Task A_read_session_may_use_a_temporary_table_of_its_own()
    {
        await server.RequireAsync();
        await using var session = await server.ReadAsync(TestContext.Current.CancellationToken);
        await using var command = session.Command("CREATE TABLE #t (i int NOT NULL); INSERT INTO #t VALUES (1); SELECT i FROM #t;");

        Assert.Equal(1, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The fixture gives no connection string away, but a test could still build one; this keeps every connection
    /// to the test databases inside the fixture and the sessions, which keep reads and writes apart. It looks for the types' names,
    /// not for one way of constructing them, so <c>new(…)</c>, a fully qualified name and a provider factory are all
    /// caught. It catches a mistake; it cannot stop someone set on going around it.
    /// </summary>
    [Fact]
    public void No_test_opens_a_connection_of_its_own()
    {
        var project = Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.DifferentialTests");
        string[] allowed =
        [
            Path.Combine("Server", "SqlServerFixture.cs"),
            Path.Combine("Server", "Deployer.cs"),
            Path.Combine("Server", "DatabaseSession.cs"),
            Path.Combine("Server", "ReadSession.cs"),
            Path.Combine("Server", "WriteSession.cs"),

            // A database of one test's own, never the shared one, for observations that need two sessions' commits.
            Path.Combine("Server", "ScratchDatabase.cs"),
            Path.Combine("Server", "ScratchSession.cs"),

            // A server of its own, with full-text search, on which each test has a database of its own.
            Path.Combine("Server", "FullTextServer.cs"),

            // A server of its own, which may call a REST endpoint; its tests create nothing there.
            Path.Combine("Server", "RestServer.cs"),
        ];

        var offenders = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(project, f))
            .Where(f => !f.StartsWith("bin", StringComparison.Ordinal) && !f.StartsWith("obj", StringComparison.Ordinal))
            .Where(f => !allowed.Contains(f, StringComparer.Ordinal))
            .Where(f => NewConnection().IsMatch(File.ReadAllText(Path.Combine(project, f))))
            .ToList();

        Assert.True(offenders.Count == 0, "These name a connection type, so may open a connection of their own around the sessions. Use SqlServerFixture.ReadAsync or WriteAsync:\n" + string.Join("\n", offenders));
    }

    // Built from pieces so that this file, which is scanned like any other, does not name the types itself. A
    // connection string builder is allowed: it opens nothing.
    private static readonly Regex Connection = new(@"\b(?:Sql" + "Connection|Sql" + "ClientFactory|DbProvider" + @"Factories)\b", RegexOptions.CultureInvariant);

    private static Regex NewConnection() => Connection;
}

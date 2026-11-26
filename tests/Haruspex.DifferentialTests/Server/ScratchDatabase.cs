using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A database of one test's own, whose writes are committed, dropped when it is disposed: for an observation that needs two
/// sessions to see each other's committed rows, which the shared database's sessions, each alone and rolled back, never do
/// (<see cref="SqlServerFixture.ScratchAsync"/>). Its connections are not pooled, so none is left open to stop the drop.
/// </summary>
public sealed class ScratchDatabase : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly Func<Task> drop;

    internal ScratchDatabase(string connectionString, Func<Task> drop)
    {
        this.connectionString = connectionString;
        this.drop = drop;
    }

    /// <summary>The database as the mssql driver's config, in JSON, for the driver in Node; it holds the password, so it goes in the environment only.</summary>
    internal string DriverConfig => SqlServerFixture.MssqlConfig(connectionString);

    /// <summary>A new session in the database, opened.</summary>
    public async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Deploys a fixture's scripts into the database (<see cref="Deployer.DeployAsync"/>).</summary>
    internal Task DeployAsync(string directory) => Deployer.DeployAsync(connectionString, directory);

    /// <summary>A new session in the database, for running a procedure there (<see cref="ProcedureRunner"/>).</summary>
    public async Task<ScratchSession> SessionAsync(CancellationToken token) => new(await OpenAsync(token).ConfigureAwait(false));

    /// <summary>Runs <paramref name="batch"/> in a session of its own, committed.</summary>
    public async Task ExecuteAsync(string batch, CancellationToken token)
    {
        await using var connection = await OpenAsync(token).ConfigureAwait(false);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var command = new SqlCommand(batch, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>The session a connection runs in, to watch it from another (<see cref="InItsWaitforAsync"/>).</summary>
    public static async Task<short> SessionOfAsync(SqlConnection connection, CancellationToken token)
    {
        await using var spid = new SqlCommand("SELECT @@SPID;", connection);
        return (short)await spid.ExecuteScalarAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until <paramref name="session"/> is in a <c>WAITFOR DELAY</c>, watched from <paramref name="watcher"/>, so that the
    /// statements its batch or procedure runs before it have run and taken whatever locks they keep. An observation that races two
    /// sessions waits for this rather than a fixed time, which a slow runner outlasts (CI's ubuntu runner did, at b2c711f). Reading
    /// another session's request needs VIEW SERVER STATE, which the test login has.
    /// </summary>
    /// <param name="watcher">Another session, not busy.</param>
    /// <param name="session">The session watched (<see cref="SessionOfAsync"/>).</param>
    /// <param name="call">The watched session's call: if it ends first, it never waited, and that is an error.</param>
    /// <param name="token">Cancels the wait.</param>
    /// <exception cref="InvalidOperationException">The call ended before it was seen waiting.</exception>
    /// <exception cref="TimeoutException">It wasn't seen waiting within 30 seconds.</exception>
    public static async Task InItsWaitforAsync(SqlConnection watcher, short session, Task call, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(call);
        await using var request = new SqlCommand("SELECT wait_type FROM sys.dm_exec_requests WHERE session_id = @session;", watcher);
        request.Parameters.Add(new SqlParameter("@session", System.Data.SqlDbType.SmallInt) { Value = session });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (call.IsCompleted)
                throw new InvalidOperationException($"Session {session}'s call ended before it was seen in its WAITFOR: a guard that found no row returns before it.");
            if (await request.ExecuteScalarAsync(token).ConfigureAwait(false) is "WAITFOR")
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);
        }

        throw new TimeoutException($"Session {session} wasn't seen in a WAITFOR within 30 seconds.");
    }

    /// <summary>
    /// Waits until <paramref name="session"/> is blocked by <paramref name="blocker"/>, watched from <paramref name="watcher"/>,
    /// so that its statement has begun and waits on a lock the blocker holds. Like <see cref="InItsWaitforAsync"/>, it watches the
    /// server rather than a clock.
    /// </summary>
    /// <param name="watcher">Another session, not busy.</param>
    /// <param name="session">The session watched (<see cref="SessionOfAsync"/>).</param>
    /// <param name="blocker">The session whose lock it should wait on.</param>
    /// <param name="call">The watched session's call: if it ends first, it never waited, and that is an error.</param>
    /// <param name="token">Cancels the wait.</param>
    /// <exception cref="InvalidOperationException">The call ended before it was seen blocked.</exception>
    /// <exception cref="TimeoutException">It wasn't seen blocked within 30 seconds.</exception>
    public static async Task BlockedByAsync(SqlConnection watcher, short session, short blocker, Task call, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(call);
        await using var request = new SqlCommand("SELECT blocking_session_id FROM sys.dm_exec_requests WHERE session_id = @session;", watcher);
        request.Parameters.Add(new SqlParameter("@session", System.Data.SqlDbType.SmallInt) { Value = session });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (call.IsCompleted)
                throw new InvalidOperationException($"Session {session}'s call ended before it was seen blocked by session {blocker}.");
            if (await request.ExecuteScalarAsync(token).ConfigureAwait(false) is short blocking && blocking == blocker)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(10), token).ConfigureAwait(false);
        }

        throw new TimeoutException($"Session {session} wasn't seen blocked by session {blocker} within 30 seconds.");
    }

    public async ValueTask DisposeAsync() => await drop().ConfigureAwait(false);
}

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

    /// <summary>Runs <paramref name="batch"/> in a session of its own, committed.</summary>
    public async Task ExecuteAsync(string batch, CancellationToken token)
    {
        await using var connection = await OpenAsync(token).ConfigureAwait(false);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var command = new SqlCommand(batch, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await drop().ConfigureAwait(false);
}

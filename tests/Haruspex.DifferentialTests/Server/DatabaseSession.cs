using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A connection to a test database: the shared snapshot, or for a write session a room it holds until it is disposed
/// (<see cref="RoomPool"/>). Only <see cref="SqlServerFixture"/> hands them out (<see cref="SqlServerFixture.ReadAsync"/>,
/// <see cref="SqlServerFixture.WriteAsync"/>), and it gives no connection string away, so a test cannot reach a database
/// except through a session. <c>IsolationTests</c> fails if one opens a connection of its own.
/// </summary>
public abstract class DatabaseSession : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly IDisposable? pass;

    private protected DatabaseSession(SqlConnection connection, SqlTransaction? transaction, IDisposable? pass)
    {
        this.connection = connection;
        Transaction = transaction;
        this.pass = pass;
    }

    private protected SqlTransaction? Transaction { get; }

    /// <summary>A command on this session's connection, inside its transaction if it has one.</summary>
#pragma warning disable CA2100 // The tests' own SQL, and the repository's: running it is the point.
    public SqlCommand Command(string sql) => new(sql, connection, Transaction);
#pragma warning restore CA2100

    /// <summary>A bulk copy on this session's connection, inside its transaction if it has one, as an application loads rows.</summary>
    public SqlBulkCopy BulkCopy(SqlBulkCopyOptions options) => new(connection, options, Transaction);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await EndAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                // Last, so no other session is given the room while this one's locks may still be held.
                pass?.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    private protected virtual Task EndAsync() => Task.CompletedTask;

    /// <summary>Keeps this session's connection out of the pool, so no later session is handed it.</summary>
    private protected void Discard() => SqlConnection.ClearPool(connection);
}

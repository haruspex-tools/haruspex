using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A connection to the test database that holds its place in the <see cref="DatabaseGate"/> until it is disposed.
/// Only <see cref="SqlServerFixture"/> hands them out (<see cref="SqlServerFixture.ReadAsync"/>,
/// <see cref="SqlServerFixture.WriteAsync"/>), and it gives no connection string away, so a test cannot reach the
/// database except through the gate. <c>IsolationTests</c> fails if one opens a connection of its own.
/// </summary>
public abstract class DatabaseSession : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly IDisposable pass;

    private protected DatabaseSession(SqlConnection connection, SqlTransaction? transaction, IDisposable pass)
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
                // Last, so no other session starts while this one's locks may still be held.
                pass.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    private protected virtual Task EndAsync() => Task.CompletedTask;

    /// <summary>Keeps this session's connection out of the pool, so no later session is handed it.</summary>
    private protected void Discard() => SqlConnection.ClearPool(connection);
}

/// <summary>
/// A session that only reads, shared with any number of others. SQL Server holds it to that, so a test or a fixture
/// procedure that writes through one fails at once, rather than deadlocking some other test by luck:
/// <list type="bullet">
/// <item>it reads a database snapshot of the test database taken after seeding, which is read-only whoever writes and
/// however: even a dbo procedure, which ownership chaining would let write past any permission, fails with error
/// 3906;</item>
/// <item>it runs as a database user that may only read and execute, switched to WITH COOKIE, and only this class
/// holds the cookie. So a test cannot REVERT to the login's own user (error 15591), and cannot USE the test database
/// itself, because an impersonated user is confined to its database (error 916).</item>
/// </list>
/// Temporary tables are allowed: they are the session's own, in tempdb. Observed on 9 Nov 2026 on the pinned image.
/// </summary>
public sealed class ReadSession : DatabaseSession
{
    private readonly byte[] cookie;

    internal ReadSession(SqlConnection connection, byte[] cookie, IDisposable pass)
        : base(connection, null, pass)
    {
        this.cookie = cookie;
    }

    /// <summary>
    /// Goes back to the login's own user before the connection returns to the pool. The pool resets a connection
    /// when it is reused, and SQL Server kills a session reset while it is still impersonating ("the session is in
    /// the kill state", observed on 9 Nov 2026). A connection that cannot revert is taken out of the pool instead.
    /// </summary>
    private protected override async Task EndAsync()
    {
        try
        {
            await using var revert = Command($"REVERT WITH COOKIE = 0x{Convert.ToHexString(cookie)};");
            await revert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            Discard();
        }
    }
}

/// <summary>
/// A session that writes, alone in the database while it lasts. Everything runs in one transaction, rolled back when
/// the session is disposed, so what a test writes never reaches another test.
/// </summary>
public sealed class WriteSession : DatabaseSession
{
    internal WriteSession(SqlConnection connection, SqlTransaction transaction, IDisposable pass)
        : base(connection, transaction, pass)
    {
    }

    private protected override async Task EndAsync()
    {
        // A statement that failed may already have ended the transaction (XACT_ABORT, or a severe error).
        if (Transaction!.Connection is not null)
            await Transaction.RollbackAsync().ConfigureAwait(false);
        await Transaction.DisposeAsync().ConfigureAwait(false);
    }
}

using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A session that writes, alone in a database of its own while it lasts (<see cref="RoomPool"/>). Everything runs in one
/// transaction, rolled back when the session is disposed, so what a test writes never reaches another test.
/// </summary>
public sealed class WriteSession : DatabaseSession
{
    internal WriteSession(SqlConnection connection, SqlTransaction transaction, IDisposable room)
        : base(connection, transaction, room)
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

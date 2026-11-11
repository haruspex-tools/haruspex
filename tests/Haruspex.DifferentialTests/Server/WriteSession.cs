using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

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

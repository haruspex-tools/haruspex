using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

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

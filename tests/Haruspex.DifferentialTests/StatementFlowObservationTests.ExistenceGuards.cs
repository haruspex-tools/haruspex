using System.Diagnostics;
using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

public partial class StatementFlowObservationTests
{
    // ---- what an existence guard proves of a later read --------------------------------------------------------------
    //
    // Each case runs a procedure that tests the row exists, waits, and reads it again, while a second session deletes the
    // row during the wait and commits. Both sessions need committed rows, so each test has a database of its own
    // (SqlServerFixture.ScratchAsync).

    /// <summary>
    /// Runs the guard and the read with <paramref name="before"/> first, <paramref name="hint"/> on the guard's table,
    /// <paramref name="between"/> straight after the guard and <paramref name="read"/> on the read's table, while another
    /// session deletes the row during the wait; gives what the read found, and whether the delete had to wait.
    /// </summary>
    private static async Task<string> Raced(ScratchDatabase scratch, string before, string hint, string between = "", string read = "")
    {
        var token = TestContext.Current.CancellationToken;
        await scratch.ExecuteAsync("""
            DROP TABLE IF EXISTS dbo.obs_order;
            CREATE TABLE dbo.obs_order (order_id int NOT NULL PRIMARY KEY, placed_on date NOT NULL);
            INSERT INTO dbo.obs_order VALUES (1, '2026-11-16');
            """, token);
        await scratch.ExecuteAsync($"""
            CREATE OR ALTER PROCEDURE dbo.obs_card @id int AS
            SET NOCOUNT ON;
            {before}
            IF NOT EXISTS (SELECT 1 FROM dbo.obs_order {hint} WHERE order_id = @id) RETURN;
            {between}
            WAITFOR DELAY '00:00:01.500';
            SELECT (SELECT o.placed_on FROM dbo.obs_order AS o {read} WHERE o.order_id = @id) AS placed_on;
            WHILE @@TRANCOUNT > 0 COMMIT;
            """, token);

        await using var guard = await scratch.OpenAsync(token);
        await using var deleter = await scratch.OpenAsync(token);
        var session = await ScratchDatabase.SessionOfAsync(guard, token);
        await using var call = new SqlCommand("EXEC dbo.obs_card 1;", guard);
        var reading = call.ExecuteScalarAsync(token);
        await ScratchDatabase.InItsWaitforAsync(deleter, session, reading, token);

        var clock = Stopwatch.StartNew();
        await using (var delete = new SqlCommand("DELETE FROM dbo.obs_order WHERE order_id = 1;", deleter))
            await delete.ExecuteNonQueryAsync(token);
        var waited = clock.Elapsed > TimeSpan.FromMilliseconds(700);

        var found = await reading;
        return $"{(found is DateTime d ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "null")}, delete {(waited ? "waited" : "did not wait")}";
    }

    [Fact]
    public async Task A_row_an_existence_guard_saw_may_be_gone_when_a_later_statement_reads_it()
    {
        // Each statement reads what is committed when it runs, and its locks end with it unless a transaction holds them, so
        // another session's delete lands between the two statements and the read finds nothing. Neither locking hints
        // without a transaction, nor a transaction without them, nor a level that holds locks without a transaction, nor
        // read committed snapshot (each statement its own snapshot), nor snapshot isolation without a transaction, holds it.
        await using var scratch = await server.ScratchAsync();

        Assert.Equal("null, delete did not wait", await Raced(scratch, "", ""));
        Assert.Equal("null, delete did not wait", await Raced(scratch, "", "WITH (UPDLOCK, HOLDLOCK)"));
        Assert.Equal("null, delete did not wait", await Raced(scratch, "BEGIN TRAN;", ""));
        Assert.Equal("null, delete did not wait", await Raced(scratch, "SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;", ""));

        // A transaction that holds the locks, committed before the read, gives them up.
        Assert.Equal("null, delete did not wait", await Raced(scratch, "BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "COMMIT;"));

        var token = TestContext.Current.CancellationToken;
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", token);
        Assert.Equal("null, delete did not wait", await Raced(scratch, "SET TRANSACTION ISOLATION LEVEL SNAPSHOT;", ""));
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;", token);
        Assert.Equal("null, delete did not wait", await Raced(scratch, "BEGIN TRAN;", ""));
    }

    [Theory]
    [InlineData("BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "")]
    [InlineData("BEGIN TRAN;", "WITH (HOLDLOCK)", "")]
    [InlineData("BEGIN TRAN;", "WITH (SERIALIZABLE)", "")]
    [InlineData("BEGIN TRAN;", "WITH (UPDLOCK)", "")]
    [InlineData("BEGIN TRAN;", "WITH (REPEATABLEREAD)", "")]
    [InlineData("BEGIN TRAN;", "WITH (XLOCK)", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN;", "", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ; BEGIN TRAN;", "", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ; BEGIN TRAN;", "WITH (READPAST)", "")]
    [InlineData("BEGIN TRAN; BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "COMMIT;")]
    public async Task A_lock_the_guard_holds_to_the_end_of_an_open_transaction_keeps_the_row_for_the_read(string before, string hint, string between)
    {
        // The guard's lock on the row lasts until the transaction ends, so the delete waits for it and the read finds the
        // row. An inner COMMIT of a nested transaction ends nothing: the outer one still holds the lock. READPAST only skips
        // rows other sessions have locked, so it keeps the level's lock on the row it reads.
        await using var scratch = await server.ScratchAsync();

        Assert.Equal("2026-11-16, delete waited", await Raced(scratch, before, hint, between));
    }

    [Theory]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN;", "WITH (READCOMMITTED)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN;", "WITH (NOLOCK)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ; BEGIN TRAN;", "WITH (NOLOCK)")]
    public async Task A_hint_on_the_guard_that_lowers_the_level_holds_no_lock_for_the_read(string before, string hint)
    {
        // A table hint overrides the session's level for that table, so a guard read with one that takes no lock, or gives
        // it up at once, leaves the row free to be deleted however high the level is.
        await using var scratch = await server.ScratchAsync();

        Assert.Equal("null, delete did not wait", await Raced(scratch, before, hint));
    }

    /// <summary>
    /// A snapshot transaction whose snapshot starts at a first read of another table, then waits while another session inserts
    /// and commits the row, then runs the guard with <paramref name="hint"/> and reads the row: what the read found, or that
    /// the guard returned.
    /// </summary>
    private static async Task<string> InsertedAfterTheSnapshot(ScratchDatabase scratch, string hint)
    {
        var token = TestContext.Current.CancellationToken;
        await scratch.ExecuteAsync("""
            DROP TABLE IF EXISTS dbo.obs_order;
            DROP TABLE IF EXISTS dbo.obs_other;
            CREATE TABLE dbo.obs_order (order_id int NOT NULL PRIMARY KEY, placed_on date NOT NULL);
            CREATE TABLE dbo.obs_other (i int NOT NULL);
            """, token);
        await scratch.ExecuteAsync($"""
            CREATE OR ALTER PROCEDURE dbo.obs_card @id int AS
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
            BEGIN TRAN;
            DECLARE @n int = (SELECT COUNT(*) FROM dbo.obs_other);
            WAITFOR DELAY '00:00:01.500';
            IF NOT EXISTS (SELECT 1 FROM dbo.obs_order {hint} WHERE order_id = @id) BEGIN COMMIT; RETURN; END
            SELECT (SELECT o.placed_on FROM dbo.obs_order AS o WHERE o.order_id = @id) AS placed_on;
            COMMIT;
            """, token);

        await using var guard = await scratch.OpenAsync(token);
        await using var inserter = await scratch.OpenAsync(token);
        var session = await ScratchDatabase.SessionOfAsync(guard, token);
        await using var call = new SqlCommand("EXEC dbo.obs_card 1;", guard);
        var reading = call.ExecuteReaderAsync(token);
        await ScratchDatabase.InItsWaitforAsync(inserter, session, reading, token);
        await using (var insert = new SqlCommand("INSERT INTO dbo.obs_order VALUES (1, '2026-11-16');", inserter))
            await insert.ExecuteNonQueryAsync(token);

        await using var reader = await reading;
        if (!await reader.ReadAsync(token))
            return "returned";
        return await reader.IsDBNullAsync(0, token) ? "null" : reader.GetDateTime(0).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task A_guard_that_reads_past_the_snapshot_can_see_a_row_the_snapshot_read_does_not()
    {
        // The snapshot began before the insert. A guard with no hint reads the snapshot, doesn't see the row and returns; one
        // with READCOMMITTEDLOCK reads what is committed now, sees it, and the read, at snapshot, then finds nothing.
        await using var scratch = await server.ScratchAsync();
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", TestContext.Current.CancellationToken);

        Assert.Equal("returned", await InsertedAfterTheSnapshot(scratch, ""));
        Assert.Equal("null", await InsertedAfterTheSnapshot(scratch, "WITH (READCOMMITTEDLOCK)"));
    }

    [Fact]
    public async Task Snapshot_isolation_in_an_open_transaction_reads_the_row_the_guard_saw()
    {
        // Both statements read the transaction's one snapshot, so the delete goes ahead and the read still finds the row, as
        // long as the read is at snapshot isolation itself: with READCOMMITTEDLOCK, or the level set back to read committed
        // before it, it reads what is committed now. A locking hint on the read fails the call (3960, an update conflict).
        await using var scratch = await server.ScratchAsync();
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", TestContext.Current.CancellationToken);
        const string snapshot = "SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;";

        Assert.Equal("2026-11-16, delete did not wait", await Raced(scratch, snapshot, ""));
        Assert.Equal("null, delete did not wait", await Raced(scratch, snapshot, "", read: "WITH (READCOMMITTEDLOCK)"));
        Assert.Equal("null, delete did not wait", await Raced(scratch, snapshot, "", "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;"));
        Assert.Equal(3960, (await Assert.ThrowsAsync<SqlException>(() => Raced(scratch, snapshot, "", read: "WITH (UPDLOCK)"))).Number);
    }
}

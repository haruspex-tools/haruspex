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

    /// <summary>The error a statement raises when a lock it needs isn't granted within <c>SET LOCK_TIMEOUT</c>.</summary>
    private const int LockTimeout = 1222;

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

        // Whether the delete must wait is the server's answer, not a clock's, so a loaded runner can't flip it: with a lock
        // timeout of 0 a delete that would wait on the guard's lock fails at once with 1222 and deletes nothing. It is then
        // sent again with no timeout, and waits for the lock as an application's delete would, so the read sees what it would.
        var waited = false;
        await using (var attempt = new SqlCommand("SET LOCK_TIMEOUT 0; DELETE FROM dbo.obs_order WHERE order_id = 1;", deleter))
        {
            try
            {
                await attempt.ExecuteNonQueryAsync(token);
            }
            catch (SqlException e) when (e.Number == LockTimeout)
            {
                waited = true;
            }
        }

        // The answer is about the guard's lock during its wait only if the guard was still waiting after the attempt (it was
        // before it: InItsWaitforAsync). A runner that stalled past the wait raced nothing, which is no claim about the engine.
        await using (var still = new SqlCommand("SELECT wait_type FROM sys.dm_exec_requests WHERE session_id = @session;", deleter))
        {
            still.Parameters.Add(new SqlParameter("@session", System.Data.SqlDbType.SmallInt) { Value = session });
            if (await still.ExecuteScalarAsync(token) is not "WAITFOR")
                throw new InvalidOperationException($"Session {session} had left its WAITFOR by the time the delete was tried, so nothing was raced: run the test again.");
        }

        if (waited)
        {
            await using var delete = new SqlCommand("SET LOCK_TIMEOUT -1; DELETE FROM dbo.obs_order WHERE order_id = 1;", deleter);
            await delete.ExecuteNonQueryAsync(token);
        }

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
    /// and commits the row, then runs the guard with <paramref name="hint"/>, <paramref name="between"/>, and the read with
    /// <paramref name="read"/>: what the read found, or that the guard returned. The guard and the read are in a transaction
    /// the procedure begins; the snapshot transaction is the procedure's own, or with <paramref name="caller"/> its caller's,
    /// in which the procedure's nests and which it doesn't see.
    /// </summary>
    private static async Task<string> InsertedAfterTheSnapshot(ScratchDatabase scratch, string hint, string read = "", string between = "", bool caller = false)
    {
        var token = TestContext.Current.CancellationToken;
        await scratch.ExecuteAsync("""
            DROP TABLE IF EXISTS dbo.obs_order;
            DROP TABLE IF EXISTS dbo.obs_other;
            CREATE TABLE dbo.obs_order (order_id int NOT NULL PRIMARY KEY, placed_on date NOT NULL);
            CREATE TABLE dbo.obs_other (i int NOT NULL);
            """, token);
        const string snapshot = """
            SET TRANSACTION ISOLATION LEVEL SNAPSHOT;
            BEGIN TRAN;
            DECLARE @n int = (SELECT COUNT(*) FROM dbo.obs_other);
            WAITFOR DELAY '00:00:01.500';
            """;
        const string snapshotCaller = $"SET NOCOUNT ON; {snapshot} EXEC dbo.obs_card 1; COMMIT;";
        await scratch.ExecuteAsync($"""
            CREATE OR ALTER PROCEDURE dbo.obs_card @id int AS
            SET NOCOUNT ON;
            {(caller ? "BEGIN TRAN;" : snapshot)}
            IF NOT EXISTS (SELECT 1 FROM dbo.obs_order {hint} WHERE order_id = @id) BEGIN COMMIT; RETURN; END
            {between}
            SELECT (SELECT o.placed_on FROM dbo.obs_order AS o {read} WHERE o.order_id = @id) AS placed_on;
            COMMIT;
            """, token);

        await using var guard = await scratch.OpenAsync(token);
        await using var inserter = await scratch.OpenAsync(token);
        var session = await ScratchDatabase.SessionOfAsync(guard, token);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var call = new SqlCommand(caller ? snapshotCaller : "EXEC dbo.obs_card 1;", guard);
#pragma warning restore CA2100
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

    /// <summary>
    /// A snapshot transaction that reads nothing before the guard: the guard, with <c>UPDLOCK, HOLDLOCK</c>, waits on another
    /// session's uncommitted insert of the row, which commits once the guard is seen waiting, and then the read: what it found,
    /// or that the guard returned. The snapshot transaction is the procedure's own, or with <paramref name="caller"/> its
    /// caller's, begun straight before the call.
    /// </summary>
    private static async Task<string> CommittedWhileTheGuardWaits(ScratchDatabase scratch, bool caller)
    {
        var token = TestContext.Current.CancellationToken;
        await scratch.ExecuteAsync("""
            DROP TABLE IF EXISTS dbo.obs_order;
            CREATE TABLE dbo.obs_order (order_id int NOT NULL PRIMARY KEY, placed_on date NOT NULL);
            """, token);
        await scratch.ExecuteAsync($"""
            CREATE OR ALTER PROCEDURE dbo.obs_card @id int AS
            SET NOCOUNT ON;
            {(caller ? "" : "SET TRANSACTION ISOLATION LEVEL SNAPSHOT;")}
            BEGIN TRAN;
            IF NOT EXISTS (SELECT 1 FROM dbo.obs_order WITH (UPDLOCK, HOLDLOCK) WHERE order_id = @id) BEGIN COMMIT; RETURN; END
            SELECT (SELECT o.placed_on FROM dbo.obs_order AS o WHERE o.order_id = @id) AS placed_on;
            COMMIT;
            """, token);
        const string callerBatch = "SET NOCOUNT ON; SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN; EXEC dbo.obs_card 1; COMMIT;";

        await using var guard = await scratch.OpenAsync(token);
        await using var inserter = await scratch.OpenAsync(token);
        await using var watcher = await scratch.OpenAsync(token);
        var session = await ScratchDatabase.SessionOfAsync(guard, token);
        var blocker = await ScratchDatabase.SessionOfAsync(inserter, token);
        await using (var insert = new SqlCommand("BEGIN TRAN; INSERT INTO dbo.obs_order VALUES (1, '2026-11-16');", inserter))
            await insert.ExecuteNonQueryAsync(token);

#pragma warning disable CA2100 // Observation tests' own batches.
        await using var call = new SqlCommand(caller ? callerBatch : "EXEC dbo.obs_card 1;", guard);
#pragma warning restore CA2100
        var reading = call.ExecuteReaderAsync(token);
        await ScratchDatabase.BlockedByAsync(watcher, session, blocker, reading, token);
        await using (var commit = new SqlCommand("COMMIT;", inserter))
            await commit.ExecuteNonQueryAsync(token);

        await using var reader = await reading;
        if (!await reader.ReadAsync(token))
            return "returned";
        return await reader.IsDBNullAsync(0, token) ? "null" : reader.GetDateTime(0).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task A_callers_snapshot_transaction_that_has_read_nothing_still_misses_the_row_the_guard_waited_for()
    {
        // With the body's own snapshot transaction and the guard its first read, the read found the row: the guard's locking
        // read did not start the snapshot before it waited. With a caller's open snapshot transaction that had read nothing
        // before the call, the read missed it: the snapshot had begun before the insert committed. When it begins is not
        // something the rule relies on: a lock is never trusted for a read with no hint at snapshot isolation.
        await using var scratch = await server.ScratchAsync();
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", TestContext.Current.CancellationToken);

        var body = await CommittedWhileTheGuardWaits(scratch, caller: false);
        var caller = await CommittedWhileTheGuardWaits(scratch, caller: true);
        Assert.Equal("body 2026-11-16, caller null", $"body {body}, caller {caller}");
    }

    [Fact]
    public async Task A_lock_the_guard_holds_keeps_the_row_only_for_a_read_of_what_is_committed_now()
    {
        // The guard's lock waits for the insert and finds the row, but a read at snapshot isolation reads the transaction's
        // snapshot, which began before the insert, and finds nothing: whether the procedure began the snapshot transaction or
        // its caller did. A read with READCOMMITTEDLOCK, or after the level is set to read committed, reads what is committed
        // now, and the lock keeps the row there.
        await using var scratch = await server.ScratchAsync();
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", TestContext.Current.CancellationToken);
        const string readCommitted = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;";

        Assert.Equal("null", await InsertedAfterTheSnapshot(scratch, Locks));
        Assert.Equal("null", await InsertedAfterTheSnapshot(scratch, Locks, caller: true));
        Assert.Equal("2026-11-16", await InsertedAfterTheSnapshot(scratch, Locks, read: "WITH (READCOMMITTEDLOCK)"));
        Assert.Equal("2026-11-16", await InsertedAfterTheSnapshot(scratch, Locks, between: readCommitted));
        Assert.Equal("2026-11-16", await InsertedAfterTheSnapshot(scratch, Locks, between: readCommitted, caller: true));
    }

    private const string Locks = "WITH (UPDLOCK, HOLDLOCK)";

    [Theory]
    [InlineData("WITH (UPDLOCK)", "error 3960")]
    [InlineData("WITH (XLOCK)", "error 3960")]
    [InlineData("WITH (HOLDLOCK)", "2026-11-16")]
    [InlineData("WITH (SERIALIZABLE)", "2026-11-16")]
    [InlineData("WITH (REPEATABLEREAD)", "2026-11-16")]
    [InlineData("WITH (ROWLOCK)", "null")]
    [InlineData("WITH (TABLOCK)", "null")]
    [InlineData("WITH (PAGLOCK)", "null")]
    [InlineData("WITH (NOWAIT)", "null")]
    public async Task A_read_at_snapshot_isolation_reads_what_is_committed_now_only_with_a_hint_that_takes_a_lock(string read, string found)
    {
        // A read with a hint that takes a lock reads what is committed now, where the guard's lock keeps the row: one that
        // takes an update or exclusive lock, finding the row changed since the snapshot began, fails the call instead (3960,
        // an update conflict). Neither returns the snapshot's empty answer. A hint that only says how to lock, or not to wait,
        // takes no lock at snapshot isolation, and the read reads the snapshot.
        await using var scratch = await server.ScratchAsync();
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", TestContext.Current.CancellationToken);

        string outcome;
        try
        {
            outcome = await InsertedAfterTheSnapshot(scratch, Locks, read: read);
        }
        catch (SqlException e)
        {
            outcome = $"error {e.Number}";
        }

        Assert.Equal(found, outcome);
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

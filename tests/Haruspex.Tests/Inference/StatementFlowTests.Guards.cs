using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// What a guard proves after it: an <c>IF</c> whose one arm ends the path (<c>RETURN</c>, <c>THROW</c>, <c>RAISERROR</c> of
/// severity 11 or more) leaves the other arm, and the statements after it, knowing its condition was not TRUE. Each fact holds
/// until something can change it. Observed in <c>StatementFlowObservationTests.Guards.cs</c>.
/// </summary>
public partial class StatementFlowTests
{
    [Theory]
    [InlineData("IF @x IS NULL RETURN;")]
    [InlineData("IF @x IS NULL THROW 50000, N'no x', 1;")]
    [InlineData("IF @x IS NULL RAISERROR(N'no x', 16, 1);")]
    [InlineData("IF @x IS NULL BEGIN PRINT N'early'; RETURN; END")]
    [InlineData("IF @x IS NULL OR @x < 0 RETURN;")]
    [InlineData("IF NOT (@x IS NOT NULL) RETURN;")]
    [InlineData("IF @x IS NOT NULL PRINT N'go'; ELSE RETURN;")]
    public void A_variable_tested_null_before_a_path_that_ends_is_never_null_after_it(string guard)
    {
        // A_null_test_that_returns_or_throws_lets_only_a_value_past_it.
        var doc = Procedure($"{guard}\nSELECT @x AS x;", "@x int");

        Assert.Equal([], Rules(doc, "x"));
    }

    [Theory]
    [InlineData("IF @x IS NULL RAISERROR(N'no x', 10, 1);")]
    [InlineData("IF @x IS NULL PRINT N'no x';")]
    [InlineData("IF @x IS NULL AND @y = 1 RETURN;")]
    [InlineData("IF @x = 1 RETURN;")]
    [InlineData("IF @x IS NULL RAISERROR(N'no x', @y, 1);")]
    public void A_guard_that_may_let_a_null_past_proves_nothing(string guard)
    {
        // Severity 10 is a message, a branch that does not end lets the NULL through, and a condition that may be not TRUE
        // when @x is NULL proves nothing of it; a severity in a variable may be below 11.
        var doc = Procedure($"{guard}\nSELECT @x AS x;", "@x int, @y int");

        Assert.Equal([IrNullRule.Parameter], Rules(doc, "x"));
    }

    [Fact]
    public void A_guard_proves_nothing_of_another_variable_or_past_a_later_assignment()
    {
        // A_guard_proves_nothing_of_another_variable_or_past_a_later_assignment.
        var doc = Procedure("IF @x IS NULL RETURN;\nSELECT @y AS y;\nSET @x = @y;\nSELECT @x AS x;", "@x int, @y int");

        Assert.Equal([IrNullRule.Parameter], Rules(doc, "y"));
        Assert.Equal([IrNullRule.Parameter], Rules(Field(doc, "x", result: 1)));
    }

    [Fact]
    public void A_branch_knows_its_condition_held_and_the_other_that_it_did_not()
    {
        // Within each arm the condition's truth is known; where the arms meet, each variable is joined as before.
        var doc = Procedure("""
            DECLARE @inside int = 0, @outside int = 0;
            IF @x IS NOT NULL SET @inside = @x; ELSE SET @outside = @x;
            IF @y IS NULL SET @y = 0;
            SELECT @inside AS inside, @outside AS outside, @y AS y;
            """, "@x int, @y int");

        Assert.Equal([], Rules(doc, "inside"));
        Assert.Equal([IrNullRule.Parameter], Rules(doc, "outside"));
        Assert.Equal([], Rules(doc, "y"));
    }

    [Fact]
    public void A_throw_caught_in_the_same_procedure_proves_nothing_after_the_try()
    {
        // A_throw_caught_in_the_same_procedure_lets_the_null_past: CATCH starts from the state before the guard.
        var doc = Procedure("""
            DECLARE @inside int = 0;
            BEGIN TRY
                IF @x IS NULL THROW 50000, N'no x', 1;
                SET @inside = @x;
            END TRY
            BEGIN CATCH
            END CATCH
            SELECT @inside AS inside, @x AS x;
            """, "@x int");

        Assert.Equal([], Rules(doc, "inside"));
        Assert.Equal([IrNullRule.Parameter], Rules(doc, "x"));
    }

    [Fact]
    public void A_guard_inside_a_loop_holds_only_on_that_pass()
    {
        // A_guard_inside_a_loop_holds_only_on_that_pass: the value set after the guard comes round to the loop's head.
        var doc = Procedure("""
            DECLARE @n int = 0, @seen int, @head int;
            WHILE @n < 3
            BEGIN
                SET @head = @x;
                IF @x IS NULL BREAK;
                SET @seen = @x;
                SET @x = NULLIF(@n, 1);
                SET @n = @n + 1;
            END
            SELECT @x AS x, @head AS head, @seen AS seen;
            """, "@x int");

        Assert.Contains(IrNullRule.Parameter, Rules(doc, "x"));
        Assert.Contains(IrNullRule.NullWhenEqual, Rules(doc, "head"));
        Assert.DoesNotContain(IrNullRule.Parameter, Rules(doc, "seen"));
        Assert.DoesNotContain(IrNullRule.NullWhenEqual, Rules(doc, "seen"));
    }

    [Fact]
    public void A_parameter_set_to_coalesce_with_a_value_last_is_never_null_after_it()
    {
        // A_parameter_set_to_coalesce_with_a_value_last_is_never_null_after_it.
        var doc = Procedure("SET @p = COALESCE(@p, 0);\nSELECT @p AS p;", "@p int");

        Assert.Equal([], Rules(doc, "p"));
    }

    /// <summary>A select that may assign nothing, of a NOT NULL column into @n and a nullable one into @label.</summary>
    private const string MayAssign = """
        DECLARE @n int, @label nvarchar(10);
        SELECT @n = t.code, @label = t.name FROM dbo.t AS t WHERE t.id = @id;
        """;

    [Theory]
    [InlineData("IF @@ROWCOUNT = 0 RETURN;")]
    [InlineData("IF @@ROWCOUNT < 1 THROW 50000, N'none', 1;")]
    [InlineData("IF NOT (@@ROWCOUNT > 0) RETURN;")]
    [InlineData("IF 0 = @@ROWCOUNT RETURN;")]
    [InlineData("IF @@ROWCOUNT >= 1 PRINT N'found'; ELSE RETURN;")]
    public void Rowcount_tested_right_after_a_select_proves_it_assigned_every_variable(string guard)
    {
        // Rowcount_right_after_a_select_that_assigns_counts_its_rows: each variable then holds what the SELECT assigned,
        // which for @label may still be NULL, for its column's own reason.
        var doc = Procedure($"{MayAssign}{guard}\nSELECT @n AS n, @label AS label;", "@id int");

        Assert.Equal([], Rules(doc, "n"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "label"));
    }

    [Theory]
    [InlineData("DECLARE @z int = 1;\nIF @@ROWCOUNT = 0 RETURN;")]
    [InlineData("SET @id = 1;\nIF @@ROWCOUNT = 0 RETURN;")]
    [InlineData("IF @id = 1 PRINT N'one';\nIF @@ROWCOUNT = 0 RETURN;")]
    [InlineData("IF @@ROWCOUNT = 1 RETURN;")]
    public void Rowcount_tested_after_another_statement_or_for_another_count_proves_nothing(string guard)
    {
        // Any_statement_between_resets_rowcount_so_the_test_proves_nothing; and @@ROWCOUNT other than 1 may still be 0.
        var doc = Procedure($"{MayAssign}{guard}\nSELECT @n AS n;", "@id int");

        Assert.Contains(IrNullRule.Variable, Rules(doc, "n"));
    }

    [Theory]
    [InlineData("IF @label IS NULL RETURN;", new IrNullRule[0])]
    [InlineData("IF @n IS NULL RETURN;", new[] { IrNullRule.NullableColumn })]
    public void A_variable_null_before_a_select_and_not_after_it_proves_the_select_assigned_them_all(string guard, IrNullRule[] label)
    {
        // A_variable_null_before_a_select_and_not_after_it_proves_the_select_assigned_them_all.
        var doc = Procedure($"{MayAssign}{guard}\nSELECT @n AS n, @label AS label;", "@id int");

        Assert.Equal([], Rules(doc, "n"));
        Assert.Equal(label, Rules(doc, "label"));
    }

    [Fact]
    public void A_variable_that_may_have_held_a_value_before_the_select_proves_nothing_of_the_others()
    {
        // obs_held: @seen holds 0 when the SELECT matches nothing, so its value says nothing of @m.
        var doc = Procedure("""
            DECLARE @seen int = 0, @m int;
            SELECT @seen = t.code, @m = t.code FROM dbo.t AS t WHERE t.id = @id;
            IF @seen IS NULL RETURN;
            SELECT @m AS m;
            """, "@id int");

        Assert.Equal([IrNullRule.Variable], Rules(doc, "m"));
    }

    [Fact]
    public void An_assignment_after_the_select_ends_what_its_variables_prove_together()
    {
        // @n set to NULL after the SELECT: @label's value proves the SELECT assigned, but no longer what @n holds.
        var doc = Procedure($"{MayAssign}SET @n = NULL;\nIF @label IS NULL RETURN;\nSELECT @n AS n;", "@id int");

        Assert.NotEmpty(Rules(doc, "n"));
    }

    [Fact]
    public void A_select_in_a_loop_proves_nothing_by_a_variable_an_earlier_pass_assigned()
    {
        // On the second pass @label already holds the first pass's value, so its value proves nothing of the second SELECT.
        var doc = Procedure("""
            DECLARE @n int, @label nvarchar(10), @i int = 0;
            WHILE @i < 2
            BEGIN
                IF @i = 0 SET @label = N'first';
                ELSE SELECT @n = t.code, @label = t.name FROM dbo.t AS t WHERE t.id = @i;
                SET @i = @i + 1;
            END
            IF @label IS NULL RETURN;
            SELECT @n AS n;
            """);

        Assert.Contains(IrNullRule.Variable, Rules(doc, "n"));
    }

    /// <summary>
    /// A transaction the body begins at a level it sets that is not <c>SNAPSHOT</c>, so that a lock the guard holds keeps the
    /// row for a read with no hint: where the body sets none, the caller's may be <c>SNAPSHOT</c>, and nothing is claimed
    /// (<see cref="A_lock_the_guard_holds_where_the_body_sets_no_level_keeps_the_row_only_for_a_read_with_a_hint_of_its_own"/>).
    /// </summary>
    private const string Begun = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED; BEGIN TRAN;";

    /// <summary>
    /// A procedure whose existence guard on <c>dbo.t</c> runs after <paramref name="before"/> with <paramref name="hint"/>, then
    /// <paramref name="between"/>, then two keyed reads of the row: a scalar subquery of a NOT NULL column, and a FOR JSON one.
    /// </summary>
    private static IrDocument Guarded(string before, string hint, string between = "", string read = "", string guard = "") =>
        Procedure($"""
            {before}
            {(guard.Length > 0 ? guard : $"IF NOT EXISTS (SELECT 1 FROM dbo.t AS t {hint} WHERE t.id = @id) RETURN;")}
            {between}
            SELECT (SELECT t.code FROM dbo.t AS t {read} WHERE t.id = @id) AS code,
                (SELECT t.id, t.name FROM dbo.t AS t {read} WHERE t.id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS card;
            WHILE @@TRANCOUNT > 0 COMMIT;
            """, "@id int, @other int");

    [Theory]
    [InlineData("", "")]
    [InlineData("", "WITH (UPDLOCK, HOLDLOCK)")]
    [InlineData("BEGIN TRAN;", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT;", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ COMMITTED; BEGIN TRAN;", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN;", "WITH (READCOMMITTED)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN;", "WITH (NOLOCK)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ; BEGIN TRAN;", "WITH (NOLOCK)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "WITH (READCOMMITTEDLOCK)")]
    public void A_read_of_a_row_an_existence_guard_found_states_the_race_where_nothing_holds_the_row(string before, string hint)
    {
        // A_row_an_existence_guard_saw_may_be_gone_when_a_later_statement_reads_it: another session may delete the row between
        // the guard and the read, so the read may find none, and says why at the guard (read-twice), as COALESCE's second read.
        // A hint on the guard that lowers the level gives up its lock
        // (A_hint_on_the_guard_that_lowers_the_level_holds_no_lock_for_the_read), and one that reads past the snapshot may see
        // a row the snapshot read doesn't (A_guard_that_reads_past_the_snapshot_can_see_a_row_the_snapshot_read_does_not).
        var doc = Guarded(before, hint);

        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "code"));
        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "card"));
        Assert.Contains(":3:", Field(doc, "card").NullableBecause[0].At, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Begun, "WITH (UPDLOCK, HOLDLOCK)", "")]
    [InlineData(Begun, "WITH (HOLDLOCK)", "")]
    [InlineData(Begun, "WITH (SERIALIZABLE)", "")]
    [InlineData(Begun, "WITH (UPDLOCK)", "")]
    [InlineData(Begun, "WITH (REPEATABLEREAD)", "")]
    [InlineData(Begun, "WITH (XLOCK)", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN;", "", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ; BEGIN TRAN;", "", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ; BEGIN TRAN;", "WITH (READPAST)", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN;", "WITH (UPDLOCK)", "")]
    [InlineData(Begun + " BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "COMMIT;")]
    [InlineData(Begun, "WITH (UPDLOCK, HOLDLOCK)", "INSERT INTO #seen (id) VALUES (@id);")]
    public void A_keyed_read_of_a_row_an_open_transaction_holds_for_it_finds_it(string before, string hint, string between)
    {
        // A_lock_the_guard_holds_to_the_end_of_an_open_transaction_keeps_the_row_for_the_read,
        // Snapshot_isolation_in_an_open_transaction_reads_the_row_the_guard_saw: the read finds exactly the guard's row. A write
        // to a temporary table fires no trigger and changes no permanent row.
        var doc = Guarded($"CREATE TABLE #seen (id int NULL); {before}", hint, between);

        Assert.Equal([], Rules(doc, "code"));
        Assert.Equal([], Rules(doc, "card"));
    }

    [Theory]
    [InlineData(Begun, "WITH (UPDLOCK, HOLDLOCK)", "COMMIT;", "", IrNullRule.ReadTwice)]
    [InlineData(Begun, "WITH (UPDLOCK, HOLDLOCK)", "IF @other = 1 COMMIT;", "", IrNullRule.ReadTwice)]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "", "", "WITH (READCOMMITTEDLOCK)", IrNullRule.ReadTwice)]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "", "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;", "", IrNullRule.ReadTwice)]
    [InlineData(Begun, "WITH (UPDLOCK, HOLDLOCK)", "ROLLBACK;", "", IrNullRule.EmptySubquery)]
    public void A_hold_ends_with_its_transaction_or_at_a_read_the_snapshot_does_not_cover(string before, string hint, string between, string read, IrNullRule rule)
    {
        // Committed (on some path) before the read, or a snapshot read at another level or with a hint: the race is back. A
        // rollback may undo what made the row, so it ends the fact altogether.
        var doc = Guarded(before, hint, between, read);

        Assert.Equal([rule], Rules(doc, "code"));
    }

    [Theory]
    [InlineData("WITH (UPDLOCK, HOLDLOCK)")]
    [InlineData("WITH (UPDLOCK)")]
    [InlineData("WITH (XLOCK)")]
    public void A_lock_the_guard_holds_at_snapshot_isolation_leaves_a_read_of_the_snapshot_raced(string hint)
    {
        // A_lock_the_guard_holds_keeps_the_row_only_for_a_read_of_what_is_committed_now: the guard's lock finds the row as
        // committed now, and a read at snapshot isolation with no hint reads the transaction's snapshot, which may be older.
        var doc = Guarded("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", hint);

        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "code"));
        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "card"));
    }

    [Theory]
    [InlineData("", "WITH (READCOMMITTEDLOCK)")]
    [InlineData("", "WITH (HOLDLOCK)")]
    [InlineData("", "WITH (SERIALIZABLE)")]
    [InlineData("", "WITH (REPEATABLEREAD)")]
    [InlineData("", "WITH (UPDLOCK)")]
    [InlineData("", "WITH (XLOCK)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;", "")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;", "")]
    public void A_lock_the_guard_holds_at_snapshot_isolation_keeps_the_row_for_a_read_of_what_is_committed_now(string between, string read)
    {
        // A_lock_the_guard_holds_keeps_the_row_only_for_a_read_of_what_is_committed_now: a read with READCOMMITTEDLOCK, or after
        // the body sets another level, reads what is committed now, where the guard's lock keeps the row.
        // A_read_at_snapshot_isolation_that_takes_a_lock_reads_what_is_committed_now: so does one with a hint that takes a
        // lock, or it fails the call (3960), which returns nothing at all.
        var doc = Guarded("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", between, read);

        Assert.Equal([], Rules(doc, "code"));
        Assert.Equal([], Rules(doc, "card"));
    }

    [Theory]
    [InlineData("", IrNullRule.ReadTwice)]
    [InlineData("WITH (READCOMMITTEDLOCK)", null)]
    [InlineData("WITH (UPDLOCK)", null)]
    public void A_lock_the_guard_holds_where_the_body_sets_no_level_keeps_the_row_only_for_a_read_with_a_hint_of_its_own(string read, IrNullRule? rule)
    {
        // A_lock_the_guard_holds_keeps_the_row_only_for_a_read_of_what_is_committed_now: a caller in a snapshot transaction
        // that has already read makes a read with no hint in the body read its snapshot, and the caller's level is not seen.
        // Claiming nothing there is provisional: the alternative is to assume the caller isn't in one (decision 0016).
        var doc = Guarded("BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", read: read);

        Assert.Equal(rule is { } r ? [r] : [], Rules(doc, "code"));
        Assert.Equal(rule is { } c ? [c] : [], Rules(doc, "card"));
    }

    [Fact]
    public void A_catch_may_follow_an_error_that_rolled_the_transaction_back_so_its_reads_are_raced()
    {
        // Whether every error that reaches CATCH leaves the transaction and the guard's lock in place is not observed, so a
        // read in CATCH is raced; one after the TRY is joined with it.
        var doc = Procedure("""
            DECLARE @code int = 0;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            BEGIN TRAN;
            IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id) RETURN;
            BEGIN TRY
                SET @code = @id / @other;
            END TRY
            BEGIN CATCH
                SET @code = (SELECT t.code FROM dbo.t AS t WHERE t.id = @id);
            END CATCH
            SELECT @code AS code;
            WHILE @@TRANCOUNT > 0 COMMIT;
            """, "@id int, @other int");

        Assert.Contains(IrNullRule.ReadTwice, Rules(doc, "code"));
    }

    [Theory]
    [InlineData("DELETE FROM dbo.u WHERE u.id = @other;")]
    [InlineData("UPDATE dbo.t SET code = 0 WHERE id = @other;")]
    [InlineData("EXEC dbo.p2;")]
    [InlineData("SET @id = @other;")]
    public void A_write_a_call_or_an_assignment_after_the_guard_ends_what_it_found(string between)
    {
        // The body's own write may remove the row, and a call may write anything: not a race, so plain empty-subquery.
        var doc = Infer(
            ("tables.sql", Tables),
            ("p2.sql", "CREATE PROCEDURE dbo.p2 AS SET NOCOUNT ON;"),
            ("p.sql", $"""
                CREATE PROCEDURE dbo.p @id int, @other int AS
                SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
                BEGIN TRAN;
                IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id) RETURN;
                {between}
                SELECT (SELECT t.code FROM dbo.t AS t WHERE t.id = @id) AS code;
                COMMIT;
                """));

        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "code"));
    }

    [Theory]
    [InlineData("IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @other) RETURN;")]
    [InlineData("IF NOT EXISTS (SELECT 1 FROM dbo.u AS u WITH (UPDLOCK, HOLDLOCK) WHERE u.id = @id) RETURN;")]
    [InlineData("IF NOT EXISTS (SELECT COUNT(*) FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id) RETURN;")]
    [InlineData("IF EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id) RETURN;")]
    [InlineData("IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id OR t.code = 0) RETURN;")]
    public void A_guard_that_proves_no_row_of_the_read_proves_nothing_of_it(string guard)
    {
        // Another key, another table, an aggregate (its one row is there whatever matches), the wrong arm, and an OR.
        var doc = Guarded(Begun, "", guard: guard);

        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "code"));
    }

    [Fact]
    public void A_guard_with_more_filters_than_the_read_proves_its_row_and_a_read_with_more_proves_nothing()
    {
        // The guard's row passes every one of the read's conditions only when each is one of the guard's.
        var stricter = Guarded(Begun, "", guard: "IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK) WHERE t.id = @id AND t.status = 'open') RETURN;");

        Assert.Equal([], Rules(stricter, "code"));

        var filtered = Procedure("""
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            BEGIN TRAN;
            IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK) WHERE t.id = @id) RETURN;
            SELECT (SELECT t.code FROM dbo.t AS t WHERE t.id = @id AND t.status = 'open') AS code;
            COMMIT;
            """, "@id int");
        Assert.Equal([IrNullRule.EmptySubquery], Rules(filtered, "code"));
    }

    [Fact]
    public void A_held_row_makes_a_keyed_result_and_a_select_that_assigns_from_it_one_row()
    {
        // The same claim reaches every keyed read: a result set of exactly one row, and an assignment that always happens.
        var doc = Procedure("""
            DECLARE @code int;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            BEGIN TRAN;
            IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id) RETURN;
            SELECT @code = t.code FROM dbo.t AS t WHERE t.id = @id;
            SELECT t.name FROM dbo.t AS t WHERE t.id = @id;
            SELECT @code AS code;
            COMMIT;
            """, "@id int");

        Assert.Equal(IrCardinality.One, Results(doc)[0].Cardinality);
        Assert.Equal([], Rules(Field(doc, "code", result: 1)));
    }

    // ---- where paths meet: each fact a guard relies on, held on one arm of an IF only ------------------------------------

    [Fact]
    public void A_select_that_may_assign_nothing_on_one_arm_only_proves_nothing_after_the_if()
    {
        // On the ELSE arm @w is set and the SELECT never ran, so @w not NULL after the IF proves nothing of @a: what the SELECT
        // may have assigned holds where paths meet only if both hold it.
        var doc = Procedure("""
            DECLARE @a nvarchar(10), @w int;
            IF @f = 1 SELECT @a = t.name, @w = t.id FROM dbo.t AS t WHERE t.id = @id; ELSE SET @w = 1;
            IF @w IS NULL RETURN;
            SELECT @a AS a;
            """, "@id int, @f int");

        Assert.Contains(IrNullRule.Variable, Rules(doc, "a"));
    }

    [Fact]
    public void A_variable_assigned_on_one_arm_only_is_not_a_witness_after_the_if()
    {
        // @w may hold 1 from the first IF, so @w not NULL after the SELECT doesn't prove the SELECT read a row: a variable is
        // certainly NULL where paths meet only if it is on both.
        var doc = Procedure("""
            DECLARE @a nvarchar(10), @w int;
            IF @f = 1 SET @w = 1;
            SELECT @a = t.name, @w = t.id FROM dbo.t AS t WHERE t.id = @id;
            IF @w IS NULL RETURN;
            SELECT @a AS a;
            """, "@id int, @f int");

        Assert.Contains(IrNullRule.Variable, Rules(doc, "a"));
    }

    [Fact]
    public void A_row_a_guard_found_on_one_arm_only_is_not_found_after_the_if()
    {
        // The guard runs only when @f = 1; on the other path nothing tested the row, so the read may find none for a reason
        // that is no race.
        var doc = Procedure("""
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            BEGIN TRAN;
            IF @f = 1
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id) RETURN;
            END
            SELECT (SELECT t.code FROM dbo.t AS t WHERE t.id = @id) AS code;
            COMMIT;
            """, "@id int, @f int");

        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "code"));
    }

    [Fact]
    public void A_transaction_begun_on_one_arm_only_holds_no_row_after_the_if()
    {
        // Without the transaction the guard's lock ends with its statement, so where paths meet the shallower depth counts.
        var doc = Procedure("""
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            IF @f = 1 BEGIN TRAN;
            IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK, HOLDLOCK) WHERE t.id = @id) RETURN;
            SELECT (SELECT t.code FROM dbo.t AS t WHERE t.id = @id) AS code;
            WHILE @@TRANCOUNT > 0 COMMIT;
            """, "@id int, @f int");

        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "code"));
    }
}

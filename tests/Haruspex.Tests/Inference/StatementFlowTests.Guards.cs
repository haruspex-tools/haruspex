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
    public void A_read_of_a_row_an_existence_guard_found_states_the_race_where_nothing_holds_the_row(string before, string hint)
    {
        // A_row_an_existence_guard_saw_may_be_gone_when_a_later_statement_reads_it: another session may delete the row between
        // the guard and the read, so the read may find none, and says why at the guard (read-twice), as COALESCE's second read.
        var doc = Guarded(before, hint);

        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "code"));
        Assert.Equal([IrNullRule.ReadTwice], Rules(doc, "card"));
        Assert.Contains(":3:", Field(doc, "card").NullableBecause[0].At, StringComparison.Ordinal);
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
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "", "")]
    [InlineData("BEGIN TRAN; BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "COMMIT;")]
    [InlineData("BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "INSERT INTO #seen (id) VALUES (@id);")]
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
    [InlineData("BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "COMMIT;", "", IrNullRule.ReadTwice)]
    [InlineData("BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "IF @other = 1 COMMIT;", "", IrNullRule.ReadTwice)]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "", "", "WITH (READCOMMITTEDLOCK)", IrNullRule.ReadTwice)]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;", "", "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;", "", IrNullRule.ReadTwice)]
    [InlineData("BEGIN TRAN;", "WITH (UPDLOCK, HOLDLOCK)", "ROLLBACK;", "", IrNullRule.EmptySubquery)]
    public void A_hold_ends_with_its_transaction_or_at_a_read_the_snapshot_does_not_cover(string before, string hint, string between, string read, IrNullRule rule)
    {
        // Committed (on some path) before the read, or a snapshot read at another level or with a hint: the race is back. A
        // rollback may undo what made the row, so it ends the fact altogether.
        var doc = Guarded(before, hint, between, read);

        Assert.Equal([rule], Rules(doc, "code"));
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
        var doc = Guarded("BEGIN TRAN;", "", guard: guard);

        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "code"));
    }

    [Fact]
    public void A_guard_with_more_filters_than_the_read_proves_its_row_and_a_read_with_more_proves_nothing()
    {
        // The guard's row passes every one of the read's conditions only when each is one of the guard's.
        var stricter = Guarded("BEGIN TRAN;", "", guard: "IF NOT EXISTS (SELECT 1 FROM dbo.t AS t WITH (UPDLOCK) WHERE t.id = @id AND t.status = 'open') RETURN;");

        Assert.Equal([], Rules(stricter, "code"));

        var filtered = Procedure("""
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
}

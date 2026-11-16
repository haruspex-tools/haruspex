namespace Haruspex.DifferentialTests;

public partial class StatementFlowObservationTests
{
    // ---- what a guard proves after it -------------------------------------------------------------------------------

    [Fact]
    public async Task A_null_test_that_returns_or_throws_lets_only_a_value_past_it()
    {
        // Each guard ends the call's path when @x is NULL: RETURN leaves without the result after it, THROW and RAISERROR of
        // severity 11 or more fail the call. A call that succeeds and reaches the SELECT has a value.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_guard @x int, @how int AS
            IF @how = 1 AND @x IS NULL RETURN;
            IF @how = 2 AND @x IS NULL THROW 50000, N'no x', 1;
            IF @how = 3 AND @x IS NULL RAISERROR(N'no x', 16, 1);
            IF @how = 4 AND @x IS NULL BEGIN SELECT 0 AS early; RETURN; END
            IF @how = 5 AND @x IS NULL RAISERROR(N'no x', 10, 1);
            SELECT @x AS x;
            """];

        Assert.Equal("", (await Run(setup, "EXEC dbo.obs_guard NULL, 1")).ToString());
        Assert.Equal(" ! 50000", (await Run(setup, "EXEC dbo.obs_guard NULL, 2")).ToString());
        Assert.Equal(" ! 50000", (await Run(setup, "EXEC dbo.obs_guard NULL, 3")).ToString());
        Assert.Equal("early: 0", (await Run(setup, "EXEC dbo.obs_guard NULL, 4")).ToString());
        for (var how = 1; how <= 4; how++)
            Assert.Equal("x: 7", (await Run(setup, $"EXEC dbo.obs_guard 7, {how}")).ToString());

        // RAISERROR of severity 11 or more carries on, but the call fails, so the NULL that reaches the SELECT is in a call that
        // does not succeed. Severity 10 is a message: the call succeeds with a NULL.
        Assert.Equal("x: null ~ 50000", (await Run(setup, "EXEC dbo.obs_guard NULL, 3", errorsAsMessages: true)).ToString());
        Assert.Equal("x: null", (await Run(setup, "EXEC dbo.obs_guard NULL, 5")).ToString());
    }

    [Fact]
    public async Task A_guard_proves_nothing_of_another_variable_or_past_a_later_assignment()
    {
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_guard_other @x int, @y int AS
            IF @x IS NULL RETURN;
            SELECT @y AS y;
            SET @x = @y;
            SELECT @x AS x;
            """];

        Assert.Equal("y: null | x: null", (await Run(setup, "EXEC dbo.obs_guard_other 1, NULL")).ToString());
    }

    [Fact]
    public async Task A_throw_caught_in_the_same_procedure_lets_the_null_past()
    {
        // The guard's THROW reaches the CATCH, which swallows it, and the SELECT after the TRY reads the NULL in a call that
        // succeeds.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_guard_caught @x int AS
            BEGIN TRY
                IF @x IS NULL THROW 50000, N'no x', 1;
                IF @x = 0 RAISERROR(N'zero', 16, 1);
            END TRY
            BEGIN CATCH
            END CATCH
            SELECT @x AS x;
            """];

        Assert.Equal("x: null", (await Run(setup, "EXEC dbo.obs_guard_caught NULL")).ToString());
        Assert.Equal("x: 0", (await Run(setup, "EXEC dbo.obs_guard_caught 0")).ToString());
    }

    [Fact]
    public async Task A_guard_inside_a_loop_holds_only_on_that_pass()
    {
        // The guard leaves the loop by BREAK, not the procedure, so the NULL reaches the SELECT after the loop. Inside the
        // body, the value assigned after the guard is what the next pass starts from, and the guard tests it again.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_guard_loop @x int AS
            DECLARE @n int = 0, @seen int;
            WHILE @n < 3
            BEGIN
                IF @x IS NULL BREAK;
                SET @seen = @x;
                SET @x = NULLIF(@n, 1);
                SET @n = @n + 1;
            END
            SELECT @x AS x, @seen AS seen, @n AS n;
            """];

        Assert.Equal("x,seen,n: null,null,0", (await Run(setup, "EXEC dbo.obs_guard_loop NULL")).ToString());
        Assert.Equal("x,seen,n: null,0,2", (await Run(setup, "EXEC dbo.obs_guard_loop 5")).ToString());
    }

    [Fact]
    public async Task A_parameter_set_to_coalesce_with_a_value_last_is_never_null_after_it()
    {
        string[] setup = ["CREATE PROCEDURE dbo.obs_coalesce @p int AS SET @p = COALESCE(@p, 0); SELECT @p AS p;"];

        Assert.Equal("p: 0", (await Run(setup, "EXEC dbo.obs_coalesce NULL")).ToString());
        Assert.Equal("p: 4", (await Run(setup, "EXEC dbo.obs_coalesce 4")).ToString());
    }

    /// <summary>A table with one row whose label is NULL, for the assignments below.</summary>
    private const string Labelled = """
        CREATE TABLE dbo.obs_item (id int NOT NULL PRIMARY KEY, n int NOT NULL, label nvarchar(10) NULL);
        INSERT INTO dbo.obs_item VALUES (1, 10, N'ten'), (2, 20, NULL);
        """;

    [Fact]
    public async Task Rowcount_right_after_a_select_that_assigns_counts_its_rows()
    {
        // A SELECT that matches nothing assigns nothing and leaves @@ROWCOUNT 0; one that matches assigns every variable it
        // lists. So IF @@ROWCOUNT = 0 RETURN straight after it lets past only a call in which every variable was assigned.
        string[] setup = [Labelled, """
            CREATE PROCEDURE dbo.obs_rowcount @id int AS
            DECLARE @n int, @label nvarchar(10);
            SELECT @n = i.n, @label = i.label FROM dbo.obs_item AS i WHERE i.id = @id;
            IF @@ROWCOUNT = 0 RETURN;
            SELECT @n AS n, @label AS label;
            """];

        Assert.Equal("", (await Run(setup, "EXEC dbo.obs_rowcount 9")).ToString());
        Assert.Equal("n,label: 10,ten", (await Run(setup, "EXEC dbo.obs_rowcount 1")).ToString());
        Assert.Equal("n,label: 20,null", (await Run(setup, "EXEC dbo.obs_rowcount 2")).ToString());
    }

    [Fact]
    public async Task Any_statement_between_resets_rowcount_so_the_test_proves_nothing()
    {
        string[] setup = [Labelled, """
            CREATE PROCEDURE dbo.obs_rowcount_reset @id int, @how int AS
            DECLARE @n int, @z int;
            SELECT @n = i.n FROM dbo.obs_item AS i WHERE i.id = @id;
            IF @how = 1 SET @z = 1;
            ELSE IF @how = 2 BEGIN DECLARE @y int = 1; END
            IF @@ROWCOUNT = 0 RETURN;
            SELECT @n AS n, @@ROWCOUNT AS rows_;
            """];

        // The SELECT reads nothing, yet a SET or a DECLARE with a value between leaves @@ROWCOUNT 1: the call gets past the
        // guard with @n NULL. (The guard's own IF then leaves it 0 for the last SELECT.)
        Assert.Equal("n,rows_: null,0", (await Run(setup, "EXEC dbo.obs_rowcount_reset 9, 1")).ToString());
        Assert.Equal("n,rows_: null,0", (await Run(setup, "EXEC dbo.obs_rowcount_reset 9, 2")).ToString());

        // The SELECT reads a row, yet an IF's tests between, both FALSE, leave @@ROWCOUNT 0: the guard returns. So the count
        // a guard reads is the SELECT's only straight after it.
        Assert.Equal("", (await Run(setup, "EXEC dbo.obs_rowcount_reset 1, 3")).ToString());
    }

    [Fact]
    public async Task A_variable_null_before_a_select_and_not_after_it_proves_the_select_assigned_them_all()
    {
        // @label is NULL until the SELECT, so a value in it proves the SELECT read a row, and with it assigned @n. A variable
        // that held a value before proves nothing: here @seen keeps 0 when the SELECT matches nothing.
        string[] setup = [Labelled, """
            CREATE PROCEDURE dbo.obs_together @id int AS
            DECLARE @n int, @label nvarchar(10), @seen int = 0, @m int;
            SELECT @n = i.n, @label = i.label FROM dbo.obs_item AS i WHERE i.id = @id;
            SELECT @seen = i.n, @m = i.n FROM dbo.obs_item AS i WHERE i.id = @id;
            IF @label IS NULL RETURN;
            SELECT @n AS n, @label AS label, @seen AS seen, @m AS m;
            """];

        Assert.Equal("n,label,seen,m: 10,ten,10,10", (await Run(setup, "EXEC dbo.obs_together 1")).ToString());
        Assert.Equal("", (await Run(setup, "EXEC dbo.obs_together 9")).ToString());

        // The row with a NULL label was read, but the guard cannot tell: it returns, which only claims less.
        Assert.Equal("", (await Run(setup, "EXEC dbo.obs_together 2")).ToString());

        string[] held = [Labelled, """
            CREATE PROCEDURE dbo.obs_held @id int AS
            DECLARE @seen int = 0, @m int;
            SELECT @seen = i.n, @m = i.n FROM dbo.obs_item AS i WHERE i.id = @id;
            IF @seen IS NULL RETURN;
            SELECT @seen AS seen, @m AS m;
            """];
        Assert.Equal("seen,m: 0,null", (await Run(held, "EXEC dbo.obs_held 9")).ToString());
    }
}

using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Inference.Lattice;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The one iteration every solver uses (<see cref="FixedPoint.Iterate"/>): it stops at the first pass that gives back what it
/// started from, compared as records; it applies a solver's widening from <see cref="FixedPoint.WidenAfter"/> on; it gives up
/// at <see cref="FixedPoint.MostPasses"/>; and a step can stop it. The guide's chapter
/// <c>docs/internals/06-flow-joins-and-fixed-points.md</c> explains the policy.
/// </summary>
public class FixedPointTests
{
    private static readonly SourceLocation At = new("p.sql", 3, 1);

    [Fact]
    public void An_iteration_settles_at_the_first_pass_that_gives_back_its_state()
    {
        var passes = 0;
        var iteration = FixedPoint.Iterate(0, n =>
        {
            passes++;
            return Pass<int>.To(Math.Min(n + 1, 3));
        });

        Assert.Equal(new Iteration<int>(IterationEnd.Settled, 3), iteration);
        Assert.Equal(4, passes);
    }

    [Fact]
    public void A_state_that_says_the_same_in_another_order_settles_in_two_passes_when_it_is_joined_on_the_left()
    {
        // The state is kept on the left of the join, so a pass that gives its reasons in another order adds nothing, and the
        // record comes back the same: the iteration stops on structural equality without reaching the cap.
        var number = ScalarShape.FromIr(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"));
        IrNullReason first = new(IrNullRule.Variable, "@a"), second = new(IrNullRule.NullableColumn, "dbo.t.b");
        var start = Value.Computed(number, [first]);
        var passes = 0;

        var iteration = FixedPoint.Iterate(start, head =>
        {
            passes++;
            return Pass<Value>.To(Value.Join(head, Value.Computed(number, [second, first]), At, "@v"));
        });

        Assert.Equal(IterationEnd.Settled, iteration.End);
        Assert.Equal(2, passes);
        Assert.Equal([first, second], iteration.Last.NullableBecause);
    }

    [Fact]
    public void A_widening_applies_from_its_pass_on_and_lets_a_growing_state_settle()
    {
        var widened = new List<int>();
        var iteration = FixedPoint.Iterate(
            0,
            n => Pass<int>.To(n + 1),
            widen: (current, next) =>
            {
                widened.Add(next);
                return 100;
            });

        Assert.Equal(new Iteration<int>(IterationEnd.Settled, 100), iteration);
        Assert.Equal(FixedPoint.WidenAfter + 1, widened[0]);
    }

    [Fact]
    public void An_iteration_that_never_settles_is_given_up_at_the_cap()
    {
        var passes = 0;
        var iteration = FixedPoint.Iterate(0, n =>
        {
            passes++;
            return Pass<int>.To(n + 1);
        });

        Assert.Equal(new Iteration<int>(IterationEnd.Unsettled, FixedPoint.MostPasses), iteration);
        Assert.Equal(FixedPoint.MostPasses, passes);
    }

    [Fact]
    public void A_step_can_stop_the_iteration()
    {
        var iteration = FixedPoint.Iterate(0, n => n == 2 ? Pass<int>.Stop() : Pass<int>.To(n + 1));

        Assert.Equal(new Iteration<int>(IterationEnd.Stopped, 2), iteration);
    }

    // ---- each solver settles by value ---------------------------------------------------------------------------------
    //
    // Every pass of a solver builds its state anew: new values, new tables, new lists, new maps. A pass that adds nothing
    // gives a state equal to the one before but never the same object, so each solver below settles only because its state
    // compares by value (decision 0022). Were any part of it compared by reference, the pass would never repeat, the
    // iteration would reach the cap, and the solver would say unknown: each test then fails.

    private const string Tables = """
        CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, parent_id int NULL, name nvarchar(20) NOT NULL);
        GO
        """;

    [Fact]
    public void A_loop_whose_passes_rebuild_its_tables_and_variables_settles()
    {
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @i int = 0, @label nvarchar(20) = N'start', @json nvarchar(max) = N'[]';
            CREATE TABLE #seen (id int NOT NULL);
            WHILE @i < 3
            BEGIN
                SET @label = N'pass';
                SET @json = (SELECT i.id FROM dbo.item AS i FOR JSON PATH);
                INSERT INTO #seen (id) SELECT i.id FROM dbo.item AS i;
                SET @i = @i + 1;
            END
            SELECT @label AS label, s.id FROM #seen AS s;
            """));

        Assert.Empty(doc.Unknowns);
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(IrNavigation.Field(doc, "label").Type).Kind);
        Assert.False(IrNavigation.Field(doc, "id").Nullable);
    }

    [Fact]
    public void A_recursive_cte_whose_steps_rebuild_its_values_settles()
    {
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", """
            CREATE PROCEDURE dbo.p AS
            WITH tree AS (
                SELECT i.id, i.name, 0 AS depth FROM dbo.item AS i WHERE i.parent_id IS NULL
                UNION ALL
                SELECT c.id, c.name, t.depth + 1 FROM dbo.item AS c JOIN tree AS t ON c.parent_id = t.id
            )
            SELECT t.id, t.name, t.depth FROM tree AS t;
            """));

        Assert.Empty(doc.Unknowns);
        Assert.False(IrNavigation.Field(doc, "name").Nullable);
    }

    [Fact]
    public void A_procedure_that_calls_itself_settles_when_a_walk_returns_what_was_assumed()
    {
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", """
            CREATE PROCEDURE dbo.p @id int AS
            DECLARE @parent int = (SELECT i.parent_id FROM dbo.item AS i WHERE i.id = @id);
            IF @parent IS NOT NULL
            BEGIN
                EXEC dbo.p @id = @parent;
                RETURN;
            END
            SELECT i.id, i.name FROM dbo.item AS i WHERE i.id = @id;
            """));

        Assert.Empty(doc.Unknowns);
        Assert.Single(Assert.Single(doc.Objects).Results ?? []);
    }

    [Fact]
    public void Functions_that_call_themselves_settle_when_a_step_gives_back_their_results()
    {
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("f.sql", """
            CREATE FUNCTION dbo.subtree(@id int) RETURNS nvarchar(max) AS
            BEGIN
                RETURN (SELECT c.id, c.name, json_query(dbo.subtree(c.id)) AS children FROM dbo.item AS c WHERE c.parent_id = @id FOR JSON PATH);
            END
            """), ("p.sql", """
            CREATE PROCEDURE dbo.p AS
            SELECT i.id, json_query(dbo.subtree(i.id)) AS children FROM dbo.item AS i WHERE i.parent_id IS NULL FOR JSON PATH;
            """));

        Assert.Empty(doc.Unknowns);
        Assert.Contains(doc.Types.Values, t => t.Recursive);
    }
}

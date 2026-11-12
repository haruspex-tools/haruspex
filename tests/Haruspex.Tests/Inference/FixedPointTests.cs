using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Inference.Lattice;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The one iteration every solver uses (<see cref="FixedPoint.Iterate"/>): it stops at the first pass that gives back what it
/// started from, compared as records; it applies a solver's widening from <see cref="FixedPoint.WidenAfter"/> on; it gives up
/// at <see cref="FixedPoint.MostPasses"/>; and a step can stop it. The guide's chapter
/// <c>docs/internals/07-summaries-and-recursion.md</c> explains the policy.
/// </summary>
public class FixedPointTests
{
    private static readonly SourceLocation At = new("p.sql", 3, 1);

    [Fact]
    public void An_iteration_settles_at_the_first_pass_that_gives_back_its_state()
    {
        var passes = 0;
        var iteration = FixedPoint.Iterate(0, (n, _) =>
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
        var number = new ScalarShape(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"));
        IrNullReason first = new(IrNullRule.Variable, "@a"), second = new(IrNullRule.NullableColumn, "dbo.t.b");
        var start = Value.Computed(number, [first]);
        var passes = 0;

        var iteration = FixedPoint.Iterate(start, (head, _) =>
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
            (n, pass) => Pass<int>.To(n + 1),
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
        var iteration = FixedPoint.Iterate(0, (n, _) =>
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
        var iteration = FixedPoint.Iterate(0, (n, _) => n == 2 ? Pass<int>.Stop() : Pass<int>.To(n + 1));

        Assert.Equal(new Iteration<int>(IterationEnd.Stopped, 2), iteration);
    }
}

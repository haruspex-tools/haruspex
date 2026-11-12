namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>
/// The one Kleene iteration every solver in the inference uses: a <c>WHILE</c> loop's state, a recursive CTE's columns, a
/// recursive procedure's result sets and a component of recursive functions are each the least fixed point of a step, reached
/// by applying the step from a start below it until a pass gives back what it started from. The guide's chapter
/// <c>docs/internals/06-flow-joins-and-fixed-points.md</c> explains the idea and the policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convergence</b> is structural equality (<see cref="EqualityComparer{T}.Default"/>, the records' own), which is finer
/// than "says the same": it may take a pass more than needed, never stop one early (decision 0022). Each step keeps the
/// state it builds on on the left of its joins, so a pass that adds nothing gives the same record back.
/// </para>
/// <para>
/// <b>The cap</b> is <see cref="MostPasses"/> for every solver: a step that hasn't settled by then is given up, and the solver
/// says unknown (<c>unresolved-recursion</c>, or for a loop <c>unsupported-statement</c>), which claims nothing and so is always
/// sound. That is the coarsest widening, to the top.
/// </para>
/// <para>
/// <b>Widening</b>, where a solver has one, is applied to every pass from <see cref="WidenAfter"/> on: it joins the state with
/// the next and jumps what is still growing to an upper bound at which it can stop (a loop's JSON built from itself, to
/// unknown). Only the loop has one; the others' states are finite, or named so that they are (a recursive function's node),
/// and the cap stands in.
/// </para>
/// </remarks>
internal static class FixedPoint
{
    /// <summary>The most passes any solver takes before it gives up.</summary>
    public const int MostPasses = 20;

    /// <summary>The passes taken before a solver's widening applies, so that what settles in a pass or two keeps its precision.</summary>
    public const int WidenAfter = 2;

    /// <summary>
    /// Applies <paramref name="step"/> from <paramref name="start"/> until a pass gives back the state it started from, the step
    /// stops it, or <see cref="MostPasses"/> passes are taken.
    /// </summary>
    /// <param name="start">Where the iteration starts: below the fixed point, so that it is the least one reached.</param>
    /// <param name="step">One pass: the next state from the current and the pass's number, or a stop.</param>
    /// <param name="widen">The solver's widening, applied to the current state and the next from <see cref="WidenAfter"/> on.</param>
    public static Iteration<T> Iterate<T>(T start, Func<T, int, Pass<T>> step, Func<T, T, T>? widen = null)
    {
        var current = start;
        for (var pass = 0; pass < MostPasses; pass++)
        {
            var taken = step(current, pass);
            if (taken.Stopped)
                return new Iteration<T>(IterationEnd.Stopped, current);
            var next = widen is not null && pass >= WidenAfter ? widen(current, taken.Next) : taken.Next;
            if (EqualityComparer<T>.Default.Equals(next, current))
                return new Iteration<T>(IterationEnd.Settled, current);
            current = next;
        }

        return new Iteration<T>(IterationEnd.Unsettled, current);
    }
}

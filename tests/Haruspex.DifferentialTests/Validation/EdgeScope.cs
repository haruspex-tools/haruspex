namespace Haruspex.DifferentialTests.Validation;

/// <summary>Which edges <see cref="Coverage.MissingEdges"/> requires a fixture's seed to reach.</summary>
public enum EdgeScope
{
    /// <summary>
    /// Every edge: each bound, length, minimum length and value of a set, a result of at most one row returning one, and an
    /// array's fewest and most items. For the fixtures whose <c>runs.json</c> sets <c>seedReachesEveryEdge</c>.
    /// </summary>
    Every,

    /// <summary>
    /// The edges of facts the column's or value's type alone doesn't give: a bound, length or minimum length whose source is a
    /// CHECK, an expression's literals, the engine or a declaration, and each value of each set. Every deployed fixture is held
    /// to these: a type's own range is held at its edges by <c>BuiltInTypeObservationTests</c>, the expression observation tests
    /// and, for a procedure's first rowset, <c>DescribedTypeTests</c>, but a CHECK's bound carried into JSON or a later result
    /// set is tested only by a seed that reaches it.
    /// </summary>
    NotFromTheType,
}

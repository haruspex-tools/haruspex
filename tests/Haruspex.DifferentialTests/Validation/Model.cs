namespace Haruspex.DifferentialTests.Validation;

/// <summary>One result set as the driver returned it.</summary>
/// <param name="Columns">The column names, in order.</param>
/// <param name="Rows">Each row's values, in column order; SQL NULL is <c>null</c>, never <see cref="DBNull"/>.</param>
public sealed record ObservedResultSet(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>A claim in the IR that the real output broke.</summary>
/// <param name="Procedure">The procedure's id, such as <c>sales.get_order</c>.</param>
/// <param name="Path">Where in its results, in the IR's path syntax with array indexes: <c>results[0][3].lines[0].note</c>.</param>
/// <param name="Claim">What the IR claimed there.</param>
/// <param name="Value">The value that broke it, as JSON, or <c>(absent)</c>.</param>
public sealed record Violation(string Procedure, string Path, string Claim, string Value)
{
    public override string ToString() => $"{Procedure} {Path}: the IR claims {Claim}, but the value was {Value}";
}

/// <summary>What was seen at a path, so a run can show its data reached every case the IR allows.</summary>
public enum Seen
{
    /// <summary>The value was null, or its key was left out because it was null.</summary>
    Null,

    /// <summary>A value that is not null.</summary>
    Value,

    /// <summary>A result that was absent: a FOR JSON query that returned no row.</summary>
    Absent,
}

/// <param name="Violations">Every broken claim, in the order found.</param>
/// <param name="Seen">
/// What was seen at each path, with array indexes written as <c>[]</c> so that paths match across rows and runs.
/// Paths under an unknown type are not recorded: nothing is claimed there.
/// </param>
/// <param name="Edges">
/// The edges of a scalar's facets that a value reached, by path as in <paramref name="Seen"/>: <c>min</c> and
/// <c>max</c> (a bound, or the nearest step inside an exclusive one), <c>length n</c> (a value of the full length),
/// <c>min length n</c>, and <c>value x</c> for each item of a set. Coverage.MissingEdges holds a fixture's seed to
/// reaching all of them.
/// </param>
public sealed record ValidationReport(
    IReadOnlyList<Violation> Violations,
    IReadOnlySet<(string Path, Seen Seen)> Seen,
    IReadOnlySet<(string Path, string Edge)> Edges);

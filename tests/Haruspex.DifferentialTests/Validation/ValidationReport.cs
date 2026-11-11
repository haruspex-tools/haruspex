namespace Haruspex.DifferentialTests.Validation;

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

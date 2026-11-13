namespace Haruspex.SqlServer.Semantics;

/// <summary>An unqualified column that more than one source of the nearest scope having it has (SQL Server's error 209).</summary>
/// <param name="Problem">Why, located at the reference.</param>
public sealed record AmbiguousColumn(BindingProblem Problem) : Unresolved(Problem);

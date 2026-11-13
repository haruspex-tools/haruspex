namespace Haruspex.SqlServer.Semantics;

/// <summary>A column no source in scope has, or that the source its qualifier names doesn't have (SQL Server's error 207 or 4104).</summary>
/// <param name="Problem">Why, located at the reference.</param>
public sealed record MissingColumn(BindingProblem Problem) : Unresolved(Problem);

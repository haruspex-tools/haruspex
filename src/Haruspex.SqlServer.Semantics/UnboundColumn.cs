namespace Haruspex.SqlServer.Semantics;

/// <summary>A column reference that cannot be bound.</summary>
/// <param name="Problem">Why, located at the reference.</param>
public sealed record UnboundColumn(BindingProblem Problem) : ColumnBinding;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The source can't be bound for certain: every column read from it is unbound, for this reason.</summary>
/// <param name="Problem">Why, located at the source.</param>
public sealed record UnresolvedTarget(BindingProblem Problem) : SourceTarget;

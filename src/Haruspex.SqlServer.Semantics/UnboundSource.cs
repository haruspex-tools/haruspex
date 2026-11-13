namespace Haruspex.SqlServer.Semantics;

/// <summary>A column read from a source that itself can't be bound (<see cref="UnresolvedTarget"/>): the source's problem, moved to the reference.</summary>
/// <param name="Source">The source.</param>
/// <param name="Problem">The source's problem, located at the reference, so each reference is reported where it is.</param>
public sealed record UnboundSource(BoundSource Source, BindingProblem Problem) : Unresolved(Problem);

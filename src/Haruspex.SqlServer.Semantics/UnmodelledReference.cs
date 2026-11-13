namespace Haruspex.SqlServer.Semantics;

/// <summary>A column reference of a form the binder doesn't model yet: three or more parts, or a pseudo-column such as <c>$IDENTITY</c>.</summary>
/// <param name="Problem">Why, located at the reference.</param>
public sealed record UnmodelledReference(BindingProblem Problem) : Unresolved(Problem);

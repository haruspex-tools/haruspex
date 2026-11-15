namespace Haruspex.SqlServer.Semantics;

/// <summary>The star can't be expanded for certain, for this reason.</summary>
/// <param name="Problem">Why, located at the star or at the source it can't list.</param>
public sealed record UnexpandedStar(BindingProblem Problem) : StarExpansion;

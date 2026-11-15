using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The star's columns, each resolved, in the order SQL Server gives them.</summary>
/// <param name="Columns">The columns.</param>
public sealed record ExpandedStar(EquatableArray<StarColumn> Columns) : StarExpansion;

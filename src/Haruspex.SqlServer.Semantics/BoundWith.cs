using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A statement's <c>WITH</c>, bound: its CTEs in order, and the names in force for the rest of the statement.</summary>
/// <param name="Names">The names in force after it: those before it, with its CTEs.</param>
/// <param name="Ctes">Each CTE with its query, in the order written.</param>
public sealed record BoundWith(NameEnvironment Names, EquatableArray<BoundCommonTable> Ctes);

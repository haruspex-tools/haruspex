using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A write's <c>OUTPUT</c>, bound (<see cref="Binder.BindOutput"/>).</summary>
/// <param name="Query">Its select list as a query over the write's sources, <c>inserted</c> and <c>deleted</c>.</param>
/// <param name="Written">The sources that name the write's target in FROM, through its alias (which OUTPUT may read before or after the write) or by its full name (which OUTPUT can't name).</param>
/// <param name="Unmatched">A MERGE's source's sources, when it has a WHEN NOT MATCHED BY SOURCE clause, for whose rows they have none.</param>
/// <param name="Pseudo"><c>inserted</c> and <c>deleted</c>, those the write has.</param>
public sealed record BoundOutput(BoundSpecification Query, EquatableArray<BoundSource> Written, EquatableArray<BoundSource> Unmatched, EquatableArray<PseudoTable> Pseudo);

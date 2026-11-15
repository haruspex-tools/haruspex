using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>The row a query gives (<see cref="Typer"/>): each column's name and value, and how many rows there are.</summary>
/// <param name="Columns">Its columns, in order.</param>
/// <param name="Unlisted">Why its columns are not all listed, when they are not: a <c>SELECT *</c> that can't be expanded.</param>
/// <param name="Rows">How many rows the query returns.</param>
/// <param name="Sources">The sources of its FROM, for a view's keys.</param>
internal sealed record Row(EquatableArray<RowColumn> Columns, UnknownShape? Unlisted, IrCardinality Rows = IrCardinality.Many, EquatableArray<BoundSource>? Sources = null);

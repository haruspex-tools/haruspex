using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>The row a query gives (<see cref="Typer"/>): each column's name and value, and how many rows there are.</summary>
/// <param name="Columns">Its columns, in order.</param>
/// <param name="Star">Where a <c>SELECT *</c> that is not expanded is, when there is one; then the columns are not all listed.</param>
/// <param name="Rows">How many rows the query returns.</param>
/// <param name="Sources">The sources of its FROM, for a view's keys.</param>
internal sealed record Row(EquatableArray<RowColumn> Columns, SourceLocation? Star, IrCardinality Rows = IrCardinality.Many, EquatableArray<BoundSource>? Sources = null);

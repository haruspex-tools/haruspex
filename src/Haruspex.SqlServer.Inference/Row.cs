using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>The row a query gives (<see cref="Typer"/>): each column's name and value, and how many rows there are.</summary>
/// <param name="Columns">Its columns, in order.</param>
/// <param name="Unlisted">Why its columns are not all listed, when they are not: a <c>SELECT *</c> that can't be expanded.</param>
/// <param name="Rows">How many rows the query returns.</param>
/// <param name="Sources">The sources of its FROM, for a view's keys.</param>
internal sealed record Row(EquatableArray<RowColumn> Columns, UnknownShape? Unlisted, IrCardinality Rows = IrCardinality.Many, EquatableArray<BoundSource>? Sources = null)
{
    /// <summary>
    /// Where an existence guard found the row this query reads, when nothing holds the row for it, so another session may
    /// have deleted it since (<see cref="FoundRow"/>): the reason a scalar subquery of it states for being empty.
    /// </summary>
    public SourceLocation? Raced { get; init; }
}

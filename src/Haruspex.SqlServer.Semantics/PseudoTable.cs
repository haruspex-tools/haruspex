using Haruspex.Collections;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary><c>inserted</c> or <c>deleted</c> in a write's <c>OUTPUT</c>: the rows of its target after the write, or before it.</summary>
/// <param name="Name"><c>inserted</c> or <c>deleted</c>.</param>
/// <param name="Columns">The target's columns; null when they are not known (a write through a CTE, a derived table or a view).</param>
public sealed record PseudoTable(string Name, EquatableArray<string>? Columns) : RelationSymbol(Name, Columns)
{
    /// <summary>
    /// The table written, when it is a table of the schema or a table variable, whose declaration says which of its columns
    /// <c>inserted.*</c> and <c>deleted.*</c> give (<see cref="Binder.Star"/>); null for any other target.
    /// </summary>
    public ITable? Written { get; init; }
}

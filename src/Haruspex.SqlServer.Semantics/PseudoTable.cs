using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary><c>inserted</c> or <c>deleted</c> in a write's <c>OUTPUT</c>: the rows of its target after the write, or before it.</summary>
/// <param name="Name"><c>inserted</c> or <c>deleted</c>.</param>
/// <param name="Columns">The target's columns; null when they are not known (a write through a CTE, a derived table or a view).</param>
public sealed record PseudoTable(string Name, EquatableArray<string>? Columns) : RelationSymbol(Name, Columns);

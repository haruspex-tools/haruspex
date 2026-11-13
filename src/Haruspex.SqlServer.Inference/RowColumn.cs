using Haruspex.Diagnostics;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>A result column before it is shaped into a rowset or JSON.</summary>
/// <param name="Origin">
/// The table column it is, unchanged, when it is one (a bare column reference without COLLATE, or a column of
/// <c>SELECT *</c>): what a view's keys are found from.
/// </param>
/// <param name="Expression">
/// The select-list expression it was read from, when there is one: a set operation types an integer literal by its digits
/// and makes a closed set of literals.
/// </param>
internal sealed record RowColumn(string? Name, Value Value, SourceLocation At, TableColumn? Origin = null, ScalarExpression? Expression = null);

using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A <c>VALUES</c> list, <c>FROM (VALUES (…), (…)) AS v (columns)</c>.</summary>
/// <param name="Syntax">The list.</param>
/// <param name="Rows">The scope its rows' values are read in: no FROM of their own, the enclosing query's or an <c>APPLY</c>'s left input's beyond.</param>
/// <param name="Columns">Its column list; null when SQL Server refuses it (a column without a name, two of one name, rows of other lengths).</param>
public sealed record ValuesTable(InlineDerivedTable Syntax, QueryScope Rows, IReadOnlyList<string>? Columns)
    : RelationSymbol(Syntax?.Alias?.Value ?? "", Columns);

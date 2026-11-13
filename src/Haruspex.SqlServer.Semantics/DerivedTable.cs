using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A derived table, <c>FROM (SELECT …) AS d (columns)</c>, bound with its query.</summary>
/// <param name="Syntax">The derived table.</param>
/// <param name="Query">Its query, bound in the scope it reads: the enclosing query's, or an <c>APPLY</c>'s left input's.</param>
/// <param name="Columns">Its column list, or else its query's columns; null when they can't be listed.</param>
public sealed record DerivedTable(QueryDerivedTable Syntax, BoundQuery Query, IReadOnlyList<string>? Columns)
    : RelationSymbol(Syntax?.Alias?.Value ?? "", Columns);

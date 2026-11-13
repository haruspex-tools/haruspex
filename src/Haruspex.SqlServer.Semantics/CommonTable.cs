using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A common table expression of a statement's <c>WITH</c>, which hides any table of its name until the statement ends.</summary>
/// <param name="Definition">Its definition.</param>
/// <param name="Columns">Its columns: its column list, or else its query's (a recursive one's anchor's); null when they can't be listed.</param>
/// <param name="IsRecursive">Whether its query reads its own name, which then means the CTE itself.</param>
public sealed record CommonTable(CommonTableExpression Definition, IReadOnlyList<string>? Columns, bool IsRecursive)
    : RelationSymbol(Definition?.ExpressionName.Value ?? throw new ArgumentNullException(nameof(Definition)), Columns);

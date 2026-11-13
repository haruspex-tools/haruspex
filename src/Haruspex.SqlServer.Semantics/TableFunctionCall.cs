using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A call of a table-valued function in FROM, <c>FROM s.f(@a) AS x</c>.</summary>
/// <param name="Syntax">The call.</param>
/// <param name="Callee">The function it names, or why it names none.</param>
/// <param name="Arguments">The scope its arguments are read in: no FROM of their own, the enclosing query's or an <c>APPLY</c>'s left input's beyond.</param>
/// <param name="Columns">The function's columns: an inline function's query's, or a multi-statement or CLR function's declared table's; null when they can't be listed.</param>
public sealed record TableFunctionCall(SchemaObjectFunctionTableReference Syntax, Callee Callee, QueryScope Arguments, IReadOnlyList<string>? Columns)
    : RelationSymbol(Syntax?.Alias?.Value ?? Syntax?.SchemaObject.BaseIdentifier.Value ?? throw new ArgumentNullException(nameof(Syntax)), Columns);

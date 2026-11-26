using Haruspex.Collections;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// XML's <c>nodes()</c> in FROM or APPLY, <c>CROSS APPLY o.lines.nodes('/lines/line') AS i(n)</c> or <c>FROM @x.nodes('/r/i')
/// AS t(c)</c>: a row for each node its path finds in <see cref="Target"/>, with one column, the one its column list names. The
/// parser reads the first form as a call of a function named by the parts before <c>nodes</c>; its column list tells it apart,
/// since a function's is refused (317), and <c>nodes()</c> without one is refused too (208: observed). Why:
/// docs/decisions/0137-xml-s-nodes-query-for-xml-and-openxml-are-typed-as-the-server-gives-them.md.
/// </summary>
/// <param name="Syntax">The call: a <see cref="SchemaObjectFunctionTableReference"/> or a <see cref="VariableMethodCallTableReference"/>.</param>
/// <param name="Target">The value it is called on: a column reference made of the parts before <c>nodes</c>, or the variable.</param>
/// <param name="Path">Its arguments, as written.</param>
/// <param name="Arguments">The scope its target is read in: the enclosing query's, or an APPLY's left input's.</param>
/// <param name="Columns">The one column its column list names; null when the list names another number, which the server refuses (8159).</param>
public sealed record XmlNodesCall(TableReference Syntax, ScalarExpression Target, EquatableArray<ScalarExpression> Path, QueryScope Arguments, EquatableArray<string>? Columns)
    : RelationSymbol(Alias(Syntax), Columns)
{
    /// <summary>The name it is read by: its alias, which the server requires with the column list.</summary>
    private static string Alias(TableReference? syntax) => (syntax as TableReferenceWithAlias)?.Alias?.Value ?? "nodes()";
}

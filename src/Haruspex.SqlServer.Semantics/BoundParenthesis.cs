using Haruspex.Collections;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A query in parentheses, bound.</summary>
/// <param name="Parenthesis">The parenthesised query.</param>
/// <param name="Inner">The query inside.</param>
public sealed record BoundParenthesis(QueryParenthesisExpression Parenthesis, BoundQuery Inner) : BoundQuery
{
    /// <inheritdoc/>
    public override QueryExpression Syntax => Parenthesis;

    /// <inheritdoc/>
    public override EquatableArray<string?>? Names => Inner.Names;
}

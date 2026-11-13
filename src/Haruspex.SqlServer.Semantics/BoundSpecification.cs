using Haruspex.Collections;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A query specification (<c>SELECT … FROM … WHERE …</c>) with the scope its FROM binds.</summary>
/// <param name="Query">The query.</param>
/// <param name="Scope">Its scope: its sources, the enclosing query's scope beyond them, and the names in force.</param>
/// <param name="ColumnNames">Its columns' names (<see cref="BoundQuery.Names"/>).</param>
public sealed record BoundSpecification(QuerySpecification Query, QueryScope Scope, EquatableArray<string?>? ColumnNames) : BoundQuery
{
    /// <inheritdoc/>
    public override QueryExpression Syntax => Query;

    /// <inheritdoc/>
    public override EquatableArray<string?>? Names => ColumnNames;
}

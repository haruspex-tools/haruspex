using Haruspex.Collections;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A set operation (<c>UNION</c>, <c>INTERSECT</c>, <c>EXCEPT</c>) of two bound queries, as the parser groups them.</summary>
/// <param name="Operation">The set operation.</param>
/// <param name="First">Its first query.</param>
/// <param name="Second">Its second query.</param>
public sealed record BoundSetOperation(BinaryQueryExpression Operation, BoundQuery First, BoundQuery Second) : BoundQuery
{
    /// <inheritdoc/>
    public override QueryExpression Syntax => Operation;

    /// <summary>Its first query's names (observed: A_set_operations_columns_are_named_by_its_first_query_and_typed_by_precedence_over_every_query); null if any query's star can't be expanded.</summary>
    public override EquatableArray<string?>? Names => First.Names is { } names && Second.Names is not null ? names : null;
}

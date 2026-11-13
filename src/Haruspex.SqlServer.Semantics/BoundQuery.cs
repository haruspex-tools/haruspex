using Haruspex.Collections;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A query expression, bound: each query specification in it with its scope (<see cref="BoundSpecification"/>), set
/// operations and parentheses around them. Its derived tables, <c>APPLY</c> sides and argument lists are bound inside its
/// scopes' sources, so the whole is a tree.
/// </summary>
/// <remarks>A closed hierarchy: <see cref="BoundSpecification"/>, <see cref="BoundSetOperation"/>, <see cref="BoundParenthesis"/>.</remarks>
public abstract record BoundQuery
{
    private protected BoundQuery()
    {
    }

    /// <summary>The query expression as written.</summary>
    public abstract QueryExpression Syntax { get; }

    /// <summary>
    /// Its columns' names, one per column and null for a column without one, as SQL Server names them: an alias, <c>$action</c>,
    /// or a column reference's last part; a set operation's are its first query's. Null when a <c>SELECT *</c> is not expanded
    /// (stars are expanded only in a view, whose stars SQL Server binds when it creates it).
    /// </summary>
    public abstract EquatableArray<string?>? Names { get; }

    /// <summary>
    /// Whether this query reads <paramref name="relation"/> in a FROM: a source of one of its scopes, its derived tables' and
    /// <c>APPLY</c> sides' included. A recursive CTE's members are told apart so: those that read the CTE are its recursive
    /// members (SQL Server refuses a recursive reference anywhere else, such as a subquery).
    /// </summary>
    /// <param name="relation">The relation, as binding gave it.</param>
    public bool Reads(RelationSymbol relation) => this switch
    {
        BoundSpecification specification => specification.Scope.Sources.Any(s => s.Target is RelationTarget { Relation: var read }
            && (ReferenceEquals(read, relation) || (read is DerivedTable derived && derived.Query.Reads(relation)))),
        BoundSetOperation operation => operation.First.Reads(relation) || operation.Second.Reads(relation),
        BoundParenthesis parenthesis => parenthesis.Inner.Reads(relation),
    };

    /// <summary>
    /// The names of a relation over this query: <paramref name="columnList"/> when there is one, else <see cref="Names"/>; null
    /// when they can't be listed, as SQL Server refuses a relation whose columns are not each named once (8155, 4506) or a
    /// column list of another length (8158).
    /// </summary>
    /// <param name="columnList">The relation's own column list, empty when it has none.</param>
    public EquatableArray<string>? Listed(IReadOnlyList<string> columnList)
    {
        ArgumentNullException.ThrowIfNull(columnList);
        if (Names is not { } names)
            return null;
        var listed = columnList.Count > 0 ? [.. columnList] : names;
        if (listed.Count != names.Count || listed.Any(string.IsNullOrEmpty) || listed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != listed.Count)
            return null;
        return [.. listed.OfType<string>()];
    }
}

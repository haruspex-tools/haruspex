using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A table source in a FROM clause, bound.</summary>
/// <param name="Alias">The name columns are qualified by: the alias, or the table's name without one; null for a source that has neither.</param>
/// <param name="Target">What it reads.</param>
/// <param name="Optional">Set when it is on an outer join's or an <c>OUTER APPLY</c>'s optional side.</param>
/// <param name="Syntax">The table reference it was bound from.</param>
public sealed record BoundSource(string? Alias, SourceTarget Target, OptionalSide? Optional, TableReference Syntax)
{
    /// <summary>
    /// Whether it is read <c>FOR SYSTEM_TIME</c> <c>ALL</c>, <c>BETWEEN</c>, <c>FROM … TO</c> or <c>CONTAINED IN</c>, which returns
    /// a system-versioned table's history rows too, one per version of a key; so no key of it holds. <c>AS OF</c> returns one
    /// version, and is not history.
    /// </summary>
    public bool History { get; init; }

    /// <summary>
    /// Whether it is read with a <c>NOLOCK</c> or <c>READUNCOMMITTED</c> hint, which on a view applies to every table under it. A
    /// scan under read-uncommitted can return a row twice, or miss one, while pages split, so no key of it holds.
    /// </summary>
    public bool Uncommitted { get; init; }

    /// <summary>
    /// The full name it is known by, when it has no alias and names an object of this database, a temporary table or a
    /// table-valued function: what a column of three parts names it by. Null for a source with an alias, which hides the name
    /// (observed: An_alias_hides_a_tables_full_name), for a CTE (observed: A_cte_has_no_full_name), a derived table and the
    /// other relations a body makes, and for a name whose schema can't be told.
    /// </summary>
    public ExposedName? Exposed { get; init; }

    /// <summary>
    /// The name the FROM writes it by: its alias, or else its name's parts as written, joined by dots, without brackets and in
    /// the FROM's case (<c>dbo.Orders</c>, <c>#t</c>, <c>@t</c>); what <c>FOR JSON AUTO</c> keys a table's array by (observed:
    /// A_source_without_an_alias_is_keyed_by_its_name_as_the_from_clause_writes_it). Null for a source that writes none, and for a
    /// name with an empty part (<c>db..t</c>).
    /// </summary>
    public string? WrittenName => Syntax switch
    {
        TableReferenceWithAlias { Alias: { } alias } => alias.Value,
        NamedTableReference named => Joined(named.SchemaObject),
        SchemaObjectFunctionTableReference function => Joined(function.SchemaObject),
        VariableTableReference variable => variable.Variable.Name,
        _ => null,
    };

    private static string? Joined(SchemaObjectName name) =>
        name.Identifiers.Any(i => string.IsNullOrEmpty(i?.Value)) ? null : string.Join('.', name.Identifiers.Select(i => i.Value));
}

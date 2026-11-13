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
}

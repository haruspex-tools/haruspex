using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A table source in a FROM clause, bound to a table or not.</summary>
/// <param name="Alias">The name columns are qualified by: the alias, or the table's name without one.</param>
/// <param name="Table">The table, or null when the source cannot be bound or is <paramref name="Local"/>.</param>
/// <param name="Unresolved">Why the source cannot be bound, when it is neither a table nor a local relation.</param>
/// <param name="OptionalSideOf">
/// Set when the source is on the optional side of an outer join: the location of that side, in the outermost such
/// join. This is structure, not nullability; what it implies for values is the reader's to decide.
/// </param>
/// <param name="Local">The CTE, derived table, <c>VALUES</c> list, view, temporary table or table variable it reads, when it is one.</param>
/// <param name="OuterApply">
/// Whether <paramref name="OptionalSideOf"/> is the right side of an <c>OUTER APPLY</c> rather than of an outer join. False
/// when an outer join encloses the APPLY, whose optional side it then is.
/// </param>
/// <param name="WholeApplied">
/// With <paramref name="OuterApply"/>: whether the source is that right side whole (an applied query, a <c>VALUES</c> list, a
/// view or a table), rather than one of the sources joined inside it. Only then is the side null-extended exactly when this
/// source has no row, so a relation that always has one is never null-extended; which relations those are is the reader's
/// to decide.
/// </param>
/// <param name="History">
/// Whether it is read <c>FOR SYSTEM_TIME</c> <c>ALL</c>, <c>BETWEEN</c>, <c>FROM … TO</c> or <c>CONTAINED IN</c>, which returns
/// a system-versioned table's history rows too, one per version of a key; so no key of it holds. <c>AS OF</c> returns one
/// version, and is not history.
/// </param>
/// <param name="Uncommitted">
/// Whether it is read with a <c>NOLOCK</c> or <c>READUNCOMMITTED</c> hint, which on a view applies to every table under it. A
/// scan under read-uncommitted can return a row twice, or miss one, while pages split, so no key of it holds.
/// </param>
public sealed record BoundSource(
    string? Alias,
    Table? Table,
    BindingProblem? Unresolved,
    SourceLocation? OptionalSideOf,
    LocalRelation? Local = null,
    bool OuterApply = false,
    bool WholeApplied = false,
    bool History = false,
    bool Uncommitted = false);

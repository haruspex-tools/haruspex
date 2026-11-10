using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>The sources a query's FROM clause binds, with the enclosing query's scope for correlated subqueries.</summary>
public sealed class BindingScope
{
    private readonly List<BoundSource> sources = [];

    internal BindingScope(BindingScope? parent, ILocalRelations? locals)
    {
        Parent = parent;
        Locals = locals ?? parent?.Locals;
    }

    /// <summary>The relations the body defines that names in this scope may refer to; inherited by nested scopes.</summary>
    public ILocalRelations? Locals { get; }

    /// <summary>The enclosing query's scope, or null for an outermost query.</summary>
    public BindingScope? Parent { get; }

    /// <summary>The sources, in the order they appear.</summary>
    public IReadOnlyList<BoundSource> Sources => sources;

    internal void Add(BoundSource source) => sources.Add(source);

    /// <summary>
    /// The scope an <c>APPLY</c>'s right side reads its outer columns from: the sources of the APPLY's left input, from
    /// <paramref name="first"/> on, as the same objects, with this scope's parent beyond them.
    /// </summary>
    /// <param name="first">The position of the left input's first source.</param>
    /// <param name="outer">The scope an enclosing APPLY's right side reads, when this APPLY is inside one; else the parent.</param>
    internal BindingScope Lateral(int first, BindingScope? outer)
    {
        var lateral = new BindingScope(outer ?? Parent, Locals);
        lateral.sources.AddRange(sources.Skip(first));
        return lateral;
    }
}

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

/// <summary>What a column reference binds to.</summary>
public abstract record ColumnBinding;

/// <summary>A column reference bound to a column of a table in scope.</summary>
/// <param name="Source">The source it was found in.</param>
/// <param name="Table">The source's table.</param>
/// <param name="Column">The column.</param>
public sealed record BoundColumn(BoundSource Source, Table Table, Column Column) : ColumnBinding;

/// <summary>A column reference bound to a column of a CTE, temporary table or table variable in scope.</summary>
/// <param name="Source">The source it was found in.</param>
/// <param name="Relation">The source's relation.</param>
/// <param name="Ordinal">The column's position, or -1 when the relation's columns are not known.</param>
public sealed record BoundLocalColumn(BoundSource Source, LocalRelation Relation, int Ordinal) : ColumnBinding;

/// <summary>A column reference that cannot be bound.</summary>
/// <param name="Problem">Why, located at the reference.</param>
public sealed record UnboundColumn(BindingProblem Problem) : ColumnBinding;

/// <summary>Why a reference cannot be bound.</summary>
/// <param name="Kind">The kind of problem.</param>
/// <param name="Detail">A sentence saying what was found.</param>
/// <param name="At">Where.</param>
public sealed record BindingProblem(BindingProblemKind Kind, string Detail, SourceLocation At);

/// <summary>The kinds of <see cref="BindingProblem"/>.</summary>
public enum BindingProblemKind
{
    /// <summary>A table source the binder does not model yet: another database, a temporary table, a derived table, some joins.</summary>
    UnsupportedSource,

    /// <summary>A name that resolves to no table, to an object that is not a table, or to more than one object.</summary>
    UnresolvedTable,

    /// <summary>The table is changed after its definition in a way the model does not apply (see <see cref="Redefinition"/>).</summary>
    TableRedefined,

    /// <summary>A column reference of a form the binder does not model yet, such as a three-part name.</summary>
    UnsupportedReference,

    /// <summary>A column that no table in scope has, or more than one has.</summary>
    UnresolvedColumn,
}

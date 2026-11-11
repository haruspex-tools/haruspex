using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A relation a body defines for itself rather than one in the schema: a common table expression, a derived table, a
/// <c>VALUES</c> list, a temporary table (<c>#t</c>) or a table variable (<c>@t</c>); or a view, read from its definition. The binder binds references to it by name and its columns by name; what
/// its columns hold is not binding's concern, so a relation carries only names, and whoever supplies it
/// (<see cref="ILocalRelations"/>) keeps the rest, keyed by the relation itself.
/// </summary>
/// <remarks>
/// A relation is compared by reference: each state of a temporary table, or each step of a recursive CTE's fixed point,
/// is a relation of its own, so a binding always says which one it read.
/// </remarks>
/// <param name="name">Its name as written: <c>#t</c>, <c>@t</c>, or a CTE's name.</param>
/// <param name="columns">Its columns' names in order, or null when they are not known, so that any column may be one of its.</param>
public sealed class LocalRelation(string name, IReadOnlyList<string>? columns)
{
    /// <summary>The name.</summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>The columns' names in order, or null when not known.</summary>
    public IReadOnlyList<string>? Columns { get; } = columns;

    /// <summary>The position of the column with this name, ignoring case as SQL Server does; -1 when there is none.</summary>
    public int IndexOf(string column)
    {
        if (Columns is null)
            return -1;
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Equals(column, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>The relation's name.</summary>
    public override string ToString() => Name;
}

/// <summary>The relations a body has defined at the point being bound: its CTEs in scope, temporary tables and table variables.</summary>
public interface ILocalRelations
{
    /// <summary>
    /// The relation a one-part name in FROM means here, or null when the body defines none of that name. For a name
    /// starting with <c>#</c> or <c>@</c> a null means the binder knows nothing of it; for any other it falls through to
    /// the schema's tables, which a CTE of the same name hides.
    /// </summary>
    /// <param name="name">The name as written, with its sigil.</param>
    LocalRelation? Find(string name);

    /// <summary>
    /// The relation a derived table (<c>FROM (SELECT …) AS d</c>) is, or null when it is not modelled. Its query is read
    /// by whoever supplies the relations, since what its columns hold is not binding's concern; it may read the enclosing
    /// queries' columns, so it is given their scope.
    /// </summary>
    /// <param name="table">The derived table.</param>
    /// <param name="outer">The scope of the query enclosing the one whose FROM it is in, or null.</param>
    LocalRelation? Derived(QueryDerivedTable table, BindingScope? outer) => null;

    /// <summary>
    /// The relation a <c>VALUES</c> list (<c>FROM (VALUES (…), (…)) AS v (columns)</c>) is, or null when it is not modelled.
    /// Its rows may read the enclosing queries' columns, or an <c>APPLY</c>'s left input's, so it is given that scope.
    /// </summary>
    /// <param name="table">The list.</param>
    /// <param name="outer">The scope its values may read beyond their own, or null.</param>
    LocalRelation? Values(InlineDerivedTable table, BindingScope? outer) => null;

    /// <summary>
    /// The relation a view of the project is, read from its definition, or null when views are not modelled here. A view
    /// is bound where it is defined, so it is not given a scope.
    /// </summary>
    /// <param name="view">The view; its <see cref="NamedObject.View"/> is its definition.</param>
    /// <param name="uncommitted">
    /// Whether it is read with a <c>NOLOCK</c> or <c>READUNCOMMITTED</c> hint, which applies to every table under it, so its
    /// definition is read uncommitted too.
    /// </param>
    LocalRelation? View(NamedObject view, bool uncommitted) => null;

    /// <summary>
    /// The relation a call of a table-valued function in FROM (<c>FROM s.f(@a) AS x</c>) is, or null when functions are not
    /// modelled here. Its arguments may read the enclosing queries' columns, or an <c>APPLY</c>'s left input's, so it is given
    /// that scope; which function it names, and what that returns, is the supplier's to decide.
    /// </summary>
    /// <param name="reference">The call.</param>
    /// <param name="outer">The scope its arguments may read beyond their own, or null.</param>
    LocalRelation? TableFunction(SchemaObjectFunctionTableReference reference, BindingScope? outer) => null;

    /// <summary>
    /// The relation an <c>OPENJSON</c> call in FROM (<c>OPENJSON(t.doc) WITH (…) AS j</c>) is, or null when it is not modelled.
    /// Its arguments may read the enclosing queries' columns, or an <c>APPLY</c>'s left input's, so it is given that scope.
    /// </summary>
    /// <param name="reference">The call.</param>
    /// <param name="outer">The scope its arguments may read beyond their own, or null.</param>
    LocalRelation? OpenJson(OpenJsonTableReference reference, BindingScope? outer) => null;

    /// <summary>
    /// The relation a <c>GENERATE_SERIES</c> call in FROM (<c>GENERATE_SERIES(1, t.n) AS s</c>) is, or null when it is not
    /// modelled. Its arguments may read the enclosing queries' columns, or an <c>APPLY</c>'s left input's, so it is given that
    /// scope.
    /// </summary>
    /// <param name="reference">The call.</param>
    /// <param name="outer">The scope its arguments may read beyond their own, or null.</param>
    LocalRelation? GenerateSeries(GlobalFunctionTableReference reference, BindingScope? outer) => null;
}

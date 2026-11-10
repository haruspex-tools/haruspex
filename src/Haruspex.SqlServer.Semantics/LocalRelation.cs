namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A relation a body defines for itself rather than one in the schema: a common table expression, a temporary table
/// (<c>#t</c>) or a table variable (<c>@t</c>). The binder binds references to it by name and its columns by name; what
/// its columns hold is not binding's concern, so a relation carries only names, and whoever supplies it
/// (<see cref="ILocalRelations"/>) keeps the rest, keyed by the relation itself.
/// </summary>
/// <remarks>
/// A relation is compared by reference: each state of a temporary table, or each step of a recursive CTE's fixed point,
/// is a relation of its own, so a binding always says which one it read.
/// </remarks>
public sealed class LocalRelation
{
    /// <summary>Creates a relation.</summary>
    /// <param name="name">Its name as written: <c>#t</c>, <c>@t</c>, or a CTE's name.</param>
    /// <param name="columns">Its columns' names in order, or null when they are not known, so that any column may be one of its.</param>
    public LocalRelation(string name, IReadOnlyList<string>? columns)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Columns = columns;
    }

    /// <summary>The name.</summary>
    public string Name { get; }

    /// <summary>The columns' names in order, or null when not known.</summary>
    public IReadOnlyList<string>? Columns { get; }

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
}

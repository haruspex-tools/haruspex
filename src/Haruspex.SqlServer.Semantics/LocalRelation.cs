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

using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A relation a body reads that is not a table of the schema: one it defines for itself (a CTE, a derived table, a
/// <c>VALUES</c> list, a temporary table, a table variable, <c>inserted</c> and <c>deleted</c>), or one whose rows come from a
/// definition or a call (a view, a table-valued function, <c>OPENJSON</c>, <c>GENERATE_SERIES</c>). It is what a name in
/// FROM means, as a compiler's symbol is what an identifier means; binding needs only its columns' names, so that is all a
/// symbol holds of them, and what they hold is attached by whoever reads the body (Inference), keyed by the source that
/// reads it.
/// </summary>
/// <remarks>
/// A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md): every kind is a sealed record
/// here, and a switch over them lists each.
/// </remarks>
public abstract record RelationSymbol
{
    private protected RelationSymbol(string name, EquatableArray<string>? columns)
    {
        Name = name;
        Columns = columns;
    }

    /// <summary>Its name, as messages give it: the name or alias it is read by, <c>#t</c>, <c>@t</c>, a view's full name.</summary>
    public string Name { get; }

    /// <summary>
    /// Its columns' names in order, or null when binding can't list them (a query of <c>SELECT *</c> not expanded, a column
    /// without a name, two of one name, a temporary table whose columns the walk doesn't know); then any column may be one
    /// of its.
    /// </summary>
    public EquatableArray<string>? Columns { get; }

    /// <summary>The position of the column with this name, ignoring case as SQL Server does; -1 when there is none or the columns are not known.</summary>
    /// <param name="column">The column's name.</param>
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
    public sealed override string ToString() => Name;
}

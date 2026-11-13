using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A column among its source's columns, as what the conditions being read prove is kept (<see cref="Facts"/>): a table's
/// column (<see cref="TableColumnKey"/>), or a relation's by its position (<see cref="RelationColumnKey"/>), since a CTE's or
/// derived table's columns are named by binding and may repeat a name a select list leaves to be renamed.
/// </summary>
internal abstract record ColumnKey
{
    private protected ColumnKey()
    {
    }

    /// <summary>The key of the column a reference binds to in a table.</summary>
    public static ColumnKey Of(TableColumn bound) => new TableColumnKey(bound.Column);

    /// <summary>The key of the column a reference binds to in a relation.</summary>
    public static ColumnKey Of(RelationColumn bound) => new RelationColumnKey(bound.Ordinal);
}

using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>A column of a table variable that holds JSON built here: the table variable, and the column's name.</summary>
/// <remarks>
/// A column is named as a write names it (<c>UPDATE @t SET Body = …</c>) or as the table declares it (<c>body</c>), which SQL
/// Server takes as one column, so the column's name compares ignoring case, as the table variable's does.
/// </remarks>
/// <param name="Table">The table variable.</param>
/// <param name="Column">The column's name.</param>
internal sealed record ColumnSlot(LocalName Table, string Column) : JsonSlotKey
{
    /// <summary>Whether both are one column of one table variable, ignoring case.</summary>
    public bool Equals(ColumnSlot? other) =>
        other is not null && Table == other.Table && StringComparer.OrdinalIgnoreCase.Equals(Column, other.Column);

    /// <summary>A hash consistent with the case-insensitive <see cref="Equals(ColumnSlot)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(Table, StringComparer.OrdinalIgnoreCase.GetHashCode(Column));

    /// <summary>The column as messages give it: <c>@orders.body</c>.</summary>
    public override string ToString() => $"{Table}.{Column}";
}

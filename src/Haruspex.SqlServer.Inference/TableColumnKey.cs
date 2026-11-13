using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>A table's column, by the column itself: its declaration names it once in its table.</summary>
/// <param name="Column">The column, as its table declares it.</param>
internal sealed record TableColumnKey(Column Column) : ColumnKey;

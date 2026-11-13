namespace Haruspex.SqlServer.Inference;

/// <summary>A relation's column, by its position among the relation's columns, from 0.</summary>
/// <param name="Ordinal">Its position.</param>
internal sealed record RelationColumnKey(int Ordinal) : ColumnKey;

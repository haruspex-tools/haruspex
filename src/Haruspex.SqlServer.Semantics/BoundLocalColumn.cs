namespace Haruspex.SqlServer.Semantics;

/// <summary>A column reference bound to a column of a CTE, temporary table or table variable in scope.</summary>
/// <param name="Source">The source it was found in.</param>
/// <param name="Relation">The source's relation.</param>
/// <param name="Ordinal">The column's position, or -1 when the relation's columns are not known.</param>
public sealed record BoundLocalColumn(BoundSource Source, LocalRelation Relation, int Ordinal) : ColumnBinding;

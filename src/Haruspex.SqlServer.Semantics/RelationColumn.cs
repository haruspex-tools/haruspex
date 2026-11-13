namespace Haruspex.SqlServer.Semantics;

/// <summary>A column of a relation (a CTE, a derived table, a temporary table and the rest), read through a source in scope.</summary>
/// <param name="Source">The source.</param>
/// <param name="Relation">The source's relation.</param>
/// <param name="Ordinal">The column's position among the relation's.</param>
public sealed record RelationColumn(BoundSource Source, RelationSymbol Relation, int Ordinal) : Resolution;

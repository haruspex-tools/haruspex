namespace Haruspex.SqlServer.Semantics;

/// <summary>The source reads a relation that is not a table of the schema: a CTE, a derived table, a view, a temporary table and the rest.</summary>
/// <param name="Relation">The relation.</param>
public sealed record RelationTarget(RelationSymbol Relation) : SourceTarget;

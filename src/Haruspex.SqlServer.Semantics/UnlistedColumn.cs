namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A column taken to be one of a relation whose columns binding can't list, because no source in scope is known to have
/// it and that one may: so its value is whatever the relation's unknown columns are.
/// </summary>
/// <param name="Source">The source.</param>
/// <param name="Relation">The source's relation, whose <see cref="RelationSymbol.Columns"/> is null.</param>
public sealed record UnlistedColumn(BoundSource Source, RelationSymbol Relation) : Resolution;

namespace Haruspex.SqlServer.Semantics;

/// <summary>What a table source in FROM reads: a table of the schema, a relation (<see cref="RelationSymbol"/>), or nothing it can be bound to.</summary>
/// <remarks>A closed hierarchy: <see cref="TableTarget"/>, <see cref="RelationTarget"/>, <see cref="UnresolvedTarget"/>.</remarks>
public abstract record SourceTarget
{
    private protected SourceTarget()
    {
    }
}

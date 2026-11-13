namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What a column reference means, as the one resolver (<see cref="Binder.Resolve"/>) finds it: a column of a table, a column
/// of a relation (listed or not), or why it can't be bound for certain, which is never a guess.
/// </summary>
/// <remarks>
/// A closed hierarchy: <see cref="TableColumn"/>, <see cref="RelationColumn"/>, <see cref="UnlistedColumn"/>, and the cases of
/// <see cref="Unresolved"/>: <see cref="AmbiguousColumn"/>, <see cref="MissingColumn"/>, <see cref="UnmodelledReference"/> and
/// <see cref="UnboundSource"/>.
/// </remarks>
public abstract record Resolution
{
    private protected Resolution()
    {
    }
}

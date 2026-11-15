namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What a <c>SELECT *</c> or <c>alias.*</c> stands for, as binding finds it (<see cref="Binder.Star"/>): its columns, or why
/// they can't be listed.
/// </summary>
/// <remarks>
/// A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md): <see cref="ExpandedStar"/>,
/// <see cref="UnlistedStar"/> and <see cref="UnexpandedStar"/>.
/// </remarks>
public abstract record StarExpansion
{
    private protected StarExpansion()
    {
    }
}

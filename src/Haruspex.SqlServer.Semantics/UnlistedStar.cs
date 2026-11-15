namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The star reads a relation whose columns binding can't list: a caller's temporary table, or a query whose own columns
/// can't be. Why is what reading that relation finds, which is the reader's to say.
/// </summary>
/// <param name="Source">The first source whose columns can't be listed.</param>
public sealed record UnlistedStar(BoundSource Source) : StarExpansion;

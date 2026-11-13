namespace Haruspex.SqlServer.Semantics;

/// <summary>One CTE of a <c>WITH</c>, bound.</summary>
/// <param name="Symbol">What its name means in the rest of the statement.</param>
/// <param name="Query">Its query, bound with the CTEs before it in scope, and itself when it is recursive.</param>
public sealed record BoundCommonTable(CommonTable Symbol, BoundQuery Query);

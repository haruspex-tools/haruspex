using Haruspex.Collections;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A window with every name resolved: what the function is computed over, or why the server refuses it.</summary>
/// <param name="Partitions">Its <c>PARTITION BY</c> expressions, none for the whole result.</param>
/// <param name="Order">Its <c>ORDER BY</c>, or null.</param>
/// <param name="Frame">Its <c>ROWS</c> or <c>RANGE</c> frame, or null for the default.</param>
/// <param name="Refused">Why SQL Server refuses the window when the procedure is created; null when it doesn't.</param>
public sealed record ResolvedWindow(EquatableArray<ScalarExpression> Partitions, OrderByClause? Order, WindowFrameClause? Frame, string? Refused = null)
{
    /// <summary>A window the server refuses, saying why.</summary>
    internal static ResolvedWindow Refusal(string why) => new([], null, null, why);
}

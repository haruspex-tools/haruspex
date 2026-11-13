using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A CHECK constraint as declared.</summary>
/// <remarks>
/// Only the declaration: what it implies for values (a value set, a range) is an interpretation, which belongs to
/// whoever reads the model (in haruspex, Haruspex.SqlServer.Semantics).
/// </remarks>
/// <param name="Name">Its name, or null when none is declared and SQL Server is left to generate one.</param>
/// <param name="Expression">Its condition, as ScriptDom parsed it.</param>
/// <param name="Column">The column a column constraint is declared on; null for a table constraint.</param>
/// <param name="IsNotForReplication">
/// Whether it is declared <c>NOT FOR REPLICATION</c>, so that rows written by a replication agent are not checked.
/// </param>
/// <param name="At">Where it is declared.</param>
/// <param name="DisabledAt">
/// Where a statement after the definition may disable it, such as <c>ALTER TABLE … NOCHECK CONSTRAINT</c> naming it or
/// <c>ALL</c>; null when none does. Which runs last depends on the order of the scripts, which the source does not say.
/// </param>
public sealed record CheckConstraint(string? Name, BooleanExpression Expression, string? Column, bool IsNotForReplication, SourceLocation At, SourceLocation? DisabledAt = null)
{
    /// <summary>The constraint's name, or where it is declared.</summary>
    public override string ToString() => Name ?? At.ToString();
}

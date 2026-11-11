using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A CHECK constraint as declared.</summary>
/// <remarks>
/// Only the declaration: what it implies for values (a value set, a range) is an interpretation, which belongs to
/// whoever reads the model (in haruspex, Haruspex.SqlServer.Semantics).
/// </remarks>
/// <param name="name">Its name, or null when SQL Server is left to generate one.</param>
/// <param name="expression">Its condition, as ScriptDom parsed it.</param>
/// <param name="column">The column it is declared on, for a column constraint; null for a table constraint.</param>
/// <param name="isNotForReplication">Whether it is declared <c>NOT FOR REPLICATION</c>.</param>
/// <param name="at">Where it is declared.</param>
/// <param name="disabledAt">Where a later statement may disable it (<c>ALTER TABLE … NOCHECK CONSTRAINT</c>), or null.</param>
public sealed class CheckConstraint(string? name, BooleanExpression expression, string? column, bool isNotForReplication, SourceLocation at, SourceLocation? disabledAt = null)
{
    /// <summary>The constraint's name, or null when none is declared.</summary>
    public string? Name { get; } = name;

    /// <summary>The condition.</summary>
    public BooleanExpression Expression { get; } = expression ?? throw new ArgumentNullException(nameof(expression));

    /// <summary>The column a column constraint is declared on; null for a table constraint.</summary>
    public string? Column { get; } = column;

    /// <summary>
    /// Whether it is declared <c>NOT FOR REPLICATION</c>, so that rows written by a replication agent are not
    /// checked.
    /// </summary>
    public bool IsNotForReplication { get; } = isNotForReplication;

    /// <summary>Where it is declared.</summary>
    public SourceLocation At { get; } = at ?? throw new ArgumentNullException(nameof(at));

    /// <summary>
    /// Where a statement after the definition may disable it, such as <c>ALTER TABLE … NOCHECK CONSTRAINT</c>
    /// naming it or <c>ALL</c>; null when none does. Which runs last depends on the order of the scripts, which
    /// the source does not say.
    /// </summary>
    public SourceLocation? DisabledAt { get; } = disabledAt;

    /// <summary>A copy that a later statement may disable.</summary>
    public CheckConstraint WithDisabledAt(SourceLocation at) => new(Name, Expression, Column, IsNotForReplication, At, at);

    /// <summary>The constraint's name, or where it is declared.</summary>
    public override string ToString() => Name ?? At.ToString();
}

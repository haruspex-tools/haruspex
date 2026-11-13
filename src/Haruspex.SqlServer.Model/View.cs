using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A view: the query it is, and the names it gives its columns.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is.</param>
/// <param name="Columns">The names its column list gives (<c>CREATE VIEW v (a, b)</c>); empty when it has none.</param>
/// <param name="Query">Its query, as ScriptDom parsed it. What it reads matters to DML through it, which changes the tables underneath.</param>
public sealed record View(ObjectName Name, SourceSpan Span, IReadOnlyList<string> Columns, SelectStatement Query) : Module(Name, Span)
{
    /// <summary>
    /// Whether it was created with ANSI_NULLS off, which it keeps (as a procedure does): set by <c>SET ANSI_NULLS OFF</c> before
    /// its definition in the same file.
    /// </summary>
    public bool AnsiNullsOff { get; init; }

    /// <summary>
    /// The second definition of a view defined more than once (created and altered, or created twice), so that which
    /// definition applies depends on the order the scripts run; null when it is defined once.
    /// </summary>
    public Redefinition? Redefinition { get; init; }
}

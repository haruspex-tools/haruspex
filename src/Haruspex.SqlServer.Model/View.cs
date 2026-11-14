using Haruspex.Collections;
using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A view: the query it is, and the names it gives its columns.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is.</param>
/// <param name="Columns">
/// The names its column list gives (<c>CREATE VIEW v (a, b)</c>); empty when it has none. For a view whose text the source
/// doesn't hold, the names of its columns as the source lists them.
/// </param>
/// <param name="Query">
/// Its query, as ScriptDom parsed it. What it reads matters to DML through it, which changes the tables underneath. Null when the
/// source doesn't hold its text: a view created <c>WITH ENCRYPTION</c>, in a DACPAC extracted from a database, whose columns are
/// then known by name only.
/// </param>
public sealed record View(ObjectName Name, SourceSpan Span, EquatableArray<string> Columns, SelectStatement? Query) : Module(Name, Span)
{
    /// <summary>
    /// Whether it was created with ANSI_NULLS off, which it keeps (as a procedure does): set by <c>SET ANSI_NULLS OFF</c> before
    /// its definition in the same file.
    /// </summary>
    public bool AnsiNullsOff { get; init; }

    /// <summary>
    /// Whether it was created <c>WITH ENCRYPTION</c>. With no <see cref="Query"/>, it says why the text is missing: encrypted, in a
    /// DACPAC extracted from a database; or else a system view, which <c>master.dacpac</c> lists without a definition.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// The second definition of a view defined more than once (created and altered, or created twice), so that which
    /// definition applies depends on the order the scripts run; null when it is defined once.
    /// </summary>
    public Redefinition? Redefinition { get; init; }
}

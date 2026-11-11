using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A stored procedure: its name and its definition as ScriptDom parsed it.</summary>
/// <param name="name">Its name.</param>
/// <param name="body">Its definition: parameters, and statements or a CLR method.</param>
/// <param name="file">The file the definition is in, as in <see cref="SourceLocation.File"/>.</param>
/// <param name="redefinition">Set when the source also changes the procedure elsewhere.</param>
public sealed class Procedure(ObjectName name, ProcedureStatementBody body, string file, Redefinition? redefinition = null)
{
    /// <summary>The procedure's name.</summary>
    public ObjectName Name { get; } = name;

    /// <summary>
    /// The definition: a <c>CREATE PROCEDURE</c>, <c>CREATE OR ALTER PROCEDURE</c> or, when the source only alters
    /// the procedure, its <c>ALTER PROCEDURE</c>. Lines and columns in it are positions in <see cref="File"/>.
    /// </summary>
    public ProcedureStatementBody Body { get; } = body ?? throw new ArgumentNullException(nameof(body));

    /// <summary>
    /// Whether <c>ANSI_NULLS</c> was off when the procedure was created: SQL Server keeps the setting in force then, and with it
    /// off, <c>= NULL</c> and <c>&lt;&gt; NULL</c> against a variable or a literal are TRUE or FALSE rather than UNKNOWN. True only
    /// when <c>SET ANSI_NULLS OFF</c> (or <c>SET ANSI_DEFAULTS OFF</c>) comes before the definition in its file, with no
    /// <c>SET ANSI_NULLS ON</c> between; otherwise it is on, the default of every client library and of SSDT.
    /// </summary>
    public bool AnsiNullsOff { get; init; }

    /// <summary>The file the definition is in, relative to the source root, as in <see cref="SourceLocation.File"/>.</summary>
    public string File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    /// <summary>Where the definition starts.</summary>
    public SourceLocation At => new(File, Body.StartLine, Body.StartColumn);

    /// <summary>
    /// Set when the source also alters or drops the procedure, so <see cref="Body"/> may not be the definition in
    /// effect. Null when the definition is the only one.
    /// </summary>
    public Redefinition? Redefinition { get; } = redefinition;

    /// <summary>The procedure's name.</summary>
    public override string ToString() => Name.ToString();
}

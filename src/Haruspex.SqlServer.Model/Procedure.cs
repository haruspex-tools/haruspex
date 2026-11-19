using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>A stored procedure: its parameters and its body.</summary>
/// <remarks>
/// The definition read is the one that stands once its script has run: a <c>CREATE PROCEDURE</c>, a <c>CREATE OR ALTER
/// PROCEDURE</c> or an <c>ALTER PROCEDURE</c>, the last of them in the script. When another script also defines or changes it,
/// or its own leaves which stands in doubt, <see cref="Redefinition"/> says so.
/// </remarks>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is.</param>
/// <param name="Parameters">Its parameters, in declaration order.</param>
/// <param name="Body">What it runs.</param>
public sealed record Procedure(ObjectName Name, SourceSpan Span, EquatableArray<Parameter> Parameters, ModuleBody Body) : Module(Name, Span)
{
    /// <summary>
    /// Whether <c>ANSI_NULLS</c> was off when the procedure was created: SQL Server keeps the setting in force then, and with it
    /// off, <c>= NULL</c> and <c>&lt;&gt; NULL</c> against a variable or a literal are TRUE or FALSE rather than UNKNOWN. True only
    /// when <c>SET ANSI_NULLS OFF</c> (or <c>SET ANSI_DEFAULTS OFF</c>) comes before the definition in its file, with no
    /// <c>SET ANSI_NULLS ON</c> between; otherwise it is on, the default of every client library and of SSDT.
    /// </summary>
    public bool AnsiNullsOff { get; init; }

    /// <summary>
    /// The first statement that also alters or drops the procedure, so that <see cref="Body"/> may not be the definition in
    /// effect; null when the definition is the only one.
    /// </summary>
    public Redefinition? Redefinition { get; init; }
}

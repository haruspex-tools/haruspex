using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A user-defined function: its name and its definition as ScriptDom parsed it.</summary>
/// <remarks>
/// Every kind of function is recorded (scalar, inline and multi-statement table-valued, CLR), so that a reader can tell
/// them apart by <see cref="FunctionStatementBody.ReturnType"/>. The function is also a <see cref="NamedObject"/>, as
/// before, for the binder: a FROM clause may name a table-valued one.
/// </remarks>
/// <param name="name">Its name.</param>
/// <param name="body">Its definition: parameters, return type, options, and statements or a CLR method.</param>
/// <param name="span">Where the definition is.</param>
/// <param name="redefinition">Set when the source also changes the function elsewhere.</param>
public sealed class UserFunction(ObjectName name, FunctionStatementBody body, SourceSpan span, Redefinition? redefinition = null)
{
    /// <summary>The function's name.</summary>
    public ObjectName Name { get; } = name;

    /// <summary>
    /// The definition: a <c>CREATE FUNCTION</c>, <c>CREATE OR ALTER FUNCTION</c> or, when the source only alters the
    /// function, its <c>ALTER FUNCTION</c>. Lines and columns in it are positions in <see cref="Span"/>'s file.
    /// </summary>
    public FunctionStatementBody Body { get; } = body ?? throw new ArgumentNullException(nameof(body));

    /// <summary>
    /// Whether <c>ANSI_NULLS</c> was off when the function was created: SQL Server keeps the setting in force then, and with it
    /// off, <c>= NULL</c> and <c>&lt;&gt; NULL</c> against a variable or a literal are TRUE or FALSE rather than UNKNOWN. True only
    /// when <c>SET ANSI_NULLS OFF</c> (or <c>SET ANSI_DEFAULTS OFF</c>) comes before the definition in its file, with no
    /// <c>SET ANSI_NULLS ON</c> between; otherwise it is on, the default of every client library and of SSDT.
    /// </summary>
    public bool AnsiNullsOff { get; init; }

    /// <summary>Where the definition is; its body's positions are in its file.</summary>
    public SourceSpan Span { get; } = span ?? throw new ArgumentNullException(nameof(span));

    /// <summary>
    /// Set when the source also alters or drops the function, so <see cref="Body"/> may not be the definition in effect.
    /// </summary>
    public Redefinition? Redefinition { get; } = redefinition;

    /// <summary>The function's name.</summary>
    public override string ToString() => Name.ToString();
}

using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A user-defined function: a <see cref="ScalarFunction"/>, an <see cref="InlineFunction"/> (a query with parameters) or a
/// <see cref="TableFunction"/> (a table its body fills).
/// </summary>
/// <remarks>
/// The definition read is a <c>CREATE FUNCTION</c>, a <c>CREATE OR ALTER FUNCTION</c> or, when the source only alters the
/// function, its <c>ALTER FUNCTION</c>; when the source also changes it elsewhere, <see cref="Redefinition"/> says so. The
/// three kinds are all there are (decision 0031).
/// </remarks>
#pragma warning disable CA1716 // SQL Server's own word for it. Visual Basic, where it is a keyword, writes it in brackets.
public abstract record Function : Module
#pragma warning restore CA1716
{
    /// <summary>Creates a function; only the kinds in this assembly can.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="span">Where its definition is.</param>
    /// <param name="parameters">Its parameters, in declaration order.</param>
    private protected Function(ObjectName name, SourceSpan span, EquatableArray<Parameter> parameters)
        : base(name, span)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    /// <summary>Its parameters, in declaration order.</summary>
    public EquatableArray<Parameter> Parameters { get; }

    /// <summary>
    /// Whether <c>ANSI_NULLS</c> was off when the function was created: SQL Server keeps the setting in force then, and with it
    /// off, <c>= NULL</c> and <c>&lt;&gt; NULL</c> against a variable or a literal are TRUE or FALSE rather than UNKNOWN. True only
    /// when <c>SET ANSI_NULLS OFF</c> (or <c>SET ANSI_DEFAULTS OFF</c>) comes before the definition in its file, with no
    /// <c>SET ANSI_NULLS ON</c> between; otherwise it is on, the default of every client library and of SSDT.
    /// </summary>
    public bool AnsiNullsOff { get; init; }

    /// <summary>
    /// The first statement that also alters or drops the function, so that its definition may not be the one in effect; null
    /// when the definition is the only one.
    /// </summary>
    public Redefinition? Redefinition { get; init; }
}

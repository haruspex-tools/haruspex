using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Syntax;

/// <summary>
/// A SQLCMD variable reference, <c>$(Region)</c>: text that a deployment replaces with the variable's value before SQL Server
/// sees it, so a literal or a quoted name holding one is not what is written. The one definition of its form, which reading a
/// script's own <c>:setvar</c> and inferring a body both use.
/// </summary>
/// <remarks>
/// Observed on 15 Nov 2026 (decision 0046): SqlPackage 170.5.96 publishing a DACPAC, and sqlcmd running a script, replace a
/// reference in a string literal and in a bracketed name, not in a comment; the value may be empty, and is put in as text, so
/// one holding a quotation mark ends the string. A name has no space, quotation mark, parenthesis or dollar sign, as sqlcmd's.
/// </remarks>
public static partial class SqlcmdVariableReference
{
    /// <summary>The first reference in <paramref name="text"/>, such as <c>$(Region)</c>; null when there is none.</summary>
    public static string? FirstIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains("$(", StringComparison.Ordinal) && Anywhere().Match(text) is { Success: true } found ? found.Value : null;
    }

    /// <summary>Whether <paramref name="text"/> holds a reference that a deployment may replace.</summary>
    public static bool IsIn(string text) => FirstIn(text) is not null;

    /// <summary>Whether <paramref name="literal"/> is a string literal holding a reference, so that its written text is not its value.</summary>
    public static bool IsIn(Literal literal) => literal is StringLiteral { Value: var text } && IsIn(text);

    /// <summary>The reference starting exactly at <paramref name="start"/> in <paramref name="text"/>, with the variable's name as the group <c>name</c>.</summary>
    public static Match At(string text, int start)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Anchored().Match(text, start);
    }

    [GeneratedRegex("""\$\((?<name>[^\s"()$]+)\)""", RegexOptions.CultureInvariant)]
    private static partial Regex Anywhere();

    [GeneratedRegex("""\G\$\((?<name>[^\s"()$]+)\)""", RegexOptions.CultureInvariant)]
    private static partial Regex Anchored();
}

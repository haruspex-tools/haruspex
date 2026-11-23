using System.Text;
using System.Text.RegularExpressions;
using Haruspex.SqlServer.Syntax;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// sqlcmd's scripting variables: the text a script sends to the server once each <c>:setvar</c> has defined a variable and
/// each <c>$(name)</c> has been replaced by its value, as sqlcmd and SSMS's SQLCMD mode do before the server sees a batch.
/// </summary>
/// <remarks>
/// <para>
/// An install script often names its database or a data folder this way (<c>CREATE DATABASE $(DatabaseName)</c>), and
/// without the replacement it doesn't parse. The rules are sqlcmd's, observed with sqlcmd 18.7 on 11 Nov 2026 (no test
/// runs sqlcmd; the cases are in <c>SqlcmdVariableTests</c>):
/// </para>
/// <list type="bullet">
/// <item><c>:setvar name "value"</c> or <c>:setvar name value</c> is a command only at the start of a line, spaces allowed
/// before it, and not inside a comment, a string or a quoted name. <c>:setvar name</c> with no value removes the
/// variable. The command and the names are matched without regard to case.</item>
/// <item><c>$(name)</c> is replaced in code, in strings and in quoted names, but not in comments. A name with no value is
/// left as written: sqlcmd reports it and sends the text unchanged, and here the parse reports it if it matters.</item>
/// <item>Block comments don't nest for sqlcmd, though they do for the server.</item>
/// </list>
/// <para>
/// Each command's line is kept, empty, so a line number in the result is the line in the file. A column after a replaced
/// variable on the same line is a column in the result. Other sqlcmd commands are left alone, so one such as <c>:r</c>
/// (include a file) fails to parse rather than being dropped, which would read a different script from the one that runs.
/// Variables defined outside the script (sqlcmd's <c>-v</c>, the environment, a project's own variables) are not known
/// here.
/// </para>
/// </remarks>
internal static partial class SqlcmdVariables
{
    /// <summary>The script as the server would receive it: commands removed and defined variables replaced.</summary>
    public static string Substitute(string script) => Substitute(script, Values(), null, null);

    /// <summary>A set of variables, matched without regard to case, as sqlcmd matches them.</summary>
    public static Dictionary<string, string> Values() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The script as the server would receive it, starting from the variables in <paramref name="values"/>, which it changes as
    /// each <c>:setvar</c> does: sqlcmd keeps one set for a script and every script it includes, so what is set before a
    /// <c>:r</c> is set in the included script, and what that sets stands after it.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="values">The variables set when the script starts; on return, those set when it ends.</param>
    /// <param name="atLine">Called with the offset of each line's start, before the line is read, with the values then set.</param>
    /// <param name="undefined">What a reference to a variable not set is replaced by; null leaves it as written.</param>
    public static string Substitute(string script, Dictionary<string, string> values, Action<int>? atLine, string? undefined)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(values);

        // Without a :setvar, a value or anything else to do there is nothing to replace, and the script is returned as it is.
        if (values.Count == 0 && atLine is null && undefined is null && !script.Contains(":setvar", StringComparison.OrdinalIgnoreCase))
            return script;

        var output = new StringBuilder(script.Length);
        var state = Lexical.Code;
        var closing = '\0';
        var i = 0;
        while (i < script.Length)
        {
            var atLineStart = i == 0 || script[i - 1] == '\n';
            if (atLineStart)
                atLine?.Invoke(i);
            if (atLineStart && state == Lexical.Code && Setvar(script, i) is { } command)
            {
                if (command.Value is { } value)
                    values[command.Name] = value;
                else
                    values.Remove(command.Name);

                // The line's text goes; its line break stays, so later lines keep their numbers.
                i = command.End;
                continue;
            }

            var c = script[i];
            var next = i + 1 < script.Length ? script[i + 1] : '\0';
            switch (state)
            {
                case Lexical.Code when c == '-' && next == '-':
                    state = Lexical.LineComment;
                    break;
                case Lexical.Code when c == '/' && next == '*':
                    output.Append("/*");
                    i += 2;
                    state = Lexical.BlockComment;
                    continue;
                case Lexical.Code when c is '\'' or '"' or '[':
                    state = Lexical.Quoted;
                    closing = c == '[' ? ']' : c;
                    break;
                case Lexical.Quoted when c == closing && next == closing:
                    // A doubled closing character is one character of the string or name, not its end.
                    output.Append(c).Append(next);
                    i += 2;
                    continue;
                case Lexical.Quoted when c == closing:
                case Lexical.LineComment when c == '\n':
                    state = Lexical.Code;
                    break;
                case Lexical.BlockComment when c == '*' && next == '/':
                    output.Append("*/");
                    i += 2;
                    state = Lexical.Code;
                    continue;
                case Lexical.Code or Lexical.Quoted when c == '$' && next == '(' && Reference(script, i, values, undefined) is { } reference:
                    output.Append(reference.Value);
                    i = reference.End;
                    continue;
                default:
                    break;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    /// <summary>The <c>:setvar</c> command on the line starting at <paramref name="start"/>, if it is one.</summary>
    private static (string Name, string? Value, int End)? Setvar(string script, int start)
    {
        var end = script.IndexOf('\n', start);
        if (end < 0)
            end = script.Length;
        var match = SetvarLine().Match(script, start, end - start);
        if (!match.Success)
            return null;
        var value = match.Groups["quoted"].Success ? match.Groups["quoted"].Value
            : match.Groups["bare"].Success ? match.Groups["bare"].Value
            : null;
        return (match.Groups["name"].Value, value, end);
    }

    /// <summary>
    /// The value of the <c>$(name)</c> at <paramref name="start"/>, and where it ends, when the variable is defined, or
    /// <paramref name="undefined"/> when it isn't and that is given.
    /// </summary>
    private static (string Value, int End)? Reference(string script, int start, Dictionary<string, string> values, string? undefined)
    {
        var match = SqlcmdVariableReference.At(script, start);
        if (!match.Success)
            return null;
        return values.TryGetValue(match.Groups["name"].Value, out var value) ? (value, start + match.Length)
            : undefined is not null ? (undefined, start + match.Length)
            : null;
    }

    /// <summary><paramref name="text"/> with each reference to a variable in <paramref name="values"/> replaced, as in a <c>:r</c> path.</summary>
    public static string Replaced(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        var output = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var match = text[i] == '$' ? SqlcmdVariableReference.At(text, i) : null;
            if (match is { Success: true } && values.TryGetValue(match.Groups["name"].Value, out var value))
            {
                output.Append(value);
                i += match.Length;
                continue;
            }

            output.Append(text[i]);
            i++;
        }

        return output.ToString();
    }

    // A name has no space, quotation mark or parenthesis. A quoted value has no quotation mark inside it.
    [GeneratedRegex("""^[ \t]*:setvar[ \t]+(?<name>[^\s"()$]+)(?:[ \t]+(?:"(?<quoted>[^"]*)"|(?<bare>[^\s"]+)))?[ \t]*\r?$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SetvarLine();

    /// <summary>Where in the script a character is, which decides whether a command or a variable there counts.</summary>
    private enum Lexical
    {
        Code,
        LineComment,
        BlockComment,
        Quoted,
    }
}

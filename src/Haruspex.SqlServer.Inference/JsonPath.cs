using System.Globalization;
using System.Text;
using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A path into JSON, as <c>JSON_VALUE</c>, <c>JSON_QUERY</c> and <c>OPENJSON</c> read it: lax or strict, then keys and array
/// indexes from <c>$</c>. Only the steps that each find one value are modelled; a wildcard or a range, which find several, are
/// not. README.md, "Paths into JSON", gives the grammar as it was observed.
/// </summary>
/// <param name="Strict">
/// Whether the path is <c>strict</c>, under which a step that finds nothing, or a value of the wrong kind, is an error rather
/// than NULL.
/// </param>
/// <param name="Steps">The steps from <c>$</c>.</param>
internal sealed record JsonPath(bool Strict, EquatableArray<JsonPath.Step> Steps)
{
    /// <summary>A step of a path: a key of an object, or an index into an array.</summary>
    internal abstract record Step
    {
        private protected Step()
        {
        }

        /// <summary>The step as a path writes it, for a note: <c>.city</c>, <c>."unit price"</c>, <c>[0]</c>.</summary>
        public abstract string Text { get; }
    }

    /// <summary>A key of an object, as the path names it once its quotes and escapes are read.</summary>
    internal sealed record Key(string Name) : Step
    {
        /// <inheritdoc/>
        public override string Text => IsIdentifier(Name) ? "." + Name : ".\"" + Name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>An index into an array, from 0.</summary>
    internal sealed record Index(int Position) : Step
    {
        /// <inheritdoc/>
        public override string Text => "[" + Position.ToString(CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>The path as far as its first <paramref name="count"/> steps, as a note names a place: <c>$.address.city</c>.</summary>
    public string Prefix(int count) => "$" + string.Concat(Steps.Take(count).Select(s => s.Text));

    /// <summary>
    /// Reads <paramref name="text"/> as SQL Server reads a path, or says why it is not read: a form SQL Server accepts but that is
    /// not modelled (a wildcard or a range), or one it refuses (error 13607), which no call with it survives.
    /// </summary>
    /// <remarks>
    /// Observed (DeclaredColumnObservationTests, "paths"): <c>lax</c> and <c>strict</c> are lower case only (<c>LAX $</c> is
    /// refused), with or without a space before <c>$</c>; blanks are allowed around the path and before a step, and inside an
    /// index's brackets; an unquoted key is ASCII letters, digits and <c>_</c>, not starting with a digit (<c>$.é</c>,
    /// <c>$.1a</c>, <c>$.$x</c> and <c>$.a-b</c> are refused); a quoted key may hold any character, <c>\"</c> for a quote; an
    /// index is decimal digits, leading zeros allowed. <c>[*]</c> and <c>[1 to 2]</c> are accepted and find several values, and
    /// <c>[last]</c> and <c>[0,1]</c> are refused (13660). Anything this reader is not sure of is not read: reading a path the
    /// server refuses would claim nothing wrong, since every call fails, but reading one it accepts into other steps would.
    /// </remarks>
    public static JsonPath? Parse(string text, out string why)
    {
        var s = text.Trim(' ');
        var strict = false;
        if (s.StartsWith("strict", StringComparison.Ordinal) && s.Length > 6 && s[6] is ' ' or '$')
            (strict, s) = (true, s[6..].TrimStart(' '));
        else if (s.StartsWith("lax", StringComparison.Ordinal) && s.Length > 3 && s[3] is ' ' or '$')
            s = s[3..].TrimStart(' ');
        if (s.Length == 0 || s[0] != '$')
            return Refused(out why);

        var steps = new List<Step>();
        var i = 1;
        while (i < s.Length)
        {
            if (s[i] == ' ')
            {
                i++;
            }
            else if (s[i] == '.' && i + 1 < s.Length && s[i + 1] == '"')
            {
                if (QuotedKey(s, i + 2) is not ({ } name, var end))
                    return NotRead(out why, "a quoted key with an escape other than \\\" is not read");
                steps.Add(new Key(name));
                i = end;
            }
            else if (s[i] == '.')
            {
                var start = ++i;
                while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '_'))
                    i++;
                if (i == start || char.IsAsciiDigit(s[start]))
                    return Refused(out why);
                steps.Add(new Key(s[start..i]));
            }
            else if (s[i] == '[')
            {
                var end = s.IndexOf(']', i);
                if (end < 0)
                    return Refused(out why);
                var inside = s[(i + 1)..end].Trim(' ');
                if (inside == "*" || inside.Contains(" to ", StringComparison.Ordinal))
                    return NotRead(out why, "a wildcard or a range finds several values, which is not modelled");
                if (inside.Length == 0 || !inside.All(char.IsAsciiDigit) || !int.TryParse(inside, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    return NotRead(out why, "an index that is not a number of at most ten digits is not read");
                steps.Add(new Index(index));
                i = end + 1;
            }
            else
            {
                return Refused(out why);
            }
        }

        why = "";
        return new JsonPath(strict, [.. steps]);

        static JsonPath? Refused(out string why) => NotRead(out why, "SQL Server refuses it (13607), or it is a form not read");

        static JsonPath? NotRead(out string why, string reason)
        {
            why = reason;
            return null;
        }
    }

    /// <summary>A quoted key's name, read from the character after its opening quote, and where the path goes on; null if not read.</summary>
    private static (string Name, int End)? QuotedKey(string s, int from)
    {
        var name = new StringBuilder();
        for (var i = from; i < s.Length; i++)
        {
            if (s[i] == '"')
                return (name.ToString(), i + 1);
            if (s[i] == '\\')
            {
                // Observed: \" is a quote. Other escapes (1 did not find the key a1) are not read.
                if (i + 1 >= s.Length || s[i + 1] != '"')
                    return null;
                i++;
            }

            name.Append(s[i]);
        }

        return null;
    }

    /// <summary>Whether a key can be written without quotes: ASCII letters, digits and <c>_</c>, not starting with a digit.</summary>
    private static bool IsIdentifier(string name) =>
        name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}

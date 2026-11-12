using System.Text;
using System.Text.RegularExpressions;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// The naming rules for one schema: how a database object's name becomes the name of its generated code. Rules, not
/// code, because haruspex is a .NET binary: a naming function would need Node at build time or an embedded JavaScript
/// engine. Rules are data, deterministic and the same in every emitter.
/// </summary>
/// <remarks>
/// <para>Applied in this order to the object's own name, never its schema:</para>
/// <list type="number">
/// <item><c>replace</c>: each regular expression in turn, replacing every match, so <c>#$</c> means the end of the
/// name. The top-level rules run first, then the schema's own (<see cref="For"/>).</item>
/// <item><c>prefix</c>: the schema's prefix, if it has one, goes in front, as words of its own.</item>
/// <item><c>case</c>: <c>pascal</c> and <c>camel</c> split the text into words at every character that is not an ASCII
/// letter or digit (<c>$</c>, <c>#</c>, <c>_</c>, spaces), and capitalise each word's first letter, keeping the rest as
/// written (<c>placedAt</c> stays <c>PlacedAt</c>). <c>camel</c> then lowers the first letter. A name that would start
/// with a digit gets <c>_</c> in front. <c>preserve</c> keeps the prefix and the name as they are, joined.</item>
/// </list>
/// <para><b>The schema is left out of names</b>: most projects use one schema or a few with distinct names, and
/// <c>DboGetOrder</c> everywhere helps nobody. Where two schemas' names meet, the clash is an error that asks for a
/// <c>prefix</c> on one schema or a <c>name</c> override, so a name never depends on which other objects exist
/// (docs/decisions/0011-generated-names-leave-the-schema-out.md).</para>
/// </remarks>
public sealed record NamingRules(IReadOnlyList<NamingReplacement> Replace, NamingCase Case, string Prefix = "")
{
    /// <summary>No replacements, no prefix, Pascal case.</summary>
    public static NamingRules Default { get; } = new([], NamingCase.Pascal);

    /// <summary>
    /// These rules with a schema's settings over them: its <c>case</c> replaces this one, its <c>replace</c> rules run
    /// after these, and its prefix is used.
    /// </summary>
    public NamingRules For(SchemaNaming? schema, string prefix) =>
        new([.. Replace, .. schema?.Replace ?? []], schema?.Case ?? Case, prefix);

    /// <summary>The generated name for an object, or why there is none.</summary>
    /// <param name="name">The object's name within its schema.</param>
    /// <param name="reserved">The words the generated code's language keeps, which the name must not be.</param>
    /// <param name="error">Why the rules give no valid name, when they don't.</param>
    public string? Name(string name, ReservedWords reserved, out string? error)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(reserved);
        var text = name;
        foreach (var rule in Replace)
        {
            try
            {
                text = rule.Pattern.Replace(text, rule.With);
            }
            catch (RegexMatchTimeoutException)
            {
                error = $"the replacement /{rule.Pattern}/ took too long on it.";
                return null;
            }
        }

        string result;
        if (Case == NamingCase.Preserve)
        {
            result = Prefix + text;
            if (!IrIdentifiers.IsIdentifier(result))
            {
                error = $"\"case\": \"preserve\" gives '{result}', which is not a valid identifier (an ASCII letter, _ or $, then letters, digits, _ or $). "
                    + "Add a replacement, or give it a name in \"procedures\".";
                return null;
            }
        }
        else
        {
            var words = Words(text);
            if (words.Count == 0)
            {
                error = $"'{text}' has no letters or digits to make a name from. Give it a name in \"procedures\".";
                return null;
            }

            result = Join([.. Words(Prefix), .. words], Case == NamingCase.Camel);
        }

        if (reserved.Contains(result))
        {
            error = $"the rules give '{result}', which is a reserved word in {reserved.Language}. Give it a name in \"procedures\".";
            return null;
        }

        error = null;
        return result;
    }

    /// <summary>
    /// A key as part of a nested type's name: Pascal case, whatever the case style, since it follows an object's name
    /// (<c>GetOrder</c> + <c>Line</c>, <c>getOrder</c> + <c>Line</c>). A key with no letters or digits is
    /// <c>Unnamed</c>.
    /// </summary>
    public static string Part(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var words = Words(key);
        return words.Count == 0 ? "Unnamed" : Join(words, camel: false);
    }

    /// <summary>The name for one item of a list: <c>Lines</c> → <c>Line</c>, <c>Data</c> → <c>DataItem</c>.</summary>
    public static string Singular(string pascal)
    {
        ArgumentNullException.ThrowIfNull(pascal);
        if (pascal.EndsWith("ies", StringComparison.Ordinal) && pascal.Length > 3)
            return pascal[..^3] + "y";
        if (pascal.EndsWith("sses", StringComparison.Ordinal) || pascal.EndsWith("xes", StringComparison.Ordinal)
            || pascal.EndsWith("ches", StringComparison.Ordinal) || pascal.EndsWith("shes", StringComparison.Ordinal)
            || pascal.EndsWith("uses", StringComparison.Ordinal))
        {
            return pascal[..^2];
        }

        if (pascal.EndsWith('s') && !pascal.EndsWith("ss", StringComparison.Ordinal) && pascal.Length > 1)
            return pascal[..^1];
        return pascal + "Item";
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
            words.Add(word.ToString());
        return words;
    }

    private static string Join(List<string> words, bool camel)
    {
        var result = new StringBuilder();
        foreach (var word in words)
            result.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1));
        if (camel)
            result[0] = char.ToLowerInvariant(result[0]);
        return char.IsAsciiDigit(result[0]) ? "_" + result : result.ToString();
    }
}

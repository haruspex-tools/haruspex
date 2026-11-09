using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Haruspex.Config;

/// <summary>
/// Selects objects of one schema by their own name: <c>get_order</c>, <c>list_*</c>, <c>get_?</c>. <c>*</c> matches any
/// run of characters and <c>?</c> any one; brackets make what they hold literal (<c>[a*b]</c>, <c>]]</c> for a
/// <c>]</c>), as in T-SQL. Everything else is literal, dots included, since the schema is the block's key and not part
/// of the pattern. Matching ignores case, as SQL Server's identifiers almost always do.
/// </summary>
public sealed class NamePattern
{
    private readonly Regex regex;

    private NamePattern(string text, Regex regex)
    {
        Text = text;
        this.regex = regex;
    }

    /// <summary>The pattern as written.</summary>
    public string Text { get; }

    /// <summary>Parses a pattern, or says why the text is not one.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out NamePattern? pattern, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        pattern = null;
        var body = new StringBuilder();
        var hasContent = false;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[':
                    var literal = new StringBuilder();
                    var j = i + 1;
                    for (; ; j++)
                    {
                        if (j >= text.Length)
                        {
                            error = $"'{text}' has a '[' without its closing ']'.";
                            return false;
                        }

                        if (text[j] != ']')
                        {
                            literal.Append(text[j]);
                        }
                        else if (j + 1 < text.Length && text[j + 1] == ']')
                        {
                            literal.Append(']');
                            j++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    body.Append(Regex.Escape(literal.ToString()));
                    hasContent |= literal.Length > 0;
                    i = j;
                    break;
                case '*':
                    body.Append(".*");
                    hasContent = true;
                    break;
                case '?':
                    body.Append('.');
                    hasContent = true;
                    break;
                default:
                    body.Append(Regex.Escape(text[i].ToString()));
                    hasContent = true;
                    break;
            }
        }

        if (!hasContent)
        {
            error = $"'{text}' is empty: a pattern names procedures of its schema, such as get_order, list_* or *.";
            return false;
        }

        // NonBacktracking: patterns come from users, so matching must stay linear whatever they write.
        pattern = new NamePattern(text, new Regex("^" + body + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking));
        error = null;
        return true;
    }

    /// <summary>Whether the object's own name matches, ignoring case.</summary>
    public bool Matches(string name) => regex.IsMatch(name);

    /// <summary>The pattern as written.</summary>
    public override string ToString() => Text;
}

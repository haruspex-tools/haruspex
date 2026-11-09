using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// Selects objects by two-part name: <c>sales.get_order</c>, <c>sales.*</c>, <c>*.list_?</c>. <c>*</c> and
/// <c>?</c> match within one part. Brackets quote a part as in T-SQL, because schema names with dots in them
/// exist (<c>[RDF.].GetPropertyList</c>); inside brackets <c>*</c> and <c>?</c> are literal. Matching is
/// case-insensitive, like the names themselves.
/// </summary>
public sealed class ObjectPattern
{
    private readonly Regex schema;
    private readonly Regex name;

    private ObjectPattern(string text, Regex schema, Regex name)
    {
        Text = text;
        this.schema = schema;
        this.name = name;
    }

    /// <summary>The pattern as given.</summary>
    public string Text { get; }

    /// <summary>Parses a pattern.</summary>
    /// <exception cref="FormatException">The text is not a two-part pattern.</exception>
    public static ObjectPattern Parse(string text) =>
        TryParse(text, out var pattern, out var error) ? pattern : throw new FormatException(error);

    /// <summary>Parses a pattern, or says why the text is not one.</summary>
    /// <param name="text">The pattern, such as <c>sales.*</c>.</param>
    /// <param name="pattern">The pattern, when the text is one.</param>
    /// <param name="error">Why the text is not a pattern, when it is not.</param>
    public static bool TryParse(string text, [NotNullWhen(true)] out ObjectPattern? pattern, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        pattern = null;
        var parts = new List<StringBuilder> { new() };
        var hasContent = new List<bool> { false };

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
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

                    parts[^1].Append(Regex.Escape(literal.ToString()));
                    hasContent[^1] |= literal.Length > 0;
                    i = j;
                    break;
                case '.':
                    parts.Add(new StringBuilder());
                    hasContent.Add(false);
                    break;
                case '*':
                    parts[^1].Append(".*");
                    hasContent[^1] = true;
                    break;
                case '?':
                    parts[^1].Append('.');
                    hasContent[^1] = true;
                    break;
                default:
                    parts[^1].Append(Regex.Escape(c.ToString()));
                    hasContent[^1] = true;
                    break;
            }
        }

        if (parts.Count != 2 || hasContent.Contains(false))
        {
            error = $"'{text}' is not a two-part name such as sales.get_order, sales.* or *.list_*.";
            return false;
        }

        error = null;
        pattern = new ObjectPattern(text, Anchored(parts[0]), Anchored(parts[1]));
        return true;
    }

    /// <summary>Whether the name matches the pattern, ignoring case.</summary>
    public bool Matches(ObjectName objectName) => schema.IsMatch(objectName.Schema) && name.IsMatch(objectName.Name);

    /// <summary>The pattern as given.</summary>
    public override string ToString() => Text;

    // NonBacktracking: patterns come from users, so matching must stay linear whatever they write.
    private static Regex Anchored(StringBuilder body) =>
        new("^" + body + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}

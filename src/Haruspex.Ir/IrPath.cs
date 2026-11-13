using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Haruspex.Ir;

/// <summary>
/// The path syntax of the IR, used by unknowns, by the identity of types and by the config file's overrides, so all
/// three write and read a place the same way: <c>results[0][].lines[].name</c>, <c>results[0].tags{}</c>,
/// <c>results[0].payload&lt;json&gt;.id</c>, <c>&lt;bom.part_tree&gt;.part</c>, and <c>["unit price"]</c> for a key that
/// is not an identifier (<see cref="IrIdentifiers"/>), its text a JSON string.
/// </summary>
public static class IrPath
{
    /// <summary>A key as a segment: <c>.key</c>, or <c>["key"]</c> with the key JSON-escaped.</summary>
    public static string Key(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return IrIdentifiers.IsIdentifier(key) ? "." + key : "[" + Quote(key) + "]";
    }

    /// <summary>Writes a path; a first key that is an identifier is written without its dot.</summary>
    public static string Render(IEnumerable<IrPathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var text = new StringBuilder();
        foreach (var segment in segments)
        {
            text.Append(segment switch
            {
                IrPathKey key when text.Length == 0 && IrIdentifiers.IsIdentifier(key.Key) => key.Key,
                IrPathKey key => Key(key.Key),
                IrPathItems => "[]",
                IrPathValues => "{}",
                IrPathIndex index => "[" + index.Index.ToString(CultureInfo.InvariantCulture) + "]",
                IrPathJson => "<json>",
                IrPathShared shared => "<" + shared.Owner + ">",
            });
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads a path written by <see cref="Render"/>, or says why the text is not one. The first key may be written with
    /// or without its dot. The empty text is the empty path.
    /// </summary>
    public static bool TryParse(string text, out IReadOnlyList<IrPathSegment> segments, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<IrPathSegment>();
        segments = result;
        error = null;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '.' || (i == 0 && IsIdentifierStart(c)))
            {
                var start = c == '.' ? i + 1 : i;
                var end = start;
                while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '_' or '$'))
                    end++;
                var key = text[start..end];
                if (!IrIdentifiers.IsIdentifier(key))
                    return Fail(text, i, "a '.' must be followed by a key; write a key that is not an identifier as [\"key\"]", out error);
                result.Add(new IrPathKey(key));
                i = end;
            }
            else if (c == '[' && i + 1 < text.Length && text[i + 1] == ']')
            {
                result.Add(new IrPathItems());
                i += 2;
            }
            else if (c == '[' && i + 1 < text.Length && text[i + 1] == '"')
            {
                if (!TryReadString(text, i + 1, out var key, out var end) || end >= text.Length || text[end] != ']')
                    return Fail(text, i, "[\" must be followed by a JSON string and ]", out error);
                result.Add(new IrPathKey(key));
                i = end + 1;
            }
            else if (c == '[')
            {
                var end = text.IndexOf(']', i);
                if (end < 0 || !int.TryParse(text.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    return Fail(text, i, "[ must be followed by ], a position such as 0, or a quoted key", out error);
                result.Add(new IrPathIndex(index));
                i = end + 1;
            }
            else if (c == '{' && i + 1 < text.Length && text[i + 1] == '}')
            {
                result.Add(new IrPathValues());
                i += 2;
            }
            else if (c == '<')
            {
                var end = text.IndexOf('>', i);
                if (end <= i + 1)
                    return Fail(text, i, "< must be followed by json or an object's name, and >", out error);
                var inner = text[(i + 1)..end];
                result.Add(inner == "json" ? new IrPathJson() : new IrPathShared(inner));
                i = end + 1;
            }
            else
            {
                return Fail(text, i, $"unexpected '{c}'", out error);
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c is '_' or '$';

    private static bool Fail(string text, int at, string why, out string? error)
    {
        error = $"'{text}' is not a path: at {at.ToString(CultureInfo.InvariantCulture)}, {why}.";
        return false;
    }

    /// <summary>Reads the JSON string starting at <paramref name="start"/> (its opening quote); <paramref name="end"/> is the index after it.</summary>
    private static bool TryReadString(string text, int start, out string value, out int end)
    {
        value = "";
        end = start + 1;
        while (end < text.Length && text[end] != '"')
            end += text[end] == '\\' ? 2 : 1;
        if (end >= text.Length)
            return false;
        end++;
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text[start..end]));
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                return false;
            value = reader.GetString() ?? "";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Quote(string key) => "\"" + JsonEncodedText.Encode(key, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
}

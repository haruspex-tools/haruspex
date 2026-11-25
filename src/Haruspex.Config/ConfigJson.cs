using System.Text;
using System.Text.Json;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

internal static class ConfigJson
{
    private static readonly JsonReaderOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    /// <summary>Reads UTF-8 text, with or without a byte order mark.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="file">The file it was read from, for problems.</param>
    /// <returns>The value; or, when the text is not JSON with comments or an object repeats a key, where and why.</returns>
    public static Outcome<JsonValue> Parse(byte[] utf8, string file)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        if (OtherEncoding(utf8) is { } encoding)
            return NotJson(file, $"The file is in {encoding}, not UTF-8; save it as UTF-8.", 1, 1);
        var start = utf8.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        var text = utf8.AsMemory(start);

        // The reader reports a text with no value in it by throwing, in its own terms ("when isFinalBlock is true"), so a file with
        // nothing in it but white space and comments is told apart first.
        if (text.Length == 0)
            return NotJson(file, "The file is empty; it must hold a JSON object.", 1, 1);
        if (!HasValue(text.Span))
            return NotJson(file, "The file holds only white space and comments; it must hold a JSON object.", 1, 1);

        var positions = new Positions(text);
        var reader = new Utf8JsonReader(text.Span, Options);
        try
        {
            // Never false here: the text has something besides white space and comments, which the reader reads or refuses.
            reader.Read();
            var value = Read(ref reader, positions, "", file);

            // The reader itself refuses anything after the value, with its position; this only guards that it does.
            if (value is Succeeded<JsonValue> && reader.Read())
            {
                var (line, column) = positions.Of(reader.TokenStartIndex);
                return NotJson(file, "Unexpected text after the end of the JSON value.", line, column);
            }

            return value;
        }
        catch (JsonException e)
        {
            // System.Text.Json reports text that isn't JSON by throwing; here, at the edge, it becomes a diagnostic. The reader
            // counts lines from 0 and positions in bytes; a message here counts both from 1, in characters.
            var line = (int)(e.LineNumber ?? 0);
            var column = positions.Column(line, (int)(e.BytePositionInLine ?? 0));
            return NotJson(file, Plain(e.Message), line + 1, column);
        }
    }

    /// <summary>
    /// The encoding the text is in when it is plainly not UTF-8, or null: UTF-32 or UTF-16 by a byte order mark, or UTF-16 by a zero
    /// byte first or second, which no UTF-8 text a JSON reader accepts has (a control character in JSON is written escaped). The
    /// JSON reader takes only UTF-8, and its account of such a file is of the first byte it can't start a value with.
    /// </summary>
    private static string? OtherEncoding(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]) || bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]) ? "UTF-32"
        : bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) ? "UTF-16"
        : bytes.Length >= 2 && bytes[0] == 0 != (bytes[1] == 0) ? "UTF-16"
        : null;

    /// <summary>
    /// Whether the text holds anything but white space (as JSON has it: space, tab, line feed and carriage return) and the comments
    /// JSON with comments allows. A comment left open counts as something, which the reader then reports where it starts.
    /// </summary>
    private static bool HasValue(ReadOnlySpan<byte> text)
    {
        var i = 0;
        while (i < text.Length)
        {
            switch (text[i])
            {
                case (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r':
                    i++;
                    break;
                case (byte)'/' when i + 1 < text.Length && text[i + 1] == (byte)'/':
                    var end = text[i..].IndexOf((byte)'\n');
                    i = end < 0 ? text.Length : i + end + 1;
                    break;
                case (byte)'/' when i + 1 < text.Length && text[i + 1] == (byte)'*':
                    var close = text[(i + 2)..].IndexOf("*/"u8);
                    if (close < 0)
                        return true;
                    i += 2 + close + 2;
                    break;
                default:
                    return true;
            }
        }

        return false;
    }

    private static Failed<JsonValue> NotJson(string file, string message, int line, int column) =>
        new([ConfigCodes.NotJson.At(new JsonLocation(file, "", line, column), message)]);

    private static Outcome<JsonValue> Read(ref Utf8JsonReader reader, Positions positions, string path, string file)
    {
        var (line, column) = positions.Of(reader.TokenStartIndex);
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                var members = new List<JsonMember>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var (keyLine, keyColumn) = positions.Of(reader.TokenStartIndex);
                    var name = reader.GetString()!; // Never null: a property name always has its text; GetString is null only for a JSON null.
                    var memberPath = ConfigPath.Child(path, name);
                    if (!seen.Add(name))
                        return new Failed<JsonValue>([ConfigCodes.KeyTwice.At(new JsonLocation(file, memberPath, keyLine, keyColumn), $"The key \"{name}\" is written twice in one object.")]);
                    reader.Read();
                    var member = Read(ref reader, positions, memberPath, file);
                    if (member is not Succeeded<JsonValue> { Value: var value })
                        return member;
                    members.Add(new JsonMember(name, value, keyLine, keyColumn));
                }

                return new Succeeded<JsonValue>(new JsonObjectValue([.. members], line, column));
            case JsonTokenType.StartArray:
                var items = new List<JsonValue>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var item = Read(ref reader, positions, ConfigPath.Index(path, items.Count), file);
                    if (item is not Succeeded<JsonValue> { Value: var value })
                        return item;
                    items.Add(value);
                }

                return new Succeeded<JsonValue>(new JsonArrayValue([.. items], line, column));
            case JsonTokenType.String:
                return new Succeeded<JsonValue>(new JsonStringValue(reader.GetString()!, line, column)); // Never null: the token is a string, not a JSON null.
            default:
                return new Succeeded<JsonValue>(new JsonOtherValue(reader.TokenType, Encoding.UTF8.GetString(reader.ValueSpan), line, column));
        }
    }

    /// <summary>The reader's message without the position it appends, which is given separately and counted from 1.</summary>
    private static string Plain(string message)
    {
        var at = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return at < 0 ? message : message[..at].TrimEnd();
    }

    /// <summary>Turns byte offsets into lines and columns counted from 1, columns in UTF-16 characters.</summary>
    private sealed class Positions
    {
        private readonly ReadOnlyMemory<byte> text;
        private readonly List<int> lineStarts = [0];

        public Positions(ReadOnlyMemory<byte> text)
        {
            this.text = text;
            var span = text.Span;
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] == (byte)'\n')
                    lineStarts.Add(i + 1);
            }
        }

        public (int Line, int Column) Of(long offset)
        {
            var index = lineStarts.BinarySearch((int)offset);
            var line = index >= 0 ? index : ~index - 1;
            return (line + 1, Column(line, (int)offset - lineStarts[line]));
        }

        /// <param name="line">Counted from 0.</param>
        /// <param name="bytes">Bytes into the line.</param>
        public int Column(int line, int bytes)
        {
            if (line >= lineStarts.Count)
                return bytes + 1;
            var start = lineStarts[line];
            var length = Math.Min(bytes, text.Length - start);
            return Encoding.UTF8.GetCharCount(text.Span.Slice(start, Math.Max(0, length))) + 1;
        }
    }
}

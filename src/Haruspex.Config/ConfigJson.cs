using System.Text;
using System.Text.Json;

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
    /// <exception cref="ConfigJsonException">The text is not JSON with comments, or an object repeats a key.</exception>
    public static JsonValue Parse(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        var start = utf8.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        var text = utf8.AsMemory(start);
        var positions = new Positions(text);
        var reader = new Utf8JsonReader(text.Span, Options);
        try
        {
            if (!reader.Read())
                throw new ConfigJsonException("The file is empty; it must hold a JSON object.", 1, 1);
            var value = Read(ref reader, positions, "");

            // The reader itself refuses anything after the value, with its position; this only guards that it does.
            if (reader.Read())
            {
                var (line, column) = positions.Of(reader.TokenStartIndex);
                throw new ConfigJsonException("Unexpected text after the end of the JSON value.", line, column);
            }

            return value;
        }
        catch (JsonException e)
        {
            // The reader counts lines from 0 and positions in bytes; a message here counts both from 1, in characters.
            var line = (int)(e.LineNumber ?? 0);
            var column = positions.Column(line, (int)(e.BytePositionInLine ?? 0));
            throw new ConfigJsonException(Plain(e.Message), line + 1, column);
        }
    }

    private static JsonValue Read(ref Utf8JsonReader reader, Positions positions, string path)
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
                    var name = reader.GetString()!;
                    var memberPath = ConfigPath.Child(path, name);
                    if (!seen.Add(name))
                        throw new ConfigJsonException($"The key \"{name}\" is written twice in one object.", keyLine, keyColumn, memberPath);
                    reader.Read();
                    members.Add(new JsonMember(name, Read(ref reader, positions, memberPath), keyLine, keyColumn));
                }

                return new JsonObjectValue([.. members], line, column);
            case JsonTokenType.StartArray:
                var items = new List<JsonValue>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    items.Add(Read(ref reader, positions, ConfigPath.Index(path, items.Count)));
                return new JsonArrayValue([.. items], line, column);
            case JsonTokenType.String:
                return new JsonStringValue(reader.GetString()!, line, column);
            default:
                return new JsonOtherValue(reader.TokenType, Encoding.UTF8.GetString(reader.ValueSpan), line, column);
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

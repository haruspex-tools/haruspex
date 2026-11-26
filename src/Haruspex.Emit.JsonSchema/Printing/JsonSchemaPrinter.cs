using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Emit.JsonSchema.Model;

namespace Haruspex.Emit.JsonSchema.Printing;

/// <summary>
/// The third stage: writes the model as JSON, the only stage that knows its syntax. The same model gives the same bytes on every
/// machine: keywords in one order (<see cref="Keyword.Order"/>), two spaces of indentation, LF line endings, a final newline, no
/// byte order mark, and characters escaped only where JSON requires it, as the IR's own file is written.
/// </summary>
internal static class JsonSchemaPrinter
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",

        // The file is data, never embedded in HTML, so escaping '<' or non-ASCII would only hurt readability.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Dictionary<string, int> Rank =
        Keyword.Order.Select((keyword, i) => (keyword, i)).ToDictionary(k => k.keyword, k => k.i, StringComparer.Ordinal);

    /// <summary>The document's text.</summary>
    public static string Print(JsonSchemaDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, Options))
        {
            json.WriteStartObject();
            json.WriteString(Keyword.Schema, Keyword.Draft);
            json.WriteString(Keyword.Description, string.Join("\n", document.Description));
            json.WriteStartObject(Keyword.Definitions);
            foreach (var definition in document.Definitions)
            {
                json.WritePropertyName(definition.Name);
                Write(json, definition.Schema);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void Write(Utf8JsonWriter json, Schema schema)
    {
        json.WriteStartObject();
        var keywords = schema.Keywords.Select(k => k.Key).Append(Keyword.Description).OrderBy(Order);
        foreach (var keyword in keywords)
        {
            if (keyword == Keyword.Description)
            {
                // The database's own description first, as it is, a paragraph of its own; then haruspex's notes.
                string?[] paragraphs = [schema.Described, schema.Notes.Count > 0 ? string.Join(" ", schema.Notes.Select(n => n.Text)) : null];
                if (paragraphs.Any(p => p is not null))
                    json.WriteString(Keyword.Description, string.Join("\n\n", paragraphs.OfType<string>()));
                continue;
            }

            json.WritePropertyName(keyword);
            Write(json, schema.Keywords[keyword]);
        }

        json.WriteEndObject();
    }

    private static int Order(string keyword) =>
        Rank.TryGetValue(keyword, out var rank) ? rank : throw new InvalidOperationException($"The keyword {keyword} has no place in the order.");

    private static void Write(Utf8JsonWriter json, SchemaValue value)
    {
        switch (value)
        {
            case SchemaValue.Text text:
                json.WriteStringValue(text.Value);
                break;

            // Exact decimal text, written as it is: a bound past what a double holds stays exact in the file.
            case SchemaValue.Number number:
                json.WriteRawValue(number.Value);
                break;
            case SchemaValue.Count count:
                json.WriteNumberValue(count.Value);
                break;
            case SchemaValue.Boolean boolean:
                json.WriteBooleanValue(boolean.Value);
                break;
            case SchemaValue.Null:
                json.WriteNullValue();
                break;
            case SchemaValue.List list:
                json.WriteStartArray();
                foreach (var item in list.Items)
                    Write(json, item);
                json.WriteEndArray();
                break;
            case SchemaValue.Nested nested:
                Write(json, nested.Schema);
                break;
            case SchemaValue.Properties properties:
                json.WriteStartObject();
                foreach (var property in properties.Items)
                {
                    json.WritePropertyName(property.Name);
                    Write(json, property.Schema);
                }

                json.WriteEndObject();
                break;
        }
    }
}

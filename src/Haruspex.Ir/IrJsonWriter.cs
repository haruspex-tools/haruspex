using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Haruspex.Ir;

/// <summary>
/// Writes an <see cref="IrDocument"/> as JSON that is the same byte for byte on every machine: keys in a
/// fixed order, collections sorted, LF line endings, no byte order mark and nothing that varies between runs
/// (no timestamps, no absolute paths). A CI check compares the file, so any variation would be a false alarm.
/// </summary>
/// <remarks>
/// Written by hand with <see cref="Utf8JsonWriter"/> rather than by a reflection serialiser, so that the key
/// order is the order in this file and the code stays trimming- and AOT-safe.
/// </remarks>
public static class IrJsonWriter
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        // The file is data, never embedded in HTML, so escaping '<' or non-ASCII would only hurt readability.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The document as the JSON text of an IR file: the same document always gives the same bytes.</summary>
    public static string Write(IrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            json.WriteStartObject();
            json.WriteString("irVersion", IrDocument.CurrentVersion);

            json.WriteStartObject("source");
            json.WriteString("dialect", document.Source.Dialect);
            json.WriteEndObject();

            json.WriteStartArray("objects");
            foreach (var obj in document.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
                WriteObject(json, obj);
            json.WriteEndArray();

            json.WriteStartObject("types");
            foreach (var (name, type) in document.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                json.WritePropertyName(name);
                WriteObjectType(json, type);
            }

            json.WriteEndObject();

            json.WriteStartArray("unknowns");
            foreach (var unknown in document.Unknowns
                         .OrderBy(u => u.Object, StringComparer.Ordinal)
                         .ThenBy(u => u.Path, StringComparer.Ordinal)
                         .ThenBy(u => u.At, StringComparer.Ordinal)
                         .ThenBy(u => IrWireNames.Of(u.Reason), StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("object", unknown.Object);
                json.WriteString("path", unknown.Path);
                json.WriteString("reason", IrWireNames.Of(unknown.Reason));
                json.WriteString("detail", unknown.Detail);
                json.WriteString("at", unknown.At);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("overrides");
            foreach (var claim in document.Overrides
                         .OrderBy(o => o.Object, StringComparer.Ordinal)
                         .ThenBy(o => o.Path, StringComparer.Ordinal)
                         .ThenBy(o => IrWireNames.Of(o.Setting), StringComparer.Ordinal)
                         .ThenBy(o => o.Via, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("object", claim.Object);
                json.WriteString("path", claim.Path);
                json.WriteString("setting", IrWireNames.Of(claim.Setting));
                if (claim.Via is not null)
                    json.WriteString("via", claim.Via);
                if (claim.Why is not null)
                    json.WriteString("why", claim.Why);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void WriteObject(Utf8JsonWriter json, IrObject obj)
    {
        json.WriteStartObject();
        json.WriteString("id", obj.Id);
        json.WriteString("name", obj.Name);
        json.WriteString("kind", IrWireNames.Of(obj.Kind));
        json.WriteString("at", obj.At);

        json.WriteStartArray("parameters");
        foreach (var parameter in obj.Parameters)
        {
            json.WriteStartObject();
            json.WriteString("name", parameter.Name);
            json.WritePropertyName("type");
            WriteType(json, parameter.Type);
            json.WriteBoolean("hasDefault", parameter.HasDefault);
            json.WriteBoolean("output", parameter.IsOutput);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        if (obj.Results is null)
        {
            // Null, not empty: an empty list would claim the object returns nothing.
            json.WriteNull("results");
        }
        else
        {
            json.WriteStartArray("results");
            foreach (var result in obj.Results)
            {
                json.WriteStartObject();
                json.WriteString("encoding", IrWireNames.Of(result.Encoding));
                json.WritePropertyName("type");
                WriteType(json, result.Type);
                json.WriteBoolean("mayBeAbsent", result.MayBeAbsent);
                if (result.MayBeAbsent)
                    WriteReasons(json, "absentBecause", result.AbsentBecause);
                json.WriteString("cardinality", IrWireNames.Of(result.Cardinality));
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteObjectType(Utf8JsonWriter json, IrObjectType type)
    {
        json.WriteStartObject();
        json.WriteString("kind", "object");

        // Written only when true, so a type that is not recursive reads as in 0.3.
        if (type.Recursive)
            json.WriteBoolean("recursive", true);
        if (type.Identity is { } identity)
        {
            json.WriteStartObject("identity");
            json.WriteString(IrWireNames.Of(identity.Owner), identity.Object);
            json.WriteString("path", identity.Path);
            json.WriteEndObject();
        }

        json.WriteStartArray("fields");
        foreach (var field in type.Fields)
        {
            json.WriteStartObject();
            json.WriteString("name", field.Name);
            json.WritePropertyName("type");
            WriteType(json, field.Type);
            json.WriteString("presence", IrWireNames.Of(field.Presence));
            json.WriteBoolean("nullable", field.Nullable);
            if (field.Nullable)
                WriteReasons(json, "nullableBecause", field.NullableBecause);

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteReasons(Utf8JsonWriter json, string name, EquatableArray<IrNullReason> reasons)
    {
        json.WriteStartArray(name);
        foreach (var reason in reasons)
        {
            json.WriteStartObject();
            json.WriteString("rule", IrWireNames.Of(reason.Rule));
            json.WriteString("at", reason.At);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteType(Utf8JsonWriter json, IrType type)
    {
        json.WriteStartObject();
        switch (type)
        {
            case IrScalar scalar:
                json.WriteString("kind", IrWireNames.Of(scalar.Kind));
                if (scalar.Format is { } format)
                    json.WriteString("format", IrWireNames.Of(format));
                if (scalar.Sql is not null)
                    json.WriteString("sql", scalar.Sql);
                if (scalar.Facets is { IsEmpty: false } facets)
                    WriteFacets(json, facets);
                break;
            case IrArray array:
                json.WriteString("kind", "array");
                json.WritePropertyName("items");
                WriteType(json, array.Items);

                // Written only when they say something, so an array FOR JSON builds reads as it did in 0.2.
                if (array.ItemsNullable)
                {
                    json.WriteBoolean("itemsNullable", true);
                    WriteReasons(json, "itemsNullableBecause", array.ItemsNullableBecause);
                }

                if (array.MinItems is { } minItems)
                    json.WriteNumber("minItems", minItems);
                if (array.MaxItems is { } maxItems)
                    json.WriteNumber("maxItems", maxItems);
                break;
            case IrTuple tuple:
                json.WriteString("kind", "tuple");
                json.WriteStartArray("items");
                foreach (var element in tuple.Items)
                {
                    json.WriteStartObject();
                    json.WritePropertyName("type");
                    WriteType(json, element.Type);
                    json.WriteBoolean("nullable", element.Nullable);
                    if (element.Nullable)
                        WriteReasons(json, "nullableBecause", element.NullableBecause);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                break;
            case IrRecord record:
                json.WriteString("kind", "record");
                json.WritePropertyName("values");
                WriteType(json, record.Values);
                json.WriteBoolean("valuesNullable", record.ValuesNullable);
                if (record.ValuesNullable)
                    WriteReasons(json, "valuesNullableBecause", record.ValuesNullableBecause);
                break;
            case IrUnion union:
                json.WriteString("kind", "union");
                json.WriteStartArray("variants");
                foreach (var variant in union.Variants)
                    WriteType(json, variant);
                json.WriteEndArray();
                break;
            case IrRef reference:
                json.WriteString("ref", reference.Name);
                break;
            case IrJsonText text:
                json.WriteString("kind", "json-text");
                if (text.Content is { } content)
                {
                    json.WritePropertyName("content");
                    WriteType(json, content);
                }

                break;
            case IrImport import:
                json.WriteString("kind", "import");
                json.WriteString("name", import.Name);
                json.WriteString("from", import.From);
                break;
            case IrUnknownType unknown:
                json.WriteString("kind", "unknown");
                json.WriteString("reason", IrWireNames.Of(unknown.Reason));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown IR type node.");
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// The facets, each only when known, in a fixed order. Numbers (bounds, numeric values) are written as JSON
    /// numbers from their exact decimal text, so a reader that parses them as decimals loses nothing; one that
    /// parses doubles rounds them, as it would any JSON number.
    /// </summary>
    private static void WriteFacets(Utf8JsonWriter json, IrFacets facets)
    {
        if (facets.Length is { } length)
        {
            json.WriteStartObject(length.IsFixed ? "length" : "maxLength");
            json.WriteNumber("value", length.Value);
            json.WriteString("unit", IrWireNames.Of(length.Unit));
            if (length.Encoding != IrByteEncoding.Unknown)
            {
                json.WriteString("encoding", IrWireNames.Of(length.Encoding));
            }

            WriteSource(json, length.From);
            json.WriteEndObject();
        }

        if (facets.MinLength is { } minLength)
        {
            json.WriteStartObject("minLength");
            json.WriteNumber("value", minLength.Value);
            WriteSource(json, minLength.From);
            json.WriteEndObject();
        }

        if (facets.Digits is { } digits)
        {
            json.WriteStartObject("digits");
            json.WriteNumber("precision", digits.Precision);
            json.WriteNumber("scale", digits.Scale);
            WriteSource(json, digits.From);
            json.WriteEndObject();
        }

        WriteBound(json, "min", facets.Min);
        WriteBound(json, "max", facets.Max);

        if (facets.Values is { } values)
        {
            json.WriteStartObject("values");
            json.WriteString("match", IrWireNames.Of(values.Match));
            json.WriteStartArray("items");
            foreach (var item in values.Items)
            {
                if (item.IsString)
                    json.WriteStringValue(item.Text);
                else
                    json.WriteRawValue(Number(item.Text));
            }

            json.WriteEndArray();
            json.WriteStartArray("from");
            foreach (var from in values.From)
                WriteSourceObject(json, from);
            json.WriteEndArray();
            json.WriteEndObject();
        }
    }

    private static void WriteBound(Utf8JsonWriter json, string name, IrBound? bound)
    {
        if (bound is null)
            return;
        json.WriteStartObject(name);
        json.WritePropertyName("value");
        json.WriteRawValue(Number(bound.Value));
        json.WriteBoolean("inclusive", bound.Inclusive);
        WriteSource(json, bound.From);
        json.WriteEndObject();
    }

    private static void WriteSource(Utf8JsonWriter json, IrFactSource from)
    {
        json.WritePropertyName("from");
        WriteSourceObject(json, from);
    }

    private static void WriteSourceObject(Utf8JsonWriter json, IrFactSource from)
    {
        json.WriteStartObject();
        json.WriteString(IrWireNames.Of(from.Kind), from.Ref);
        json.WriteEndObject();
    }

    /// <summary>Plain decimal text, checked, because it is written raw.</summary>
    private static string Number(string text)
    {
        var body = text.StartsWith('-') ? text[1..] : text;
        var parts = body.Split('.');
        var valid = parts.Length <= 2 && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))
            && (parts[0] == "0" || parts[0][0] != '0');
        return valid ? text : throw new ArgumentException($"'{text}' is not a plain decimal number.", nameof(text));
    }
}

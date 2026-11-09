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
                         .ThenBy(u => u.Reason, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("object", unknown.Object);
                json.WriteString("path", unknown.Path);
                json.WriteString("reason", unknown.Reason);
                json.WriteString("detail", unknown.Detail);
                json.WriteString("at", unknown.At);
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
        json.WriteString("kind", obj.Kind switch
        {
            IrObjectKind.Procedure => "procedure",
            _ => throw new ArgumentOutOfRangeException(nameof(obj), obj.Kind, "Unknown object kind."),
        });
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
                json.WriteString("encoding", result.Encoding switch
                {
                    IrEncoding.Rowset => "rowset",
                    IrEncoding.Json => "json",
                    _ => throw new ArgumentOutOfRangeException(nameof(obj), result.Encoding, "Unknown encoding."),
                });
                json.WritePropertyName("type");
                WriteType(json, result.Type);
                json.WriteBoolean("mayBeAbsent", result.MayBeAbsent);
                if (result.MayBeAbsent)
                    WriteReasons(json, "absentBecause", result.AbsentBecause);
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
        json.WriteStartArray("fields");
        foreach (var field in type.Fields)
        {
            json.WriteStartObject();
            json.WriteString("name", field.Name);
            json.WritePropertyName("type");
            WriteType(json, field.Type);
            json.WriteString("presence", field.Presence switch
            {
                IrPresence.Always => "always",
                IrPresence.OmittedWhenNull => "omittedWhenNull",
                _ => throw new ArgumentOutOfRangeException(nameof(type), field.Presence, "Unknown presence."),
            });
            json.WriteBoolean("nullable", field.Nullable);
            if (field.Nullable)
                WriteReasons(json, "nullableBecause", field.NullableBecause);

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteReasons(Utf8JsonWriter json, string name, IReadOnlyList<IrNullReason> reasons)
    {
        json.WriteStartArray(name);
        foreach (var reason in reasons)
        {
            json.WriteStartObject();
            json.WriteString("rule", reason.Rule);
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
                json.WriteString("kind", ScalarKindName(scalar.Kind));
                if (scalar.Format is not null)
                    json.WriteString("format", scalar.Format);
                json.WriteString("sql", scalar.Sql);
                break;
            case IrArray array:
                json.WriteString("kind", "array");
                json.WritePropertyName("items");
                WriteType(json, array.Items);
                break;
            case IrRef reference:
                json.WriteString("ref", reference.Name);
                break;
            case IrJsonText:
                json.WriteString("kind", "json-text");
                break;
            case IrUnknownType unknown:
                json.WriteString("kind", "unknown");
                json.WriteString("reason", unknown.Reason);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown IR type node.");
        }

        json.WriteEndObject();
    }

    /// <summary>The kind's name in the IR file.</summary>
    public static string ScalarKindName(IrScalarKind kind) => kind switch
    {
        IrScalarKind.String => "string",
        IrScalarKind.Number => "number",
        IrScalarKind.Boolean => "boolean",
        IrScalarKind.Date => "date",
        IrScalarKind.Time => "time",
        IrScalarKind.Datetime => "datetime",
        IrScalarKind.Uuid => "uuid",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown scalar kind."),
    };
}

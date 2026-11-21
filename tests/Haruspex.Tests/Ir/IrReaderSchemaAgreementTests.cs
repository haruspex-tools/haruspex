using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haruspex.Ir;
using Haruspex.Tests.Schemas;

namespace Haruspex.Tests.Ir;

/// <summary>
/// The reader and the published schema agree on every property of every object of the format: removing a property is
/// refused by the reader exactly when the schema refuses it, and adding one the format doesn't have is refused by both. Each
/// case is one edit to the file of <see cref="IrSamples.Declared"/>, which holds every property at least once, so a reader
/// that read a required property with a default, or an object the schema left open, fails here.
/// </summary>
/// <remarks>
/// When the reader refuses, it names the property: the one removed or added. Two kinds of object say themselves through a
/// key rather than a value (an identity, keyed by its owner's kind, and a fact source, keyed by its kind), and a reference
/// is the one type node without a kind; their refusals name the object instead, or the kind it then lacks.
/// </remarks>
public class IrReaderSchemaAgreementTests
{
    /// <summary>A key no object of the format has.</summary>
    private const string Stranger = "notInTheFormat";

    private static readonly string File = IrJsonWriter.Write(IrSamples.Declared);

    /// <summary>Every property of every object in the sample's file, as the JSON Pointer of the object and the key.</summary>
    public static TheoryData<string, string> Properties { get; } = [.. Objects(JsonNode.Parse(File)!, "").SelectMany(o => o.Node.AsObject().Select(p => (o.Location, p.Key)))];

    /// <summary>
    /// Every object in the sample's file, as its JSON Pointer, but a fold's <c>classes</c> and a source's <c>files</c>: their keys are
    /// names and paths, any string, so no key is one the format lacks.
    /// </summary>
    public static TheoryData<string> AllObjects { get; } =
        [.. Objects(JsonNode.Parse(File)!, "").Select(o => o.Location).Where(l => !l.EndsWith("/classes", StringComparison.Ordinal) && !l.EndsWith("/files", StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(Properties))]
    public void Removing_a_property_is_refused_by_the_reader_exactly_when_the_schema_refuses_it(string location, string key)
    {
        var json = JsonNode.Parse(File)!;
        var target = At(json, location).AsObject();
        target.Remove(key);
        var text = json.ToJsonString();

        var schemaErrors = SchemaErrors(text);
        var refusal = Refusal(text);

        // The things the reader refuses that the schema can't see: a type or a fold removed while something still names it.
        if (location == "/types" && File.Contains($"\"ref\": \"{key}\"", StringComparison.Ordinal))
        {
            Assert.Empty(schemaErrors);
            Assert.EndsWith($"which names no type in $.types.", refusal?.Problem, StringComparison.Ordinal);
            return;
        }

        if (location == "/folds" && File.Contains($"\"fold\": \"{key}\"", StringComparison.Ordinal))
        {
            Assert.Empty(schemaErrors);
            Assert.EndsWith($"which names no fold in $.folds.", refusal?.Problem, StringComparison.Ordinal);
            return;
        }

        // What ties a table-valued parameter to its rows and their type, which the schema can't follow from one place to another:
        // the type a parameter names, the name, and the row's identity; the table types as a whole, which a document of 0.27 may
        // leave out when no parameter names one; and a column that declares no type, which only a computed one may, its kind removed.
        if ((location == "/tableTypes" && File.Contains($"\"tableType\": \"{key}\"", StringComparison.Ordinal))
            || (location.Length == 0 && key == "tableTypes")
            || (key == "tableType" && location.Contains("/parameters/", StringComparison.Ordinal))
            || (key == "identity" && At(JsonNode.Parse(File)!, location)["identity"]?["tableType"] is not null)
            || (key == "generated" && location.StartsWith("/tableTypes/", StringComparison.Ordinal) && target["sql"] is null))
        {
            Assert.DoesNotContain(schemaErrors, e => !e.Contains("tableTypes", StringComparison.Ordinal));
            Assert.NotNull(refusal);
            return;
        }

        // What ties a claim to the declared column it rests on, and a declaration to the hash of its own file (0.28), which the schema
        // can't follow from one place to another: the claim's column, the declarations as a whole, and the file's own hash.
        if ((key == "via" && target["setting"]?.GetValue<string>() == "column-json")
            || (location.Length == 0 && key == "columns")
            || (location.StartsWith("/columns/", StringComparison.Ordinal) && location.EndsWith("/files", StringComparison.Ordinal) && At(JsonNode.Parse(File)!, location[..^"/files".Length])["schema"]?.GetValue<string>() == key))
        {
            Assert.Empty(schemaErrors);
            Assert.NotNull(refusal);
            return;
        }

        // An object's sets are named all or none, so a name taken from one set is refused as its object's list of sets.
        if (key == "name" && location.Contains("/results/", StringComparison.Ordinal))
        {
            Assert.NotEmpty(schemaErrors);
            Assert.Equal(ReaderPath(location[..location.LastIndexOf('/')]), refusal?.Path);
            return;
        }

        // A combined document's fields say how each database gave them (0.25), which a document of one database can't: without its
        // baseline, the first field's sides are refused.
        if (location == "/source" && key == "baseline")
        {
            Assert.NotEmpty(schemaErrors);
            Assert.Equal("$.types.FindOrdersRow.fields[0].sides", refusal?.Path);
            return;
        }

        // Sides that name neither database, or a source's files with none left, are refused as a whole.
        if ((location.EndsWith("/sides", StringComparison.Ordinal) || location.EndsWith("/files", StringComparison.Ordinal)) && target.Count == 0)
        {
            Assert.NotEmpty(schemaErrors);
            Assert.Equal(ReaderPath(location), refusal?.Path);
            return;
        }

        if (schemaErrors.Count == 0)
        {
            Assert.True(refusal is null, $"The schema accepts the file without {location}/{key}, but the reader refuses it: {refusal?.Message}");
            return;
        }

        Assert.True(refusal is not null, $"The schema refuses the file without {location}/{key} ({string.Join("; ", schemaErrors)}), but the reader reads it.");
        var path = ReaderPath(location);
        Assert.Equal(ExpectedPathAfterRemoving(target, path, key), refusal.Path);
    }

    [Theory]
    [MemberData(nameof(AllObjects))]
    public void A_property_the_format_lacks_is_refused_by_the_reader_and_the_schema(string location)
    {
        var json = JsonNode.Parse(File)!;
        var target = At(json, location).AsObject();
        var keyed = IsKeyedByKind(target);
        target.Add(Stranger, "x");
        var text = json.ToJsonString();

        Assert.NotEmpty(SchemaErrors(text));
        var refusal = Refusal(text);
        Assert.NotNull(refusal);
        var path = ReaderPath(location);
        Assert.Equal(keyed ? path : path + IrPath.Key(Stranger), refusal.Path);
    }

    [Fact]
    public void The_sample_holds_every_key_the_schema_lists()
    {
        using var schema = JsonDocument.Parse(IrJsonSchema.Write());
        var listed = PropertyNames(schema.RootElement).ToHashSet(StringComparer.Ordinal);
        var written = Objects(JsonNode.Parse(File)!, "")
            .Where(o => !o.Location.EndsWith("/types", StringComparison.Ordinal))
            .SelectMany(o => o.Node.AsObject().Select(p => p.Key))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(listed.Except(written).Order(StringComparer.Ordinal));
    }

    /// <summary>The names under every <c>properties</c> in the schema: every key an object of the format can have.</summary>
    private static IEnumerable<string> PropertyNames(JsonElement schema) =>
        schema.ValueKind != JsonValueKind.Object
            ? schema.ValueKind == JsonValueKind.Array ? schema.EnumerateArray().SelectMany(PropertyNames) : []
            : schema.EnumerateObject().SelectMany(p => p.Name == "properties" && p.Value.ValueKind == JsonValueKind.Object
                ? p.Value.EnumerateObject().Where(k => k.Value.ValueKind == JsonValueKind.Object).Select(k => k.Name).Concat(p.Value.EnumerateObject().SelectMany(k => PropertyNames(k.Value)))
                : PropertyNames(p.Value));

    /// <summary>
    /// The path the reader names when <paramref name="key"/> is removed from the object at <paramref name="path"/>. Usually the
    /// key's own. An identity without its owner, or a fact source without its one key, names itself; a reference without its <c>ref</c> lacks a kind; and an array
    /// without <c>itemsNullable</c>, the one flag the writer leaves out when false, reads as never null and so names its reasons.
    /// </summary>
    private static string ExpectedPathAfterRemoving(JsonObject without, string path, string key) =>
        key switch
        {
            _ when IrWireNames.TryParse(key, out IrTypeOwner _) && without.ContainsKey("path") => path,
            _ when IrWireNames.TryParse(key, out IrFactSourceKind _) && without.Count == 0 => path,
            "ref" => path + IrPath.Key("kind"),
            "itemsNullable" => path + IrPath.Key("itemsNullableBecause"),
            _ => path + IrPath.Key(key),
        };

    /// <summary>An identity (its owner's kind as a key, beside <c>path</c>) or a fact source (one key, its kind).</summary>
    private static bool IsKeyedByKind(JsonObject node) =>
        (node.Count == 1 && node.Any(p => IrWireNames.TryParse(p.Key, out IrFactSourceKind _) && p.Value is System.Text.Json.Nodes.JsonValue))
        || (node.ContainsKey("path") && node.Any(p => IrWireNames.TryParse(p.Key, out IrTypeOwner _)));

    private static List<string> SchemaErrors(string text)
    {
        using var schema = JsonDocument.Parse(IrJsonSchema.Write());
        using var instance = JsonDocument.Parse(text);
        return new SchemaSubset(schema.RootElement.Clone()).Validate(instance.RootElement);
    }

    private static IrFormatException? Refusal(string text)
    {
        try
        {
            IrJsonReader.Read(text);
            return null;
        }
        catch (IrFormatException e)
        {
            return e;
        }
    }

    /// <summary>Every object in the tree, with its JSON Location.</summary>
    private static IEnumerable<(string Location, JsonNode Node)> Objects(JsonNode node, string location) => node switch
    {
        JsonObject obj => [(location, obj), .. obj.SelectMany(p => p.Value is null ? [] : Objects(p.Value, location + "/" + Escape(p.Key)))],
        JsonArray array => array.SelectMany((item, i) => item is null ? [] : Objects(item, location + "/" + i.ToString(CultureInfo.InvariantCulture))),
        _ => [],
    };

    private static JsonNode At(JsonNode root, string location) =>
        location.Length == 0
            ? root
            : location[1..].Split('/').Select(Unescape).Aggregate(root, (node, part) => node is JsonArray array ? array[int.Parse(part, CultureInfo.InvariantCulture)]! : node[part]!);

    /// <summary>The reader's JSONPath for a JSON Location: <c>/objects/0/results</c> is <c>$.objects[0].results</c>.</summary>
    private static string ReaderPath(string location) =>
        "$" + string.Concat(location.Length == 0 ? [] : location[1..].Split('/').Select(Unescape)
            .Select(part => part.Length > 0 && part.All(char.IsAsciiDigit) ? "[" + part + "]" : IrPath.Key(part)));

    private static string Escape(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string Unescape(string part) => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}

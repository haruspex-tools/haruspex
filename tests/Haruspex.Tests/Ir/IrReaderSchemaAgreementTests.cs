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
/// that read a required property with a default, or an object the schema left open, fails here. And they agree on values and
/// labels: each way of breaking a file in <see cref="Breakages"/> says which side refuses it, and a rule only the reader checks is
/// one the IR README lists, so a disagreement in either direction fails here.
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

    /// <summary>The rules only the reader checks, as the IR README's "Rules only the reader checks" names them.</summary>
    private const string NumberForm = "a number's form";
    private const string NamesNothing = "a name that names nothing";
    private const string NamedTwice = "a name given twice";
    private const string Label = "what a label says of the whole document";

    /// <summary>
    /// Ways to break a file of a sample, by a value or a label rather than a property: each says which side refuses it. The reader
    /// refuses every one; the schema refuses each whose rule is null, and the rest are the rules only the reader checks, by the name the
    /// IR README gives them. A rule JSON Schema could state that the schema left to the reader fails here, and so does one the schema
    /// states that the reader doesn't check.
    /// </summary>
    private static readonly Dictionary<string, (Func<string> Sample, Action<JsonObject> Break, string? OnlyTheReader)> Breakages = new(StringComparer.Ordinal)
    {
        ["another version"] = (Everything, json => json["irVersion"] = "0.14", null),
        ["a required property missing"] = (Everything, json => Result(json).Remove("cardinality"), null),
        ["a flag that is not a boolean"] = (Everything, json => json["objects"]![0]!["parameters"]![0]!["hasDefault"] = "no", null),
        ["an unknown property"] = (Everything, json => FirstField(json)["colour"] = "red", null),
        ["an unknown null rule"] = (Everything, json => FirstField(json)["nullableBecause"]![0]!["rule"] = "outer-applied", null),
        ["an unknown type kind"] = (Everything, json => FirstField(json)["type"]!["kind"] = "money", null),
        ["a named type that is not an object"] = (Everything, json => json["types"]!["OrderRow"]!["kind"] = "record", null),
        ["nullable without reasons"] = (Everything, json => FirstField(json).Remove("nullableBecause"), null),
        ["reasons for a value never null"] = (Everything, json => FirstField(json)["nullable"] = false, null),
        ["an empty list of reasons"] = (Everything, json => FirstField(json)["nullableBecause"] = new JsonArray(), null),
        ["a fact source of an unknown kind"] = (Everything, json => FirstField(json)["type"]!["maxLength"]!["from"] = new JsonObject { ["guess"] = "x" }, null),
        ["a fact with two sources"] = (Everything, json => FirstField(json)["type"]!["maxLength"]!["from"]!["check"] = "ck", null),
        ["a fixed length and a maximum"] = (Everything, json => FirstField(json)["type"]!["length"] = FirstField(json)["type"]!["maxLength"]!.DeepClone(), null),
        ["a union of one variant"] = (Everything, json => Field(json, "OrderRow", 8)["type"]!["variants"]!.AsArray().RemoveAt(1), null),
        ["a canonical set without its fold"] = (Everything, json => Field(json, "OrderRow", 2)["type"]!["values"]!.AsObject().Remove("fold"), null),
        ["a fold on a set that is not canonical"] = (Everything, json => Field(json, "OrderRow", 2)["type"]!["values"]!["match"] = "exact", null),
        ["an identity with two owners"] = (Everything, json => json["types"]!["OrderRow"]!["identity"]!["function"] = "sales.line_tree", null),
        ["results that are neither listed nor null"] = (Everything, json => json["objects"]![0]!["results"] = "none", null),
        ["an absent result without reasons"] = (Everything, json => Result(json).Remove("absentBecause"), null),
        ["sets named some but not all"] = (Everything, json => Result(json).Remove("name"), null),
        ["a set named output"] = (Everything, json => Result(json)["name"] = "output", null),
        ["a set named returnValue"] = (Everything, json => Result(json)["name"] = "returnValue", null),
        ["a negative count"] = (Everything, json => FirstField(json)["type"]!["maxLength"]!["value"] = -1, null),
        ["a count past 32 bits"] = (Everything, json => Field(json, "OrderRow", 3)["type"]!["maxItems"] = 4294967296, null),
        ["a rowset typed as a string"] = (Everything, json => Rowset(json)["type"] = new JsonObject { ["kind"] = "string" }, null),
        ["a rowset of strings"] = (Everything, json => Rowset(json)["type"]!["items"] = new JsonObject { ["kind"] = "string" }, null),
        ["a package's hash that is not one"] = (Everything, json => json["source"]!["sha256"] = "not-a-hash", null),
        ["a reference's hash that is not one"] = (Everything, json => json["source"]!["references"]![1]!["sha256"] = "zzz", null),
        ["a fold's class of no characters"] = (Everything, json => json["folds"]!.AsObject().Single().Value!["classes"]!["A"] = "", null),

        // What a document of 0.23, which says nothing of where values came from and combines nothing, can't say.
        ["0.23: a field's source columns"] = (Everything, json => FirstField(json)["from"] = SourceColumns(), null),
        ["0.23: a field's sides"] = (Everything, json => FirstField(json)["sides"] = Sides(), null),
        ["0.23: a source's path"] = (Everything, json => json["source"]!["path"] = "database", null),
        ["0.23: a source's files"] = (Everything, json => json["source"]!["files"] = new JsonObject { ["a.sql"] = Hash }, null),
        ["0.23: a key left out whatever its value"] = (Everything, json => FirstField(json)["presence"] = "optional", null),
        ["0.23: a field null because not in both"] = (Everything, json => FirstField(json)["nullableBecause"]![0]!["rule"] = "not-in-both", null),
        ["0.23: a result absent because not in both"] = (Everything, json => Result(json)["absentBecause"]![0]!["rule"] = "not-in-both", null),
        ["0.23: an open object"] = (Everything, json => json["types"]!["OrderRow"]!["open"] = true, null),
        ["0.23: a claim on a declared column"] = (Everything, json => json["overrides"]![0]!["setting"] = "column-json", null),

        // What a document of 0.24, which combines two databases and says nothing of where values came from, can't say.
        ["0.24: a field's source columns"] = (Combined, json => FirstField(json)["from"] = SourceColumns(), null),
        ["0.24: a field's sides"] = (Combined, json => FirstField(json)["sides"] = Sides(), null),
        ["0.24: a source's path"] = (Combined, json => json["source"]!["path"] = "database", null),
        ["0.24: a baseline's files"] = (Combined, json => json["source"]!["baseline"]!["files"] = new JsonObject { ["a.sql"] = Hash }, null),
        ["0.24: a document that combines nothing"] = (Combined, json => json["source"]!.AsObject().Remove("baseline"), null),
        ["0.24, said of a document of 0.25"] = (Derived, json => json["irVersion"] = "0.24", null),

        // A document of one database of 0.25, read from the committed file of that version.
        ["0.25 of one database: a field null because not in both"] = (() => VersionFile("0.25"), json => NullableField(json)["nullableBecause"]![0]!["rule"] = "not-in-both", null),
        ["0.27: an open object"] = (Binary, json => json["types"]!["OrderRow"]!["open"] = true, null),

        // A number's text, which JSON Schema never sees: a count of 99.0 or 1e1 is an integer to it.
        ["a count written 99.0"] = (Everything, json => FirstField(json)["type"]!["maxLength"]!["value"] = JsonNode.Parse("99.0"), NumberForm),
        ["a count written 1e1"] = (Everything, json => FirstField(json)["type"]!["maxLength"]!["value"] = JsonNode.Parse("1e1"), NumberForm),
        ["a bound written 1e5"] = (Everything, json => Field(json, "OrderRow", 3)["type"]!["max"]!["value"] = JsonNode.Parse("1e5"), NumberForm),

        // A name the schema can't look up, and names it can't compare.
        ["a reference to no type"] = (Everything, json => Field(json, "OrderRow", 4)["type"]!["items"]!["ref"] = "Nowhere", NamesNothing),
        ["a fold that names none"] = (Everything, json => Field(json, "OrderRow", 2)["type"]!["values"]!["fold"] = "nowhere", NamesNothing),
        ["two objects of one id"] = (Everything, json => json["objects"]!.AsArray().Add(json["objects"]![0]!.DeepClone()), NamedTwice),
        ["two fields of one name"] = (Everything, json => json["types"]!["OrderRow"]!["fields"]!.AsArray().Add(FirstField(json).DeepClone()), NamedTwice),
        ["two parameters of one name"] = (Everything, json => json["objects"]![0]!["parameters"]!.AsArray().Add(json["objects"]![0]!["parameters"]![0]!.DeepClone()), NamedTwice),

        // A label whose rule is about something anywhere in the document.
        ["0.25, said of a document of 0.23"] = (Everything, json => json["irVersion"] = "0.25", Label),
        ["0.25, said of a document of 0.24"] = (Combined, json => json["irVersion"] = "0.25", Label),
        ["0.26, said of a document of 0.27"] = (Binary, json => json["irVersion"] = "0.26", Label),
        ["0.28, said of a document of 0.27"] = (Binary, json => json["irVersion"] = "0.28", Label),
        ["0.29, said of a document of 0.28"] = (Declared, json => json["irVersion"] = "0.29", Label),
        ["0.28: a reason's detail"] = (Declared, json => FirstField(json)["nullableBecause"]![0]!["detail"] = "words", Label),

        // A binary value says only its length, in bytes.
        ["a binary value's length in code units"] = (Binary, json => Signature(json)["length"]!["unit"] = "utf16", null),
        ["a binary value's length with an encoding"] = (Binary, json => Signature(json)["length"]!["encoding"] = "single-byte", null),
        ["a binary value's minimum length"] = (Binary, json => Signature(json)["minLength"] = JsonNode.Parse("""{ "value": 1, "from": { "type": "binary(32)" } }"""), null),
        ["a binary value's format"] = (Binary, json => Signature(json)["format"] = "int32", null),
        ["a binary value's value set"] = (Binary, json => Signature(json)["values"] = JsonNode.Parse("""{ "items": ["AQ=="], "match": "exact", "from": [{ "type": "binary(32)" }] }"""), null),
    };

    public static TheoryData<string> BreakageNames { get; } = [.. Breakages.Keys];

    /// <summary>Every breakage is refused by the reader, and by the schema exactly when its case says JSON Schema can state the rule.</summary>
    [Theory]
    [MemberData(nameof(BreakageNames))]
    public void A_broken_file_is_refused_by_the_reader_and_by_the_schema_where_json_schema_can_say_so(string breakage)
    {
        var (_, _, onlyTheReader) = Breakages[breakage];
        var text = Broken(breakage);

        Assert.NotNull(Refusal(text));
        var schemaErrors = SchemaErrors(text);
        if (onlyTheReader is null)
            Assert.True(schemaErrors.Count > 0, $"The reader refuses \"{breakage}\", and JSON Schema can say why, but the schema accepts it.");
        else
            Assert.True(schemaErrors.Count == 0, $"The schema refuses \"{breakage}\" ({string.Join("; ", schemaErrors)}), which its case says is {onlyTheReader}, a rule only the reader checks.");
    }

    /// <summary>Each rule a case leaves to the reader is one the IR README lists as JSON Schema's limit, by the same name.</summary>
    [Fact]
    public void Each_rule_only_the_reader_checks_is_named_in_the_specification()
    {
        var specification = System.IO.File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "Haruspex.Ir", "README.md"));
        var section = specification[specification.IndexOf("**Rules only the reader checks.**", StringComparison.Ordinal)..];

        Assert.All(Breakages.Values.Select(b => b.OnlyTheReader).OfType<string>().Distinct(), rule => Assert.Contains($"- **{rule}**", section, StringComparison.Ordinal));
    }

    /// <summary>
    /// Ajv, a validator a consumer would use, compiled strictly, gives every breakage the verdict the tests' own validator gives, so a
    /// keyword <see cref="SchemaSubset"/> read differently from a real validator can't pass the schema and these tests both.
    /// </summary>
    [Fact]
    public void Ajv_gives_every_broken_file_the_verdict_the_schema_subset_gives()
    {
        var documents = Breakages.Keys.ToDictionary(name => name, Broken, StringComparer.Ordinal);

        var verdicts = AjvVerdicts.Of(Path.Combine(RepositoryPaths.Root, IrJsonSchema.PublishedPath), documents);

        Assert.All(documents, d =>
        {
            var subset = SchemaErrors(d.Value);
            Assert.True(subset.Count == 0 == (verdicts[d.Key].Count == 0), $"{d.Key}: Ajv says {Verdict(verdicts[d.Key])}, the schema subset {Verdict(subset)}.");
        });

        static string Verdict(List<string> errors) => errors.Count == 0 ? "valid" : string.Join("; ", errors);
    }

    private static string Broken(string breakage)
    {
        var (sample, change, _) = Breakages[breakage];
        var json = JsonNode.Parse(sample())!.AsObject();
        change(json);
        return json.ToJsonString();
    }

    private static string Everything() => IrJsonWriter.Write(IrSamples.Everything);

    private static string Combined() => IrJsonWriter.Write(IrSamples.Combined);

    private static string Derived() => IrJsonWriter.Write(IrSamples.Derived);

    private static string Binary() => IrJsonWriter.Write(IrSamples.Binary);

    private static string Declared() => IrJsonWriter.Write(IrSamples.Declared);

    private static string VersionFile(string version) => System.IO.File.ReadAllText(Path.Combine(RepositoryPaths.Root, "tests", "ir-versions", $"{version}.ir.json"));

    private static readonly string Hash = new('e', 64);

    private static JsonArray SourceColumns() => [new JsonObject { ["column"] = "sales.orders.id", ["at"] = "tables/orders.sql:2:5" }];

    private static JsonObject Sides() => new() { ["candidate"] = new JsonObject { ["presence"] = "always", ["nullable"] = false } };

    private static JsonObject FirstField(JsonObject json) => Field(json, "OrderRow", 0);

    private static JsonObject Field(JsonObject json, string type, int i) => json["types"]![type]!["fields"]![i]!.AsObject();

    private static JsonObject NullableField(JsonObject json) =>
        json["types"]!.AsObject().SelectMany(t => t.Value!["fields"]!.AsArray()).First(f => f!["nullableBecause"] is not null)!.AsObject();

    private static JsonObject Result(JsonObject json) => json["objects"]![0]!["results"]![0]!.AsObject();

    /// <summary>The first result whose encoding is <c>rowset</c>.</summary>
    private static JsonObject Rowset(JsonObject json) =>
        json["objects"]!.AsArray().SelectMany(o => o!["results"]!.AsArray()).First(r => (string?)r!["encoding"] == "rowset")!.AsObject();

    private static JsonObject Signature(JsonObject json) =>
        json["types"]!["OrderRow"]!["fields"]!.AsArray().Single(f => (string?)f!["name"] == "signature")!["type"]!.AsObject();

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

using System.Text.Json.Nodes;
using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of a column declared to hold JSON of a schema (0.28): the label it takes, the declarations
/// and the claims that rest on them, and any JSON. Why: docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md
/// and docs/decisions/0094-any-json-is-a-type-of-its-own.md.
/// </summary>
public partial class IrJsonReaderTests
{
    /// <summary>The declared sample's JSON with <paramref name="change"/> made to it, read: the refusal the reader gives.</summary>
    private static IrFormatException RefusedDeclared(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Declared))!.AsObject();
        change(json);
        return Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
    }

    private static JsonNode Field(JsonObject json, string name) =>
        json["types"]!["OrderRow"]!["fields"]!.AsArray().Single(f => (string?)f!["name"] == name)!;

    [Fact]
    public void A_document_that_declares_a_column_is_labelled_0_28_and_reads_back_equal()
    {
        var json = IrJsonWriter.Write(IrSamples.Declared);

        Assert.Contains("\"irVersion\": \"0.28\"", json, StringComparison.Ordinal);
        Assert.Equal(IrSamples.Declared, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    /// <summary>
    /// A map keyed by data may have keys the format uses elsewhere for its own objects: a schema file named <c>ref</c>, beside files named
    /// <c>match</c> and <c>fold</c>, and a script named <c>ref</c>, are files like any other, so the document reads back.
    /// </summary>
    [Fact]
    public void Files_named_ref_match_and_fold_read_back_as_files()
    {
        var hash = new string('c', 64);
        var document = IrSamples.Declared with
        {
            Source = IrSamples.Declared.Source with { Files = new Dictionary<string, string> { ["ref"] = hash }.ToEquatableMap(f => f.Key, f => f.Value) },
            Columns =
            [
                new IrColumnDeclaration(
                    "sales.orders.settings",
                    "ref",
                    null,
                    new Dictionary<string, string> { ["ref"] = hash, ["match"] = hash, ["fold"] = hash }.ToEquatableMap(f => f.Key, f => f.Value)),
            ],
        };
        var json = IrJsonWriter.Write(document);

        Assert.Equal(document, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_declaration_is_written_with_its_pointer_and_the_hash_of_every_file_it_read()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Declared))!.AsObject();

        Assert.Equal(
            $$$"""[{"column":"sales.orders.settings","schema":"schemas/order-settings.schema.json","pointer":"/$defs/settings","files":{"schemas/common.schema.json":"{{{new string('b', 64)}}}","schemas/order-settings.schema.json":"{{{new string('a', 64)}}}"}}]""",
            json["columns"]!.ToJsonString());
    }

    [Fact]
    public void Any_json_is_written_as_its_own_kind_narrowed_to_an_object_or_array_only_when_it_is()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Declared))!.AsObject();

        Assert.Equal("""{"kind":"json-text","content":{"kind":"any-json"}}""", Field(json, "extras")["type"]!.ToJsonString());
        Assert.Equal("""{"kind":"any-json","objectOrArray":true}""", Field(json, "live")["type"]!.ToJsonString());
    }

    [Fact]
    public void A_document_that_declares_nothing_keeps_the_label_the_rest_of_it_earns()
    {
        Assert.Equal("0.27", IrSamples.Binary.Version);
        Assert.Equal("0.28", (IrSamples.Binary with { Columns = IrSamples.Declared.Columns }).Version);
        Assert.Equal("0.28", (IrSamples.Everything with { Types = IrSamples.Declared.Types }).Version);
    }

    [Fact]
    public void Declarations_in_a_document_labelled_0_27_are_refused()
    {
        var e = RefusedDeclared(json =>
        {
            json["irVersion"] = IrDocument.BytesVersion;
            Field(json, "extras")["type"] = JsonNode.Parse("""{ "kind": "json-text" }""");
            Field(json, "live")["type"] = JsonNode.Parse("""{ "kind": "string" }""");
            Field(json, "mode")["type"] = JsonNode.Parse("""{ "kind": "string" }""");
            Field(json, "mode")["presence"] = "always";
        });

        Assert.Equal("$.columns is given in a document of IR 0.27: a document that declares a column's JSON is IR 0.28 or later.", e.Message);
    }

    [Fact]
    public void Any_json_in_a_document_labelled_before_0_28_is_refused_by_its_path()
    {
        var e = RefusedDeclared(json =>
        {
            json["irVersion"] = IrDocument.BytesVersion;
            json.Remove("columns");
            json["overrides"] = new JsonArray();
            json["types"]!["OrderRow"]!.AsObject().Remove("open");
            Field(json, "mode")["type"] = JsonNode.Parse("""{ "kind": "string" }""");
            Field(json, "mode")["presence"] = "always";
        });

        Assert.Equal("$.types.OrderRow.fields[15].type.content", e.Path);
        Assert.Contains("any JSON (kind \"any-json\"), given in a document of IR 0.27", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fact_a_declaration_states_in_a_document_labelled_before_0_28_is_refused_by_its_path()
    {
        var e = RefusedDeclared(json =>
        {
            json["irVersion"] = IrDocument.BytesVersion;
            json.Remove("columns");
            json["overrides"] = new JsonArray();
            json["types"]!["OrderRow"]!.AsObject().Remove("open");
            Field(json, "extras")["type"] = JsonNode.Parse("""{ "kind": "json-text" }""");
            Field(json, "live")["type"] = JsonNode.Parse("""{ "kind": "string" }""");
        });

        Assert.Equal("$.types.OrderRow.fields[17].type.minLength", e.Path);
    }

    [Fact]
    public void An_open_object_in_a_document_labelled_before_0_28_is_refused_by_its_path()
    {
        var e = RefusedDeclared(json =>
        {
            json["irVersion"] = IrDocument.BytesVersion;
            json.Remove("columns");
            json["overrides"] = new JsonArray();
            Field(json, "extras")["type"] = JsonNode.Parse("""{ "kind": "json-text" }""");
            Field(json, "live")["type"] = JsonNode.Parse("""{ "kind": "string" }""");
            Field(json, "mode")["type"] = JsonNode.Parse("""{ "kind": "string" }""");
            Field(json, "mode")["presence"] = "always";
        });

        Assert.Equal("$.types.OrderRow.open", e.Path);
    }

    [Fact]
    public void A_document_labelled_0_28_that_declares_nothing_is_refused()
    {
        var e = RefusedBinary(json => json["irVersion"] = IrDocument.DeclaredVersion);

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("declares none", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_claim_on_a_declared_column_the_document_does_not_declare_is_refused()
    {
        var e = RefusedDeclared(json => json["overrides"]!.AsArray().Single(o => (string?)o!["setting"] == "column-json")!["via"] = "sales.orders.notes");

        Assert.Equal("$.overrides[0].via", e.Path);
    }

    [Fact]
    public void A_column_declared_twice_is_refused()
    {
        var e = RefusedDeclared(json => json["columns"]!.AsArray().Add(json["columns"]![0]!.DeepClone()));

        Assert.Equal("$.columns[1].column", e.Path);
    }

    [Fact]
    public void A_declaration_without_the_hash_of_its_own_schema_file_is_refused()
    {
        var e = RefusedDeclared(json => json["columns"]![0]!["files"]!.AsObject().Remove("schemas/order-settings.schema.json"));

        Assert.Equal("$.columns[0].files", e.Path);
    }

    [Fact]
    public void An_empty_list_of_declarations_is_refused()
    {
        var e = RefusedDeclared(json => json["columns"] = new JsonArray());

        Assert.Equal("$.columns", e.Path);
    }

    [Fact]
    public void A_key_left_out_whatever_its_value_is_read_in_a_document_of_one_database_that_declares_a_column()
    {
        var oneDatabase = IrSamples.Everything with
        {
            Types = IrSamples.Everything.Types.ToDictionary(t => t.Key, t => t.Key != "FindOrdersRow" ? t.Value : t.Value with
            {
                Fields = [new IrField("note", new IrAnyJson(), IrPresence.Optional, [])],
            }),
            Columns = IrSamples.Declared.Columns,
        };

        Assert.Equal("0.28", oneDatabase.Version);
        Assert.Equal(oneDatabase, IrJsonReader.Read(IrJsonWriter.Write(oneDatabase)));
    }
}

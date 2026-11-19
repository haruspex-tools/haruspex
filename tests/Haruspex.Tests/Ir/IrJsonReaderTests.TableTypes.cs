using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>What the reader refuses of a document's table types (0.26), which JSON Schema can't see: what ties a parameter to its type.</summary>
public partial class IrJsonReaderTests
{
    /// <summary>The whole sample's JSON with <paramref name="change"/> made to it, read: the refusal the reader gives.</summary>
    private static IrFormatException RefusedWhole(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Whole))!.AsObject();
        change(json);
        return Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
    }

    private static JsonObject Lines(JsonObject json) =>
        json["objects"]![0]!["parameters"]!.AsArray().Single(p => (string?)p!["name"] == "@lines")!.AsObject();

    [Fact]
    public void A_table_type_a_parameter_names_that_the_document_lacks_is_refused()
    {
        var e = RefusedWhole(json => Lines(json)["tableType"] = "sales.gone");

        Assert.Equal("$.objects[0].parameters[2].tableType is \"sales.gone\", which names no table type in $.tableTypes.", e.Message);
    }

    [Fact]
    public void A_parameter_that_refers_to_a_named_type_other_than_its_table_type_s_rows_is_refused()
    {
        var e = RefusedWhole(json => Lines(json)["type"] = JsonNode.Parse("""{ "kind": "array", "items": { "ref": "LineRow" } }"""));

        Assert.Equal("$.objects[0].parameters[2].type", e.Path);
    }

    [Fact]
    public void A_table_type_no_parameter_takes_is_refused()
    {
        var e = RefusedWhole(json => json["tableTypes"]!["sales.spare"] = JsonNode.Parse(json["tableTypes"]!["sales.line_list"]!.ToJsonString()));

        Assert.Equal("$.tableTypes[\"sales.spare\"] is a table type no parameter takes; a document defines only those its parameters take.", e.Message);
    }

    [Fact]
    public void Table_types_in_a_document_labelled_before_0_25_are_refused()
    {
        var e = RefusedWhole(json => json["irVersion"] = IrDocument.CombinedVersion);

        Assert.Equal("$.tableTypes is given in a document of IR 0.24: a document that defines a table type is IR 0.26.", e.Message);
    }

    [Fact]
    public void A_document_labelled_0_25_without_table_types_is_refused()
    {
        var e = Refused(json => json["irVersion"] = IrDocument.CurrentVersion);

        Assert.Equal("$.tableTypes", e.Path);
    }

    [Fact]
    public void A_table_type_s_row_whose_column_is_not_a_scalar_is_refused()
    {
        var e = RefusedWhole(json => json["types"]!["LineListRow"]!["fields"]![0]!["type"] = JsonNode.Parse("""{ "kind": "json-text" }"""));

        Assert.Equal("$.types.LineListRow.fields[0].type", e.Path);
    }
}

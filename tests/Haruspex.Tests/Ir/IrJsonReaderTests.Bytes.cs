using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of a binary value (0.27): the label it takes, and the facets a binary value can't have. Why:
/// docs/decisions/0081-binary-values-are-a-kind-of-their-own-counted-in-bytes.md.
/// </summary>
public partial class IrJsonReaderTests
{
    /// <summary>The binary sample's JSON with <paramref name="change"/> made to it, read: the refusal the reader gives.</summary>
    private static IrFormatException RefusedBinary(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Binary))!.AsObject();
        change(json);
        return Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
    }

    /// <summary>The type of the sample's field <c>signature</c>, a <c>binary(32)</c>.</summary>
    private static JsonObject Signature(JsonObject json) =>
        json["types"]!["OrderRow"]!["fields"]!.AsArray().Single(f => (string?)f!["name"] == "signature")!["type"]!.AsObject();

    [Fact]
    public void A_document_with_binary_values_is_labelled_0_27_and_reads_back_equal()
    {
        var json = IrJsonWriter.Write(IrSamples.Binary);

        Assert.Contains("\"irVersion\": \"0.27\"", json, StringComparison.Ordinal);
        Assert.Equal(IrSamples.Binary, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_binary_value_s_length_is_written_in_bytes_with_no_encoding()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Binary))!.AsObject();

        Assert.Equal(
            """{"kind":"bytes","sql":"binary(32)","length":{"value":32,"unit":"bytes","from":{"type":"binary(32)"}}}""",
            Signature(json).ToJsonString());
    }

    [Fact]
    public void A_binary_value_in_a_document_labelled_0_26_is_refused_by_its_path()
    {
        var e = RefusedBinary(json => json["irVersion"] = IrDocument.TableTypesVersion);

        Assert.Equal(
            "$.objects[0].parameters[3].type is a binary value (kind \"bytes\"), given in a document of IR 0.26: a document that holds one is IR 0.27, or 0.28 when it declares a column's JSON.",
            e.Message);
    }

    [Fact]
    public void A_document_labelled_0_27_that_holds_no_binary_value_is_refused()
    {
        var e = Refused(json => json["irVersion"] = IrDocument.BytesVersion);

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("holds none", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_binary_value_s_length_counted_in_utf16_code_units_is_refused()
    {
        var e = RefusedBinary(json => Signature(json)["length"]!["unit"] = "utf16");

        Assert.EndsWith(".type.length", e.Path, StringComparison.Ordinal);
        Assert.EndsWith("a binary value's length counts its bytes, which encode nothing.", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_binary_value_s_length_in_bytes_of_an_encoding_is_refused()
    {
        var e = RefusedBinary(json => Signature(json)["length"]!["encoding"] = "utf-8");

        Assert.EndsWith(".length", e.Path, StringComparison.Ordinal);
        Assert.Contains("counted in bytes of an encoding", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("format", "\"int32\"")]
    [InlineData("minLength", """{ "value": 1, "from": { "type": "binary(32)" } }""")]
    [InlineData("digits", """{ "precision": 5, "scale": 0, "from": { "type": "binary(32)" } }""")]
    [InlineData("min", """{ "value": 0, "inclusive": true, "from": { "type": "binary(32)" } }""")]
    [InlineData("max", """{ "value": 9, "inclusive": true, "from": { "type": "binary(32)" } }""")]
    [InlineData("values", """{ "items": ["AQ=="], "match": "exact", "from": [{ "type": "binary(32)" }] }""")]
    public void A_binary_value_with_a_fact_only_text_or_numbers_have_is_refused(string facet, string value)
    {
        var e = RefusedBinary(json => Signature(json)[facet] = JsonNode.Parse(value));

        Assert.EndsWith("." + facet, e.Path, StringComparison.Ordinal);
        Assert.Contains("binary value (kind \"bytes\")", e.Message, StringComparison.Ordinal);
    }
}

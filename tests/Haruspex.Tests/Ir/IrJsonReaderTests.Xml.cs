using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of an XML value (0.34): the label a document that holds one takes, the round trip, the rule
/// <c>xml-value</c> labelling a document alone, an XML value in a document labelled earlier, and the format and facets it can't
/// have. Why: docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md.
/// </summary>
public partial class IrJsonReaderTests
{
    /// <summary>The XML sample's JSON with <paramref name="change"/> made to it, read: the refusal the reader gives.</summary>
    private static IrFormatException RefusedXml(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Xml))!.AsObject();
        change(json);
        return Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
    }

    /// <summary>The XML sample's field <c>spec</c>, an <c>xml(sales.spec_schema)</c>.</summary>
    private static JsonObject SpecField(JsonObject json) =>
        json["types"]!["FindOrdersRow"]!["fields"]!.AsArray().Single(f => (string?)f!["name"] == "spec")!.AsObject();

    [Fact]
    public void A_document_that_holds_an_xml_value_is_labelled_0_34_and_reads_back_equal()
    {
        var json = IrJsonWriter.Write(IrSamples.Xml);

        Assert.Equal("0.34", IrSamples.Xml.Version);
        Assert.Contains("\"irVersion\": \"0.34\"", json, StringComparison.Ordinal);
        Assert.Equal(IrSamples.Xml, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void An_xml_value_is_written_with_its_kind_and_type_alone()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Xml))!.AsObject();

        Assert.Equal("""{"kind":"xml","sql":"xml(sales.spec_schema)"}""", SpecField(json)["type"]!.ToJsonString());
    }

    [Fact]
    public void A_value_read_from_xml_labels_a_document_0_34_though_it_holds_no_xml_value()
    {
        var weight = new IrField("weight", new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "decimal(9,2)"), IrPresence.Always, [new IrNullReason(IrNullRule.XmlValue, "procedures/find_orders.sql:3:20")]);
        var document = IrSamples.Described with
        {
            Types = IrSamples.Described.Types.ToDictionary(t => t.Key, t => t.Key == "FindOrdersRow" ? t.Value with { Fields = [.. t.Value.Fields, weight] } : t.Value, StringComparer.Ordinal),
        };

        Assert.Equal("0.34", document.Version);
        Assert.Equal(document, IrJsonReader.Read(IrJsonWriter.Write(document)));
    }

    [Fact]
    public void A_document_that_holds_none_keeps_its_label() => Assert.Equal("0.33", IrSamples.Described.Version);

    [Fact]
    public void An_xml_value_in_a_document_labelled_0_33_is_refused_by_its_path()
    {
        var e = RefusedXml(json => json["irVersion"] = IrDocument.DescribedVersion);

        // The first XML value, objects before types.
        Assert.Equal("$.objects[0].parameters[4].type.kind", e.Path);
        Assert.Contains("is given in a document of IR 0.33: an XML value", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_read_from_xml_in_a_document_labelled_0_33_is_refused_by_its_rule()
    {
        var e = RefusedXml(json =>
        {
            json["irVersion"] = IrDocument.DescribedVersion;
            json["types"]!["FindOrdersRow"]!["fields"]!.AsArray().Remove(SpecField(json));
            json["objects"]![0]!["parameters"]!.AsArray().RemoveAt(4);
        });

        Assert.Equal("$.types.FindOrdersRow.fields[2].nullableBecause[0].rule", e.Path);
    }

    [Fact]
    public void A_document_labelled_0_34_that_holds_no_xml_value_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Described))!.AsObject();
        json["irVersion"] = IrDocument.CurrentVersion;

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("holds none", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("format", "\"int32\"")]
    [InlineData("length", """{ "value": 10, "unit": "bytes", "from": { "type": "xml" } }""")]
    [InlineData("maxLength", """{ "value": 10, "unit": "utf16", "from": { "type": "xml" } }""")]
    [InlineData("minLength", """{ "value": 1, "from": { "type": "xml" } }""")]
    [InlineData("values", """{ "items": ["<a/>"], "match": "exact", "from": [{ "type": "xml" }] }""")]
    public void An_xml_value_s_format_or_facet_is_refused(string property, string value)
    {
        var e = RefusedXml(json => SpecField(json)["type"]![property] = JsonNode.Parse(value));

        Assert.Equal($"$.types.FindOrdersRow.fields[2].type.{property}", e.Path);
        Assert.Contains("XML value (kind \"xml\")", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_xml_is_written_as_0_33_wrote_it_byte_for_byte()
    {
        // 0.34 adds only what a document either has or lacks: the committed 0.33 file reads as 0.33 and writes back unchanged.
        var file = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "tests", "ir-versions", "0.33.ir.json")).ReplaceLineEndings("\n");
        var document = IrJsonReader.Read(file);

        Assert.Equal(IrDocument.DescribedVersion, document.Version);
        Assert.Equal(file, IrJsonWriter.Write(document));
    }
}

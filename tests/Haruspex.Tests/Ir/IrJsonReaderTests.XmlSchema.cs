using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of a fact an XML schema collection proves (0.35): the label a document that holds one takes,
/// the round trip, such a fact in a document labelled earlier, and a document labelled 0.35 that holds none. Why:
/// docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
public partial class IrJsonReaderTests
{
    /// <summary>The schema-facts sample's field <c>weight</c>'s type, a number bounded by its node's simple type.</summary>
    private static JsonObject WeightType(JsonObject json) =>
        json["types"]!["FindOrdersRow"]!["fields"]!.AsArray().Single(f => (string?)f!["name"] == "weight")!["type"]!.AsObject();

    [Fact]
    public void A_document_that_holds_a_fact_of_a_schema_is_labelled_0_35_and_reads_back_equal()
    {
        var json = IrJsonWriter.Write(IrSamples.SchemaFacts);

        Assert.Equal("0.35", IrSamples.SchemaFacts.Version);
        Assert.Equal(
            """{"value":0,"inclusive":false,"from":{"xml-schema":"sales.spec_schema spec/@weight"}}""",
            WeightType(JsonNode.Parse(json)!.AsObject())["min"]!.ToJsonString());
        Assert.Equal(IrSamples.SchemaFacts, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_fact_of_a_schema_in_a_document_labelled_0_34_is_refused_by_its_path()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.SchemaFacts))!.AsObject();
        json["irVersion"] = "0.34";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.types.FindOrdersRow.fields[3].type.facets.min", e.Path);
        Assert.Contains("is given in a document of IR 0.34: a fact an XML schema collection proves", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_labelled_0_35_that_holds_no_fact_of_a_schema_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Xml))!.AsObject();
        json["irVersion"] = "0.35";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("holds none", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_with_no_fact_of_a_schema_is_written_as_0_34_wrote_it_byte_for_byte()
    {
        // 0.35 adds only what a document either has or lacks: the committed 0.34 file reads as 0.34 and writes back unchanged.
        var file = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "tests", "ir-versions", "0.34.ir.json")).ReplaceLineEndings("\n");
        var document = IrJsonReader.Read(file);

        Assert.Equal(IrDocument.XmlVersion, document.Version);
        Assert.Equal(file, IrJsonWriter.Write(document));
        Assert.Equal("0.34", IrSamples.Xml.Version);
    }
}

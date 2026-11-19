using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// A document combining two databases (IR 0.24, decision 0072): it says so by naming the baseline's source, it alone may have a
/// key that may be left out whatever its value and a result set only one database returns, and it is labelled 0.24 while a
/// document of one database is still labelled 0.23, so that a run without a baseline writes what it wrote before.
/// </summary>
public class IrCombinedDocumentTests
{
    private static IrFormatException Refused(IrDocument document, Action<JsonObject> change)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(document))!.AsObject();
        change(json);
        return Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
    }

    [Fact]
    public void A_combined_document_reads_back_equal_and_writes_the_same_bytes()
    {
        var json = IrJsonWriter.Write(IrSamples.Combined);

        var read = IrJsonReader.Read(json);

        Assert.Equal(IrSamples.Combined, read);
        Assert.Equal(json, IrJsonWriter.Write(read));
    }

    [Fact]
    public void A_combined_document_is_labelled_0_24_and_names_its_baseline_s_source()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Combined))!;

        Assert.Equal("0.24", json["irVersion"]!.GetValue<string>());
        Assert.Equal("""{"dialect":"tsql","package":"shop-release.dacpac","sha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"}""", json["source"]!["baseline"]!.ToJsonString());
    }

    [Fact]
    public void A_document_of_one_database_is_labelled_0_23_as_before()
    {
        Assert.Equal("0.23", IrSamples.Everything.Version);
        Assert.StartsWith("{\n  \"irVersion\": \"0.23\",\n", IrJsonWriter.Write(IrSamples.Everything), StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_in_a_document_labelled_0_23_is_refused()
    {
        var e = Refused(IrSamples.Combined, json => json["irVersion"] = "0.23");

        Assert.Equal("$.source.baseline is given in a document of IR 0.23, which describes one database: a document combining two is IR 0.24 or later.", e.Message);
    }

    [Fact]
    public void A_document_labelled_0_24_without_a_baseline_is_refused()
    {
        var e = Refused(IrSamples.Combined, json => json["source"]!.AsObject().Remove("baseline"));

        Assert.Equal("$.source.baseline is missing: a document of IR 0.24 combines two databases and names the other's source. A document of one database is IR 0.23, 0.25 or later.", e.Message);
    }

    [Fact]
    public void A_key_that_may_be_left_out_whatever_its_value_is_refused_in_a_document_of_one_database()
    {
        var e = Refused(IrSamples.Everything, json => json["types"]!["FindOrdersRow"]!["fields"]![0]!["presence"] = "optional");

        Assert.Equal("$.types.FindOrdersRow.fields[0].presence is \"optional\", which only a document combining two databases (IR 0.24 or 0.25) says.", e.Message);
    }

    [Fact]
    public void A_result_missing_because_one_database_lacks_it_is_refused_in_a_document_of_one_database()
    {
        var e = Refused(IrSamples.Everything, json => json["objects"]![0]!["results"]![0]!["absentBecause"]![0]!["rule"] = "not-in-both");

        Assert.Equal("$.objects[0].results[0].absentBecause[0].rule is \"not-in-both\", which only a document combining two databases (IR 0.24 or 0.25) says.", e.Message);
    }

    [Fact]
    public void A_baseline_s_source_with_a_property_a_source_lacks_is_refused()
    {
        var e = Refused(IrSamples.Combined, json => json["source"]!["baseline"]!["baseline"] = new JsonObject { ["dialect"] = "tsql" });

        Assert.Equal("$.source.baseline.baseline", e.Path);
    }

    [Fact]
    public void A_baseline_has_no_baseline_of_its_own() =>
        Assert.Throws<ArgumentException>(() => new IrSource("tsql") { Baseline = IrSamples.Combined.Source });

    [Fact]
    public void A_result_only_one_database_returns_is_described_as_absent_against_the_other() =>
        Assert.Equal(
            "rows, [] when called against the database without it",
            IrResultText.Describe(IrSamples.Combined.Objects.Single(o => o.Id == "sales.get_order").Results![2]));
}

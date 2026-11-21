using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// Where a field came from (IR 0.25, decision 0078): its source columns (<c>from</c>) and, combined, each database's field
/// (<c>sides</c>) round trip, label a document 0.25, and are refused in a document labelled 0.23 or 0.24, which can't say them.
/// </summary>
public class IrFieldSourceTests
{
    private static IrFormatException Refused(IrDocument document, Action<JsonObject> change)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(document))!.AsObject();
        change(json);
        return Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
    }

    private static JsonObject FindOrdersRowField(JsonObject json, int i) => json["types"]!["FindOrdersRow"]!["fields"]![i]!.AsObject();

    [Fact]
    public void A_document_saying_where_its_fields_came_from_reads_back_equal_and_writes_the_same_bytes()
    {
        var json = IrJsonWriter.Write(IrSamples.Derived);

        var read = IrJsonReader.Read(json);

        Assert.Equal(IrSamples.Derived, read);
        Assert.Equal(json, IrJsonWriter.Write(read));
        Assert.Equal("0.25", read.Version);
    }

    [Fact]
    public void Source_columns_in_a_document_labelled_0_24_are_refused_by_their_path()
    {
        var e = Refused(IrSamples.Derived, json =>
        {
            json["irVersion"] = "0.24";
            json["source"]!.AsObject().Remove("files");
            json["source"]!.AsObject().Remove("path");
            FindOrdersRowField(json, 0).Remove("sides");
            FindOrdersRowField(json, 1).Remove("sides");
        });

        Assert.Equal("$.types.FindOrdersRow.fields[0].from is given in a document of IR 0.24: a field's source columns are IR 0.25.", e.Message);
    }

    [Fact]
    public void A_source_s_file_hashes_in_a_document_labelled_0_24_are_refused_and_an_empty_list_of_them_too()
    {
        var labelled = Refused(IrSamples.Combined with { Source = IrSamples.Derived.Source with { Path = null } }, json => json["irVersion"] = "0.24");
        var empty = Refused(IrSamples.Derived, json => json["source"]!["files"] = new JsonObject());

        Assert.Equal("$.source.files is given in a document of IR 0.24: a source's files are IR 0.25.", labelled.Message);
        Assert.Equal("$.source.files is empty: a source that points into no script leaves it out.", empty.Message);
    }

    [Fact]
    public void A_source_s_path_in_a_document_labelled_0_24_is_refused()
    {
        var e = Refused(IrSamples.Combined with { Source = IrSamples.Combined.Source with { Path = "database" } }, json => json["irVersion"] = "0.24");

        Assert.Equal("$.source.path is given in a document of IR 0.24: a source's path is IR 0.25.", e.Message);
    }

    [Fact]
    public void A_field_s_sides_in_a_document_of_one_database_are_refused()
    {
        var e = Refused(IrSamples.Everything, json => FindOrdersRowField(json, 0)["sides"] = JsonNode.Parse("""{ "candidate": { "presence": "always", "nullable": false } }"""));

        Assert.Equal("$.types.FindOrdersRow.fields[0].sides is given, which only a document combining two databases (IR 0.24 or later) says.", e.Message);
    }

    [Fact]
    public void An_empty_list_of_source_columns_is_refused()
    {
        var e = Refused(IrSamples.Derived, json => FindOrdersRowField(json, 0)["from"] = new JsonArray());

        Assert.Equal("$.types.FindOrdersRow.fields[0].from is empty: a field that reads no column leaves it out.", e.Message);
    }

    [Fact]
    public void Sides_that_name_neither_database_are_refused()
    {
        var e = Refused(IrSamples.Derived, json => FindOrdersRowField(json, 0)["sides"] = new JsonObject());

        Assert.Equal("$.types.FindOrdersRow.fields[0].sides names neither side: a combined field comes from at least one of the two databases.", e.Message);
    }

    [Fact]
    public void A_side_that_may_be_left_out_whatever_its_value_is_refused()
    {
        var e = Refused(IrSamples.Derived, json => FindOrdersRowField(json, 0)["sides"]!["baseline"]!["presence"] = "optional");

        Assert.Equal("$.types.FindOrdersRow.fields[0].sides.baseline.presence is \"optional\", which only a combined field is: each side is one database's.", e.Message);
    }

    [Fact]
    public void Where_a_field_came_from_is_no_claim_so_a_comparison_reports_no_change_in_it() =>
        Assert.Empty(IrComparison.Compare(IrSamples.Combined, IrSamples.Derived));
}

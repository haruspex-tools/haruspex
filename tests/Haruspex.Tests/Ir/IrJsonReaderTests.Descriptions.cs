using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of a description (0.33, <see cref="IrField.Description"/>, <see cref="IrParameter.Description"/>,
/// <see cref="IrObject.Description"/>): the label a document that records one takes, the round trip, a description in a document
/// labelled earlier, and what <c>check</c> and a combined document make of one.
/// Why: docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md.
/// </summary>
public partial class IrJsonReaderTests
{
    [Fact]
    public void A_document_that_records_a_description_is_labelled_0_33_and_reads_back_equal()
    {
        var json = IrJsonWriter.Write(IrSamples.Described);

        Assert.Equal("0.33", IrSamples.Described.Version);
        Assert.Contains("\"irVersion\": \"0.33\"", json, StringComparison.Ordinal);
        Assert.Equal(IrSamples.Described, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_document_that_records_none_keeps_its_label()
    {
        Assert.Equal("0.32", IrSamples.Folders.Version);
        Assert.DoesNotContain("\"description\"", IrJsonWriter.Write(IrSamples.Folders), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$.objects[0].description")]
    [InlineData("$.objects[0].parameters[0].description")]
    [InlineData("$.types.FindOrdersRow.fields[0].description")]
    public void A_description_in_a_document_labelled_earlier_is_refused_by_its_path(string at)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Folders))!.AsObject();
        var owner = at switch
        {
            "$.objects[0].description" => json["objects"]![0]!,
            "$.objects[0].parameters[0].description" => json["objects"]![0]!["parameters"]![0]!,
            _ => json["types"]!["FindOrdersRow"]!["fields"]![0]!,
        };
        owner["description"] = "Words.";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal(at, e.Path);
        Assert.Contains("is given in a document of IR 0.32: a description is IR 0.33 or later.", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_labelled_0_33_that_records_no_description_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Folders))!.AsObject();
        json["irVersion"] = "0.33";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("records none", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_description_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Described))!.AsObject();
        json["objects"]![0]!["parameters"]![0]!["description"] = "";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.objects[0].parameters[0].description", e.Path);
    }

    [Fact]
    public void A_description_holding_half_of_a_surrogate_pair_alone_is_refused_as_no_text()
    {
        var json = IrJsonWriter.Write(IrSamples.Described).Replace("\"The order to read.\"", "\"The \\ud800 order.\"", StringComparison.Ordinal);

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json));

        Assert.Equal("$.objects[0].parameters[0].description", e.Path);
        Assert.Contains("half of a UTF-16 surrogate pair alone", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_description_is_no_change_of_claim()
    {
        var before = IrSamples.Described;
        var after = before with { Objects = [before.Objects[0] with { Description = "Another text." }, .. before.Objects.Skip(1)] };
        var gone = IrSamples.Folders;

        Assert.Empty(IrComparison.Compare(before, after));
        Assert.Empty(IrComparison.Compare(before, gone));
    }

    [Fact]
    public void A_combined_document_keeps_a_description_only_where_both_give_the_same()
    {
        var one = IrSamples.Everything;
        var same = one with
        {
            Objects =
            [
                one.Objects[0] with
                {
                    Description = "One order, with its lines.",
                    Parameters = [one.Objects[0].Parameters[0] with { Description = "The order to read." }, .. one.Objects[0].Parameters.Skip(1)],
                },
                .. one.Objects.Skip(1),
            ],
            Types = one.Types.ToDictionary(t => t.Key, t => t.Key == "FindOrdersRow" ? t.Value with { Fields = [t.Value.Fields[0] with { Description = "The order's own number." }, .. t.Value.Fields.Skip(1)] } : t.Value, StringComparer.Ordinal),
        };
        var other = same with
        {
            Objects = [same.Objects[0] with { Description = "Another text." }, .. same.Objects.Skip(1)],
            Types = same.Types.ToDictionary(t => t.Key, t => t.Key == "FindOrdersRow" ? t.Value with { Fields = [t.Value.Fields[0] with { Description = null }, .. t.Value.Fields.Skip(1)] } : t.Value, StringComparer.Ordinal),
        };

        var combined = IrCombination.Of(same, other, new IrFitOptions()).Document;

        Assert.Null(combined.Objects[0].Description);
        Assert.Equal("The order to read.", combined.Objects[0].Parameters[0].Description);
        Assert.Null(combined.Types["FindOrdersRow"].Fields[0].Description);
    }
}

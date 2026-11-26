using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of the packages the config file declared builds (0.31, <see cref="IrSource.Builds"/>): the
/// label a document that records one takes, the round trip, a name that is no package the source read, and the declaration in a
/// document labelled earlier. Why: docs/decisions/0122-the-ir-records-the-packages-declared-builds.md.
/// </summary>
public partial class IrJsonReaderTests
{
    [Fact]
    public void A_document_that_records_packages_declared_builds_is_labelled_0_31_and_reads_back_equal()
    {
        var document = IrSamples.Built;
        var json = IrJsonWriter.Write(document);

        Assert.Equal("0.31", document.Version);
        Assert.Contains("\"builds\": [\n      \"catalogue.dacpac\",\n      \"shop.dacpac\"\n    ]", json, StringComparison.Ordinal);
        Assert.Contains("\"builds\": [\n        \"shop-release.dacpac\"\n      ]", json, StringComparison.Ordinal);
        Assert.Equal(document, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_document_whose_baseline_alone_records_a_build_is_labelled_0_31()
    {
        var document = IrSamples.Built with { Source = IrSamples.Built.Source with { Builds = [] } };
        var json = IrJsonWriter.Write(document);

        Assert.Equal("0.31", document.Version);
        Assert.Null(JsonNode.Parse(json)!["source"]!["builds"]);
        Assert.Equal(document, IrJsonReader.Read(json));
    }

    [Fact]
    public void A_document_that_records_no_build_is_still_labelled_0_30()
    {
        Assert.Equal("0.30", IrSamples.Stamped.Version);
        Assert.StartsWith("{\n  \"irVersion\": \"0.30\",", IrJsonWriter.Write(IrSamples.Stamped), StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_labelled_0_31_that_records_no_build_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Stamped))!.AsObject();
        json["irVersion"] = "0.31";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("which labels a document that records packages declared builds, and this one records none", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$.source")]
    [InlineData("$.source.baseline")]
    public void Builds_recorded_in_a_document_labelled_earlier_are_refused_by_their_path(string at)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Stamped))!.AsObject();
        var source = at == "$.source" ? json["source"]! : json["source"]!["baseline"]!;
        source["builds"] = new JsonArray(at == "$.source" ? "shop.dacpac" : "shop-release.dacpac");

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal(at + ".builds", e.Path);
        Assert.Contains("is given in a document of IR 0.30: packages declared builds are IR 0.31", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sales.dacpac")]
    [InlineData("master.dacpac")]
    [InlineData("SHOP.dacpac")]
    public void A_build_that_is_no_package_the_source_read_is_refused(string name)
    {
        // master.dacpac is a reference that wasn't found, so nothing of it was read; a name is the one the source record gives.
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Built))!.AsObject();
        json["source"]!["builds"] = new JsonArray(name);

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.source.builds[0]", e.Path);
        Assert.Contains("which names no package the source read", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_build_named_twice_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Built))!.AsObject();
        json["source"]!["builds"] = new JsonArray("shop.dacpac", "shop.dacpac");

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.source.builds[1]", e.Path);
    }

    [Fact]
    public void An_empty_list_of_builds_is_refused_since_none_declared_is_written_by_leaving_it_out()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Built))!.AsObject();
        json["source"]!["builds"] = new JsonArray();

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.source.builds", e.Path);
    }

    [Fact]
    public void A_document_whose_build_is_no_package_it_read_is_not_written()
    {
        var document = IrSamples.Built with { Source = IrSamples.Built.Source with { Builds = ["ledger.dacpac", "master.dacpac"] } };

        var e = Assert.Throws<ArgumentException>(() => IrJsonWriter.Write(document));

        Assert.Contains("declares master.dacpac a build, which is no package it read", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_that_records_builds_and_names_no_writer_is_not_written()
    {
        var document = IrSamples.Built with { Source = IrSamples.Built.Source with { WrittenBy = null, Baseline = IrSamples.Built.Source.Baseline! with { WrittenBy = null } } };

        var e = Assert.Throws<ArgumentException>(() => IrJsonWriter.Write(document));

        Assert.Contains("records packages declared builds and names no build of the tool that wrote it", e.Message, StringComparison.Ordinal);
    }
}

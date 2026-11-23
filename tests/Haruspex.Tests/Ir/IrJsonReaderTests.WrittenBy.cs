using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of the build that wrote a document (0.30, <see cref="IrSource.WrittenBy"/>): the label a
/// stamped document takes, a 0.30 document without its stamp, and a stamp in a document labelled earlier, which carries none.
/// Why: docs/decisions/0117-an-ir-document-names-the-build-that-made-its-claims.md.
/// </summary>
public partial class IrJsonReaderTests
{
    [Fact]
    public void A_document_that_names_its_writer_is_labelled_0_30_and_reads_back_equal()
    {
        var document = IrSamples.Stamped;
        var json = IrJsonWriter.Write(document);

        Assert.Equal("0.30", document.Version);
        Assert.Contains($"\"writtenBy\": {{\n      \"version\": \"{IrSamples.Build.Version}\",\n      \"commit\": \"{IrSamples.Build.Commit}\"\n    }}", json, StringComparison.Ordinal);
        Assert.Equal(document, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_writer_whose_commit_is_not_known_is_written_without_one_and_reads_back_equal()
    {
        var document = IrSamples.Everything with { Source = IrSamples.Everything.Source with { WrittenBy = new IrBuild("0.0.0-preview") } };
        var json = IrJsonWriter.Write(document);

        Assert.Equal("0.30", document.Version);
        Assert.Contains("\"writtenBy\": {\n      \"version\": \"0.0.0-preview\"\n    }", json, StringComparison.Ordinal);
        Assert.Equal(document, IrJsonReader.Read(json));
    }

    [Fact]
    public void A_baseline_read_from_a_file_that_names_no_writer_is_written_without_one()
    {
        var document = IrSamples.Stamped with { Source = IrSamples.Stamped.Source with { Baseline = IrSamples.Stamped.Source.Baseline! with { WrittenBy = null } } };
        var json = JsonNode.Parse(IrJsonWriter.Write(document))!;

        Assert.Null(json["source"]!["baseline"]!["writtenBy"]);
        Assert.NotNull(json["source"]!["writtenBy"]);
        Assert.Equal(document, IrJsonReader.Read(json.ToJsonString()));
    }

    [Fact]
    public void A_document_labelled_0_30_that_names_no_writer_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Stamped))!.AsObject();
        json["source"]!.AsObject().Remove("writtenBy");

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.source.writtenBy", e.Path);
        Assert.Contains("is missing: a document of IR 0.30 names the build that wrote it", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$.source")]
    [InlineData("$.source.baseline")]
    public void A_writer_named_in_a_document_labelled_earlier_is_refused_by_its_path(string at)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Declared))!.AsObject();
        var source = at == "$.source" ? json["source"]! : json["source"]!["baseline"]!;
        source["writtenBy"] = new JsonObject { ["version"] = IrSamples.Build.Version };

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal(at + ".writtenBy", e.Path);
        Assert.Contains("is given in a document of IR 0.28: the build that wrote a document is IR 0.30", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
    [InlineData("0123456")]
    [InlineData("")]
    public void A_commit_that_is_not_a_whole_one_is_refused(string commit)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Stamped))!.AsObject();
        json["source"]!["writtenBy"]!["commit"] = commit;

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.source.writtenBy.commit", e.Path);
    }

    [Fact]
    public void An_empty_version_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Stamped))!.AsObject();
        json["source"]!["writtenBy"]!["version"] = "";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.source.writtenBy.version", e.Path);
    }

    [Fact]
    public void A_document_whose_baseline_names_its_writer_and_whose_own_source_does_not_is_not_written()
    {
        var document = IrSamples.Stamped with { Source = IrSamples.Stamped.Source with { WrittenBy = null } };

        var e = Assert.Throws<ArgumentException>(() => IrJsonWriter.Write(document));

        Assert.Contains("names the build that wrote its baseline and not its own", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_that_names_no_writer_is_still_labelled_by_the_rest_of_what_it_says()
    {
        Assert.Equal("0.28", IrSamples.Declared.Version);
        Assert.Equal("0.29", Explained().Version);
        Assert.Equal("0.30", (Explained() with { Source = Explained().Source with { WrittenBy = IrSamples.Build } }).Version);
    }
}

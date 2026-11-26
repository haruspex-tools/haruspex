using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of a source read from several folders (0.32, <see cref="IrSource.Paths"/>): the label a
/// document that records one takes, the round trip, a document of one folder unchanged, and the folders in a document labelled
/// earlier. Why: docs/decisions/0127-a-source-may-be-several-folders.md.
/// </summary>
public partial class IrJsonReaderTests
{
    private static IrDocument ReadFrom(params string[] folders) =>
        IrSamples.Stamped with { Source = IrSamples.Stamped.Source with { Path = null, Paths = [.. folders] } };

    [Fact]
    public void A_document_read_from_several_folders_is_labelled_0_32_with_its_folders_sorted_and_reads_back_equal()
    {
        var document = ReadFrom("tables", "procedures");
        var json = IrJsonWriter.Write(document);

        Assert.Equal("0.32", document.Version);
        Assert.Contains("\"paths\": [\n      \"procedures\",\n      \"tables\"\n    ]", json, StringComparison.Ordinal);
        Assert.Null(JsonNode.Parse(json)!["source"]!["path"]);
        Assert.Equal(document, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void Every_folder_of_many_is_written_and_read_back()
    {
        var document = ReadFrom("views", "tables", "procedures", "functions");
        var json = IrJsonWriter.Write(document);

        Assert.Equal(["functions", "procedures", "tables", "views"], JsonNode.Parse(json)!["source"]!["paths"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Equal(document, IrJsonReader.Read(json));
    }

    [Fact]
    public void A_document_whose_baseline_alone_was_read_from_several_folders_is_labelled_0_32()
    {
        var json = IrJsonWriter.Write(IrSamples.Folders);

        Assert.Equal("0.32", IrSamples.Folders.Version);
        Assert.Equal("database", JsonNode.Parse(json)!["source"]!["path"]!.GetValue<string>());
        Assert.Equal(IrSamples.Folders, IrJsonReader.Read(json));
    }

    [Fact]
    public void A_document_read_from_one_folder_is_labelled_as_before_and_records_no_paths()
    {
        var json = IrJsonWriter.Write(IrSamples.Stamped);

        Assert.Equal("0.30", IrSamples.Stamped.Version);
        Assert.Equal("0.31", IrSamples.Built.Version);
        Assert.DoesNotContain("\"paths\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_labelled_0_32_that_records_no_folders_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Built))!.AsObject();
        json["irVersion"] = "0.32";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("which labels a document whose source was read from several folders", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_labelled_0_32_may_record_packages_declared_builds()
    {
        var document = IrSamples.Folders;

        Assert.NotEmpty(document.Source.Builds);
        Assert.Equal(document, IrJsonReader.Read(IrJsonWriter.Write(document)));
    }

    [Theory]
    [InlineData("$.source")]
    [InlineData("$.source.baseline")]
    public void Folders_recorded_in_a_document_labelled_earlier_are_refused_by_their_path(string at)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Built))!.AsObject();
        var source = at == "$.source" ? json["source"]!.AsObject() : json["source"]!["baseline"]!.AsObject();
        source.Remove("path");
        source["paths"] = new JsonArray("procedures", "tables");

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal(at + ".paths", e.Path);
        Assert.Contains("is given in a document of IR 0.31: a source read from several folders is IR 0.32.", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]", "$.source.paths", "must list at least two folders")]
    [InlineData("[\"tables\"]", "$.source.paths", "must list at least two folders")]
    [InlineData("[\"tables\", \"tables\"]", "$.source.paths[1]", "named twice")]
    [InlineData("[\"tables\", \"\"]", "$.source.paths[1]", "is empty")]
    public void Folders_listed_other_than_once_each_two_or_more_are_refused(string paths, string at, string problem)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(ReadFrom("tables", "procedures")))!.AsObject();
        json["source"]!["paths"] = JsonNode.Parse(paths);

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal(at, e.Path);
        Assert.Contains(problem, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_beside_folders_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(ReadFrom("tables", "procedures")))!.AsObject();
        json["source"]!["path"] = "database";

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.source.path", e.Path);
    }

    [Fact]
    public void A_source_the_reader_would_refuse_is_not_written()
    {
        Assert.Throws<ArgumentException>(() => IrJsonWriter.Write(ReadFrom("tables")));
        Assert.Throws<ArgumentException>(() => IrJsonWriter.Write(ReadFrom("tables", "tables")));
        Assert.Throws<ArgumentException>(() => IrJsonWriter.Write(ReadFrom("tables", "procedures") with { Source = ReadFrom("tables", "procedures").Source with { Path = "database" } }));
        Assert.Throws<ArgumentException>(() => IrJsonWriter.Write(ReadFrom("tables", "procedures") with { Source = ReadFrom("tables", "procedures").Source with { WrittenBy = null, Baseline = null } }));
    }
}

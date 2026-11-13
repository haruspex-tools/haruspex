using Haruspex.SqlServer.Sources;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Sources;

/// <summary>
/// Where each object of every fixture and corpus is, as the model records it: the positions every diagnostic and every
/// <c>at</c> in the IR come from. The plots fixture lays its scripts out with byte order marks, UTF-16, CRLF and lone CR
/// line endings, tabs and <c>GO</c> in each form, and the corpora have many batches to a file, so a position that any of
/// those moves fails here.
/// </summary>
public class ModelLocationsTests
{
    public static TheoryData<string> Fixtures { get; } = [.. Fixture.All.Select(f => f.Name)];

    public static TheoryData<string> Corpora { get; } = [.. Corpus.Corpus.All.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_object_of_a_fixture_is_where_the_model_says(string fixture) =>
        Snapshot.Match(ModelLocations.Render(SqlScriptFolder.Read(Fixture.Named(fixture).Database)), $"{nameof(ModelLocationsTests)}.{fixture}", "txt");

    [Theory]
    [MemberData(nameof(Corpora))]
    public void Every_object_of_a_corpus_is_where_the_model_says(string corpus)
    {
        var read = Corpus.Corpus.All.Single(c => c.Name == corpus);
        Snapshot.Match(ModelLocations.Render(SqlScriptFolder.Read(read.ProjectFolder, read.Scripts)), $"{nameof(ModelLocationsTests)}.{corpus}", "txt");
    }
}

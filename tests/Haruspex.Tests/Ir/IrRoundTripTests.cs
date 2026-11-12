using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// The IR's round trip, as a law over real output: for every document haruspex writes, reading the file gives a document
/// equal to the one written, and writing that again gives the same bytes. Checked on what every fixture infers, on every
/// committed IR snapshot (what a consumer of a fixture's file gets) and on both public corpora.
/// </summary>
/// <remarks>
/// Both halves are needed. Equal documents alone would pass a reader that dropped what the writer leaves out anyway; equal
/// bytes alone would pass a reader and writer that agreed on a mistake. Together they say the file holds exactly the
/// document, no more and no less.
/// </remarks>
public class IrRoundTripTests
{
    public static TheoryData<string> Fixtures { get; } = [.. Fixture.All.Select(f => f.Name)];

    public static TheoryData<string> Snapshots { get; } = [.. Fixture.All.SelectMany(f => f.Snapshots).Select(s => Path.GetRelativePath(RepositoryPaths.Fixtures, s.Ir))];

    public static TheoryData<string> Corpora { get; } = [.. Corpus.Corpus.All.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void What_a_fixture_infers_reads_back_equal_and_writes_the_same_bytes(string fixture) =>
        AssertRoundTrip(Fixture.Named(fixture).Ir);

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void A_committed_ir_file_reads_and_writes_back_byte_for_byte(string snapshot)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryPaths.Fixtures, snapshot));

        Assert.Equal(text, IrJsonWriter.Write(IrJsonReader.Read(text)));
    }

    [Theory]
    [MemberData(nameof(Corpora))]
    public void What_a_corpus_infers_reads_back_equal_and_writes_the_same_bytes(string corpus) =>
        AssertRoundTrip(Corpus.Corpus.Runs.Single(r => r.Corpus.Name == corpus).Document);

    private static void AssertRoundTrip(IrDocument written)
    {
        var json = IrJsonWriter.Write(written);

        var read = IrJsonReader.Read(json);

        Assert.Equal(written, read);
        Assert.Equal(json, IrJsonWriter.Write(read));
    }
}

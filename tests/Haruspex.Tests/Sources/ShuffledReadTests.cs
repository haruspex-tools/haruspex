using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// A folder's model doesn't depend on the order its files are listed in: read with them in any order, every object is in
/// the same place in the model, every diagnostic is the same, and so is the IR of every procedure.
/// </summary>
/// <remarks>
/// This holds the reader to reading files in one order of its own. What that order makes of the model, source order, is
/// held by <c>DatabaseModelOrderTests</c>, and the order of the names a message lists by <c>ExecTests</c> and <c>TableFunctionTests</c>.
/// </remarks>
public class ShuffledReadTests
{
    public static TheoryData<string> Fixtures { get; } = [.. Fixture.All.Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_fixture_read_with_its_files_in_any_order_gives_the_same_model_and_ir(string fixture)
    {
        var folder = Fixture.Named(fixture).Database;
        var files = Directory.EnumerateFiles(folder, "*.sql", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)).ToList();

        Assert.All(Shuffles(files), shuffled => Assert.Equal(Output(folder, files), Output(folder, shuffled)));
    }

    [Fact]
    public void A_corpus_read_with_its_files_in_any_order_gives_the_same_model_and_ir()
    {
        // WideWorldImporters: hundreds of files in many folders.
        var corpus = Corpus.Corpus.All.Single(c => c.Name == "WideWorldImporters");

        Assert.All(Shuffles(corpus.Scripts), shuffled => Assert.Equal(Output(corpus.ProjectFolder, corpus.Scripts), Output(corpus.ProjectFolder, shuffled)));
    }

    /// <summary>The model in its own order, its diagnostics, and the IR of every procedure.</summary>
    private static string Output(string folder, IEnumerable<string> files)
    {
        var read = SqlScriptFolder.Read(folder, files);
        var document = IrInference.Infer(read.Model, [.. read.Model.Procedures.Select(p => p.Name)]).Value;
        return ModelLocations.Render(read, sorted: false) + IrJsonWriter.Write(document);
    }

    /// <summary>The files reversed, and in three orders shuffled with fixed seeds, so a failure can be repeated.</summary>
    private static IEnumerable<IReadOnlyList<string>> Shuffles(IReadOnlyList<string> files)
    {
        yield return [.. files.Reverse()];
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var shuffled = files.ToArray();
#pragma warning disable CA5394 // A test's fixed shuffle, not security.
            new Random(seed).Shuffle(shuffled);
#pragma warning restore CA5394
            yield return shuffled;
        }
    }
}

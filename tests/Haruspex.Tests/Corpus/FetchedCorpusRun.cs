using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Tests.Corpus;

/// <summary>What haruspex made of a fetched corpus: the diagnostics met reading it, and the IR of each database read.</summary>
/// <param name="Corpus">The corpus run.</param>
/// <param name="Diagnostics">Everything reported while reading it, errors and warnings, in the order met.</param>
/// <param name="Documents">The IR of each database the corpus reads, in its manifest's order.</param>
internal sealed record FetchedCorpusRun(FetchedCorpus Corpus, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<IrDocument> Documents)
{
    /// <summary>The figures of every database read, summed.</summary>
    public CorpusFigures Figures { get; } = CorpusFigures.Sum([.. Documents.Select(CorpusFigures.Count)]);
}

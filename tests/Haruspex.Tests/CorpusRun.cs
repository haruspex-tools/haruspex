using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>What haruspex made of a corpus: the errors met while reading it, and the IR it inferred.</summary>
/// <param name="Corpus">The corpus run.</param>
/// <param name="Errors">Errors met while reading the scripts. A script with an error adds nothing to the model.</param>
/// <param name="Document">The IR for every procedure read.</param>
internal sealed record CorpusRun(Corpus Corpus, IReadOnlyList<Diagnostic> Errors, IrDocument Document);

using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>The outcome of applying a config file to a document: the document when there are no errors, and every problem.</summary>
public sealed record ConfigApplied(IrDocument? Document, EquatableArray<ConfigProblem> Problems);

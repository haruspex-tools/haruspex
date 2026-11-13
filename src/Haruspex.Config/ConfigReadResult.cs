using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>The outcome of reading a config file: the config when it has no errors, and every problem found.</summary>
public sealed record ConfigReadResult(HaruspexConfig? Config, EquatableArray<Diagnostic> Problems);

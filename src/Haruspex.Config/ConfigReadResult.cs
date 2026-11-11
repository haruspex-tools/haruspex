namespace Haruspex.Config;

/// <summary>The outcome of reading a config file: the config when it has no errors, and every problem found.</summary>
public sealed record ConfigReadResult(HaruspexConfig? Config, IReadOnlyList<ConfigProblem> Problems);

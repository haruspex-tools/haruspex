namespace Haruspex.Config;

/// <summary>A query file <see cref="QuerySelection"/> found: where it is on disk, and its path relative to the config file's folder, with <c>/</c> between folders.</summary>
/// <param name="Path">The file on disk.</param>
/// <param name="Relative">Its path relative to the config file's folder: the query's identity.</param>
public sealed record QueryFileFound(string Path, string Relative);


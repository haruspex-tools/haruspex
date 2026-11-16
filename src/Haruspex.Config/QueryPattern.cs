using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>
/// One entry of <c>queries</c>: a folder, a query file, or a pattern of them, relative to the config file's folder, with where it
/// is written. <see cref="QuerySelection"/> finds the files it names.
/// </summary>
/// <param name="Text">The entry as written, with <c>/</c> between folders: <c>queries</c>, <c>sql/**/*.sql</c>, <c>sql/get_order.sql</c>.</param>
/// <param name="At">Where it is written, for a problem with it.</param>
public sealed record QueryPattern(string Text, JsonLocation At)
{
    /// <summary>Whether it is a pattern rather than a path: it holds <c>*</c> or <c>?</c>.</summary>
    public bool IsPattern => Text.Contains('*', StringComparison.Ordinal) || Text.Contains('?', StringComparison.Ordinal);
}

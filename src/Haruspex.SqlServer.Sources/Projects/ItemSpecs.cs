namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// What an item's <c>Include</c>, <c>Exclude</c>, <c>Remove</c> or <c>Update</c> names: a list separated by <c>;</c>, each a path or
/// a pattern relative to the project's folder, matched ignoring case (decision 0044). One rule for the evaluator, which applies an
/// item, and for the reader, which works out what a doubted one may touch.
/// </summary>
internal static class ItemSpecs
{
    /// <summary>The paths or patterns <paramref name="text"/> lists.</summary>
    public static string[] Of(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Whether the path or pattern <paramref name="spec"/>, relative to <paramref name="folder"/>, names the file at the full <paramref name="path"/>.</summary>
    public static bool Names(string folder, string spec, string path) =>
        Glob.IsPattern(spec) ? Glob.Of(folder, spec).Matches(path) : DiskPath.Full(folder, spec).Equals(path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether any path or pattern <paramref name="specs"/> lists names the file at <paramref name="path"/>.</summary>
    public static bool AnyNames(string folder, string specs, string path) => Of(specs).Any(spec => Names(folder, spec, path));

    /// <summary>Every file <paramref name="specs"/> names, as full paths: a pattern's files, and a path as written.</summary>
    public static IEnumerable<string> Files(string folder, string specs) =>
        Of(specs).SelectMany(spec => Glob.IsPattern(spec) ? Glob.Of(folder, spec).Files() : DiskPath.IsRootedElsewhere(spec) ? [] : [DiskPath.Full(folder, spec)]);
}

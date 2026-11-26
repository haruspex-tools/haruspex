namespace Haruspex.Ir;

/// <summary>
/// What a document's source means for the code written from it, in words, for the header an emitter writes, so that every
/// emitter says the same thing of the same document, each in its own place (a Zod comment, a JSON Schema description).
/// </summary>
public static class IrSourceText
{
    /// <summary>
    /// For a document combining two databases (<see cref="IrSource.Baseline"/>), what that means for its types:
    /// <c>Combined with a baseline (Shop.dacpac): results accept what either database returns, and parameters take only what both
    /// accept.</c> Null for a document of one database.
    /// </summary>
    public static string? Baseline(IrSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Baseline is { } baseline
            ? $"Combined with a baseline{(baseline.Package is { } package ? $" ({package.Name})" : "")}: results accept what either database returns, and parameters take only what both accept."
            : null;
    }

    /// <summary>
    /// For a document whose claims rest on packages the config file declared builds (<see cref="IrSource.Builds"/>, its baseline's
    /// included), that assumption: <c>Its types assume that sales.dacpac was built from a project, as the config file declares, so
    /// its primary keys and unique constraints hold.</c> Null when none was declared, so a document without one says nothing new.
    /// </summary>
    /// <remarks>
    /// A declared build is the user's word, as a declared session is, so it is said where the types are read
    /// (docs/decisions/0120-an-extract-s-key-constraints-are-no-key.md, docs/decisions/0122-the-ir-records-the-packages-declared-builds.md).
    /// </remarks>
    public static string? Builds(IrSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<string> packages = [.. source.Builds, .. (source.Baseline?.Builds ?? []).Select(name => "the baseline's " + name)];
        if (packages.Count == 0)
            return null;
        var listed = packages.Count == 1 ? packages[0] : string.Join(", ", packages.Take(packages.Count - 1)) + " and " + packages[^1];
        return packages.Count == 1
            ? $"Its types assume that {listed} was built from a project, as the config file declares, so its primary keys and unique constraints hold."
            : $"Its types assume that {listed} were built from projects, as the config file declares, so their primary keys and unique constraints hold.";
    }
}

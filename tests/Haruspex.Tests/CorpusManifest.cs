using System.Text.Json;

namespace Haruspex.Tests;

/// <summary>
/// <c>tests/corpus/manifest.json</c>: where the vendored corpus came from, under which licence, and the SHA-256 of every
/// file, written by <c>tests/corpus/refresh.mjs</c>. See <c>tests/corpus/README.md</c>.
/// </summary>
/// <param name="Repository">The upstream repository's URL.</param>
/// <param name="Commit">The full SHA of the commit the files were taken from.</param>
/// <param name="Committed">When that commit was made, as GitHub gives it.</param>
/// <param name="Licence">The licence the files are under, and the file that states it.</param>
/// <param name="Folder">The folder under <c>tests/corpus</c> that holds the files, at their upstream paths.</param>
/// <param name="Files">Every vendored file, in ordinal order of its path.</param>
internal sealed record CorpusManifest(
    string Repository,
    string Commit,
    string Committed,
    CorpusManifest.LicenceFile Licence,
    string Folder,
    IReadOnlyList<CorpusManifest.VendoredFile> Files)
{
    /// <summary>The folder that holds the corpus: the manifest, the refresh script, and the vendored files below it.</summary>
    public static string Root { get; } = Path.Combine(TestPaths.RepoRoot, "tests", "corpus");

    /// <summary>The manifest as committed.</summary>
    public static CorpusManifest Current { get; } = Load();

    /// <summary>The folder the vendored files are in, at their upstream paths.</summary>
    public string VendoredRoot => Path.Combine(Root, Folder);

    /// <summary>A vendored file's full path.</summary>
    public string PathOf(string upstreamPath) => Path.Combine(VendoredRoot, upstreamPath);

    private static CorpusManifest Load()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        var root = json.RootElement;
        static string Text(JsonElement e, string name) => e.GetProperty(name).GetString()
            ?? throw new InvalidOperationException($"tests/corpus/manifest.json: {name} is null.");
        var licence = root.GetProperty("licence");
        return new CorpusManifest(
            Text(root, "repository"),
            Text(root, "commit"),
            Text(root, "committed"),
            new LicenceFile(Text(licence, "spdx"), Text(licence, "file")),
            Text(root, "folder"),
            [.. root.GetProperty("files").EnumerateArray().Select(f => new VendoredFile(Text(f, "path"), f.GetProperty("bytes").GetInt64(), Text(f, "sha256")))]);
    }

    /// <summary>The licence: its SPDX identifier, and its file's upstream path.</summary>
    internal sealed record LicenceFile(string Spdx, string File);

    /// <summary>One vendored file: its upstream path, its length and its SHA-256, in lower-case hex.</summary>
    internal sealed record VendoredFile(string Path, long Bytes, string Sha256);
}

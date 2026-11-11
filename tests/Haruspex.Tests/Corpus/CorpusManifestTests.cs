using System.Security.Cryptography;

namespace Haruspex.Tests.Corpus;

/// <summary>
/// The vendored corpus is exactly what the manifest says: every file byte for byte as fetched from the pinned commit, and
/// nothing else. A file edited by hand, by a tool that normalises line endings, or added without the refresh script, fails
/// here, so the figures in <c>docs/corpus-figures.md</c> are always of the commit the manifest names.
/// </summary>
public class CorpusManifestTests
{
    private static readonly CorpusManifest Manifest = CorpusManifest.Current;

    [Fact]
    public void The_manifest_names_a_full_commit_and_the_licence_it_vendors()
    {
        Assert.Matches("^[0-9a-f]{40}$", Manifest.Commit);
        Assert.Equal("https://github.com/microsoft/sql-server-samples", Manifest.Repository);
        Assert.Equal("MIT", Manifest.Licence.Spdx);
        Assert.Contains(Manifest.Files, f => f.Path == Manifest.Licence.File);
    }

    [Fact]
    public void Every_vendored_file_matches_its_recorded_hash()
    {
        var wrong = Manifest.Files
            .Where(f => !File.Exists(Manifest.PathOf(f.Path)) || Sha256(Manifest.PathOf(f.Path)) != f.Sha256)
            .Select(f => f.Path)
            .ToList();

        Assert.True(wrong.Count == 0, "These files are missing or differ from tests/corpus/manifest.json; restore them, or run tests/corpus/refresh.mjs:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Nothing_is_vendored_that_the_manifest_does_not_list()
    {
        var listed = Manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var unlisted = Directory.EnumerateFiles(Manifest.VendoredRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Manifest.VendoredRoot, path).Replace('\\', '/'))
            .Where(path => !listed.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unlisted.Count == 0, "These files are in the vendored corpus but not in tests/corpus/manifest.json:\n" + string.Join("\n", unlisted));
    }

    [Fact]
    public void The_manifest_lists_its_files_once_each_in_ordinal_order()
    {
        var paths = Manifest.Files.Select(f => f.Path).ToList();

        Assert.Equal(paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), paths);
    }

    [Fact]
    public void The_build_compiles_nothing_from_the_corpus()
    {
        // The corpus holds a .sqlproj, which must stay data: the solution names every project it builds.
        var solution = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "Haruspex.slnx"));

        Assert.DoesNotContain("tests/corpus", solution.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

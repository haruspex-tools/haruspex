namespace Haruspex.Tests.Corpus;

/// <summary>
/// <c>tests/corpus/fetched.json</c> says enough to fetch each corpus exactly and read it, without the corpus being here: these
/// tests run on every build, fetched or not. That the fetched files match it is <see cref="CorpusHarvestTests"/>'s.
/// </summary>
public class FetchedCorpusManifestTests
{
    public static TheoryData<string> Ids { get; } = [.. FetchedCorpus.All.Select(c => c.Id)];

    private static FetchedCorpus Corpus(string id) => FetchedCorpus.All.Single(c => c.Id == id);

    [Fact]
    public void Each_corpus_has_its_own_id_and_name()
    {
        Assert.Equal(FetchedCorpus.All.Count, FetchedCorpus.All.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(FetchedCorpus.All.Count, FetchedCorpus.All.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(FetchedCorpus.All, c => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", c.Id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_corpus_is_pinned_to_a_full_commit_of_a_public_repository(string id)
    {
        var corpus = Corpus(id);

        Assert.Matches("^[0-9a-f]{40}$", corpus.Commit);
        Assert.Matches("^https://github\\.com/[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", corpus.Repository);
        Assert.NotEmpty(corpus.Committed);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_corpus_fetches_its_licence_and_notices_with_its_code(string id)
    {
        var corpus = Corpus(id);
        var fetched = corpus.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(corpus.Licence.Spdx);
        Assert.Contains(corpus.Licence.File, fetched);
        Assert.All(corpus.Licence.Notices, notice => Assert.Contains(notice, fetched));
        Assert.All(corpus.Licence.Notices.Append(corpus.Licence.File), path => Assert.Contains(path, corpus.Take));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_corpus_lists_what_it_fetched_once_each_in_ordinal_order(string id)
    {
        var corpus = Corpus(id);
        var paths = corpus.Files.Select(f => f.Path).ToList();

        Assert.NotEmpty(paths);
        Assert.Equal(paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), paths);
        Assert.All(corpus.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
        // What fetch.mjs takes: each file is a file named, or a script or project below a folder named.
        Assert.All(paths, path => Assert.Contains(corpus.Take, t => t.EndsWith('/') ? path.StartsWith(t, StringComparison.Ordinal) : path == t));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Every_script_fetched_is_read_or_left_out_with_a_reason(string id)
    {
        var corpus = Corpus(id);
        var scripts = corpus.Scripts.ToHashSet(StringComparer.Ordinal);
        var leftOut = corpus.LeftOut.SelectMany(o => o.Files).ToList();
        var projectFolders = corpus.Read.Where(d => d.Project is not null).Select(d => FolderOf(d.Project!)).ToList();
        var folders = corpus.Read.Where(d => d.Folder is not null).Select(d => d.Folder!).ToList();

        Assert.All(corpus.Read, d => Assert.True((d.Project is null) != (d.Folder is null), $"{id}: a database read names a project or a folder, not both or neither."));
        Assert.All(corpus.Read.Where(d => d.Project is not null), d => Assert.Contains(d.Project!, corpus.Files.Select(f => f.Path)));
        // A project decides which of its scripts are read, so a script below one is accounted for by it.
        Assert.All(scripts, s => Assert.True(
            projectFolders.Concat(folders).Any(f => FetchedCorpus.IsBelow(s, f)) || leftOut.Contains(s),
            $"{id}: {s} is neither read nor left out."));
        Assert.All(leftOut, s => Assert.Contains(s, scripts));
        Assert.All(leftOut, s => Assert.DoesNotContain(projectFolders, f => FetchedCorpus.IsBelow(s, f)));
        Assert.All(corpus.LeftOut, o => Assert.NotEmpty(o.Files));
        Assert.Equal(leftOut.Count, leftOut.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_corpus_says_what_it_is_for_the_figures_page(string id) => Assert.NotEmpty(Corpus(id).About);

    [Fact]
    public void The_corpora_are_fetched_into_the_folder_the_variable_names_or_the_cache()
    {
        Assert.Equal("/elsewhere", FetchedCorpus.Root(v => v == FetchedCorpus.FolderVariable ? "/elsewhere" : null));
        Assert.EndsWith(Path.Combine(".cache", "haruspex", "corpora"), FetchedCorpus.Root(_ => null), StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine(".cache", "haruspex", "corpora"), FetchedCorpus.Root(_ => ""), StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_folder_for_the_corpora_is_refused()
    {
        // The fetch script and the test host run in different working directories, so a relative folder would name two places.
        var refused = Assert.Throws<InvalidOperationException>(() => FetchedCorpus.Root(v => v == FetchedCorpus.FolderVariable ? "cache" : null));

        Assert.Contains(FetchedCorpus.FolderVariable, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_that_differs_from_the_manifest_is_reported_file_by_file()
    {
        var folder = Directory.CreateTempSubdirectory("haruspex-corpus-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "kept.sql"), "SELECT 1;");
            File.WriteAllText(Path.Combine(folder, "changed.sql"), "SELECT 2;");
            File.WriteAllText(Path.Combine(folder, "extra.sql"), "SELECT 3;");
            var corpus = FetchedCorpus.All[0] with
            {
                Files =
                [
                    new("changed.sql", 9, FileHash.Sha256(Path.Combine(folder, "kept.sql"))),
                    new("kept.sql", 9, FileHash.Sha256(Path.Combine(folder, "kept.sql"))),
                    new("missing.sql", 9, FileHash.Sha256(Path.Combine(folder, "kept.sql"))),
                ],
            };

            Assert.Equal(
                ["changed.sql: differs from the manifest", "extra.sql: not in the manifest", "missing.sql: missing"],
                corpus.Problems(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Nothing_fetched_is_committed()
    {
        // The fetch script writes outside the checkout; a corpus copied in would be vendored without its licence review.
        var tracked = Directory.EnumerateDirectories(Path.Combine(RepositoryPaths.Root, "tests", "corpus")).Select(Path.GetFileName);

        Assert.Equal(["sql-server-samples"], tracked);
    }

    private static string FolderOf(string path) => path.Contains('/', StringComparison.Ordinal) ? path[..path.LastIndexOf('/')] : "";
}

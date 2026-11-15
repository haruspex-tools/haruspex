using System.Text.Json;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Tests.Corpus;

/// <summary>
/// A public corpus that is fetched, not vendored: one entry of <c>tests/corpus/fetched.json</c>, which
/// <c>tests/corpus/fetch.mjs</c> reads to fetch it into a folder outside the checkout, and which says how the tests read it.
/// Nothing from it is committed but counts (<c>docs/corpus-harvest.md</c>), so the repository holds none of its code, whatever
/// its licence (decision 0020; <c>tests/corpus/README.md</c>).
/// </summary>
/// <param name="Id">A short name for its folder in the cache and on the fetch script's command line.</param>
/// <param name="Name">Its name on the figures page, as its authors give it.</param>
/// <param name="Kind">Whether it is an application's database, measured by coverage, or a tool.</param>
/// <param name="Repository">The upstream repository's URL.</param>
/// <param name="Commit">The full SHA of the commit fetched.</param>
/// <param name="Committed">When that commit was made, as git gives it.</param>
/// <param name="Licence">The licence it is under, and the files that state it and its notices, all fetched with it.</param>
/// <param name="Take">The upstream paths fetched: a file, or a folder (ending in <c>/</c>) whose scripts and projects are taken.</param>
/// <param name="Read">How its code is read: each entry a database read on its own, the figures summed.</param>
/// <param name="LeftOut">Scripts fetched but not read, in groups that share a reason.</param>
/// <param name="About">What it is and what it exercises, for the figures page, in Markdown.</param>
/// <param name="Files">Every file fetched, in ordinal order of its upstream path, with its length and SHA-256.</param>
internal sealed record FetchedCorpus(
    string Id,
    string Name,
    FetchedCorpusKind Kind,
    string Repository,
    string Commit,
    string Committed,
    FetchedCorpus.LicenceFiles Licence,
    IReadOnlyList<string> Take,
    IReadOnlyList<FetchedCorpus.Database> Read,
    IReadOnlyList<FetchedCorpus.Omission> LeftOut,
    string About,
    IReadOnlyList<FetchedCorpus.FetchedFile> Files)
{
    /// <summary>The manifest, relative to the repository's root.</summary>
    public const string ManifestPath = "tests/corpus/fetched.json";

    /// <summary>The variable naming the folder the corpora are fetched into, which <c>fetch.mjs</c> reads too.</summary>
    public const string FolderVariable = "HARUSPEX_CORPORA";

    /// <summary>The variable that turns a corpus not fetched into a failure rather than a skip, as CI sets it.</summary>
    public const string RequireVariable = "HARUSPEX_REQUIRE_CORPORA";

    /// <summary>The corpora, in the manifest's order, which is the figures page's.</summary>
    public static IReadOnlyList<FetchedCorpus> All { get; } = Load();

    /// <summary>
    /// The folder the corpora are fetched into: <c>HARUSPEX_CORPORA</c>, or <c>~/.cache/haruspex/corpora</c>. The fetch script
    /// follows the same rule, so the tests look where it writes.
    /// </summary>
    public static string Root(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment(FolderVariable) is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "haruspex", "corpora");
    }

    /// <summary>This corpus's folder in the cache, holding its files at their upstream paths.</summary>
    public string Folder => Path.Combine(Root(Environment.GetEnvironmentVariable), Id);

    /// <summary>A fetched file's full path.</summary>
    public string PathOf(string upstreamPath) => Path.Combine(Folder, upstreamPath);

    /// <summary>The scripts fetched, as upstream paths, in ordinal order.</summary>
    public IEnumerable<string> Scripts => Files.Select(f => f.Path).Where(p => p.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What is wrong with the folder against the manifest: files missing, changed, or not listed. Empty when the folder is
    /// exactly what the pinned commit holds.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var listed = Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(Folder, p).Replace('\\', '/')).Order(StringComparer.Ordinal))
        {
            if (!listed.Remove(path, out var file))
                problems.Add($"{path}: not in the manifest");
            else if (FileHash.Sha256(PathOf(path)) != file.Sha256)
                problems.Add($"{path}: differs from the manifest");
        }

        problems.AddRange(listed.Keys.Order(StringComparer.Ordinal).Select(path => $"{path}: missing"));
        return problems;
    }

    /// <summary>
    /// Skips the calling test when this corpus hasn't been fetched, saying how to fetch it, or fails it when
    /// <c>HARUSPEX_REQUIRE_CORPORA=1</c>, as CI sets it. A folder that is there but not what the manifest says fails: a stale
    /// fetch would otherwise measure another commit than the one the page names.
    /// </summary>
    public void Require() => RequireAll([this]);

    /// <summary>As <see cref="Require"/>, for several corpora at once, naming every one missing.</summary>
    public static void RequireAll(IReadOnlyList<FetchedCorpus> corpora)
    {
        ArgumentNullException.ThrowIfNull(corpora);
        var missing = corpora.Where(c => !Directory.Exists(c.Folder)).Select(c => c.Id).ToList();
        if (missing.Count > 0)
        {
            var how = $"Fetch {(missing.Count == 1 ? "it" : "them")} with `node tests/corpus/fetch.mjs` (tests/corpus/README.md).";
            var what = $"{(missing.Count == 1 ? "Corpus" : "Corpora")} {string.Join(", ", missing)} not fetched into {Root(Environment.GetEnvironmentVariable)}.";
            if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
                Assert.Fail($"{RequireVariable}=1: {what} {how}");
            Assert.Skip($"{what} {how}");
        }

        foreach (var corpus in corpora)
        {
            var problems = corpus.Problems();
            Assert.True(
                problems.Count == 0,
                $"{corpus.Folder} is not what {ManifestPath} pins; fetch it again with `node tests/corpus/fetch.mjs {corpus.Id}`:\n" + string.Join("\n", problems));
        }
    }

    /// <summary>
    /// Runs haruspex over each database read: every procedure in every schema, inferred with no config file. Names for generated
    /// code are not settled, since they don't change what is typed.
    /// </summary>
    public FetchedCorpusRun Run()
    {
        var diagnostics = new List<Diagnostic>();
        var documents = new List<IrDocument>();
        foreach (var database in Read)
        {
            var (model, read) = database.Project is { } project
                ? FromProject(PathOf(project))
                : FromFolder(database.Folder ?? throw new InvalidOperationException($"{Id}: a database read names neither a folder nor a project."));
            diagnostics.AddRange(read);
            documents.Add(IrInference.Infer(model, [.. model.Procedures.Select(p => p.Name)]).Value);
        }

        return new FetchedCorpusRun(this, diagnostics, documents);
    }

    /// <summary>The scripts of a folder read: every script fetched below it, less those left out.</summary>
    public IReadOnlyList<string> ScriptsBelow(string folder)
    {
        var leftOut = LeftOut.SelectMany(o => o.Files).ToHashSet(StringComparer.Ordinal);
        return [.. Scripts.Where(s => IsBelow(s, folder) && !leftOut.Contains(s))];
    }

    /// <summary>Whether an upstream path is below a folder, the empty folder being the repository's root.</summary>
    public static bool IsBelow(string path, string folder) =>
        folder.Length == 0 || path.StartsWith(folder + "/", StringComparison.Ordinal);

    private static (DatabaseModel, IReadOnlyList<Diagnostic>) FromProject(string path)
    {
        var read = SqlProjectReader.Read(path);
        return (read.Model, read.Diagnostics);
    }

    private (DatabaseModel, IReadOnlyList<Diagnostic>) FromFolder(string folder)
    {
        var scripts = ScriptsBelow(folder).Select(s => folder.Length == 0 ? s : s[(folder.Length + 1)..]);
        var read = SqlScriptFolder.Read(PathOf(folder), scripts);
        return (read.Model, read.Diagnostics);
    }

    private static List<FetchedCorpus> Load()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root, ManifestPath)));
        static string Text(JsonElement e, string name) => e.GetProperty(name).GetString()
            ?? throw new InvalidOperationException($"{ManifestPath}: {name} is null.");
        static List<string> Texts(JsonElement e, string name) => [.. e.GetProperty(name).EnumerateArray().Select(t => t.GetString()
            ?? throw new InvalidOperationException($"{ManifestPath}: {name} holds a null."))];
        static string? Optional(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value.GetString() : null;

        return [.. json.RootElement.GetProperty("corpora").EnumerateArray().Select(c =>
        {
            var licence = c.GetProperty("licence");
            return new FetchedCorpus(
                Text(c, "id"),
                Text(c, "name"),
                Enum.Parse<FetchedCorpusKind>(Text(c, "kind"), ignoreCase: true),
                Text(c, "repository"),
                Text(c, "commit"),
                Text(c, "committed"),
                new LicenceFiles(Text(licence, "spdx"), Text(licence, "file"), Texts(licence, "notices")),
                Texts(c, "take"),
                [.. c.GetProperty("read").EnumerateArray().Select(d => new Database(Optional(d, "project"), Optional(d, "folder")))],
                [.. c.GetProperty("leftOut").EnumerateArray().Select(o => new Omission(Texts(o, "files"), Text(o, "why")))],
                Text(c, "about"),
                [.. c.GetProperty("files").EnumerateArray().Select(f => new FetchedFile(Text(f, "path"), f.GetProperty("bytes").GetInt64(), Text(f, "sha256")))]);
        })];
    }

    /// <summary>The licence: its SPDX identifier, the file that states it, and any notices that must travel with the code.</summary>
    /// <param name="Spdx">The SPDX identifier.</param>
    /// <param name="File">The licence's upstream path, fetched with the code.</param>
    /// <param name="Notices">Upstream paths of notices the licence asks to be kept with the code, fetched with it.</param>
    internal sealed record LicenceFiles(string Spdx, string File, IReadOnlyList<string> Notices);

    /// <summary>One database of the corpus, read from a project or from a folder of scripts: one of the two is given.</summary>
    /// <param name="Project">The upstream path of a <c>.sqlproj</c>, read as <c>haruspex generate</c> reads a project.</param>
    /// <param name="Folder">The upstream path of a folder, whose scripts are read as a folder's, less those left out.</param>
    internal sealed record Database(string? Project = null, string? Folder = null);

    /// <summary>Scripts fetched but not read, and why.</summary>
    /// <param name="Files">Their upstream paths, in ordinal order.</param>
    /// <param name="Why">The reason, for the figures page.</param>
    internal sealed record Omission(IReadOnlyList<string> Files, string Why);

    /// <summary>One fetched file: its upstream path, its length and its SHA-256, in lower-case hex.</summary>
    internal sealed record FetchedFile(string Path, long Bytes, string Sha256);
}

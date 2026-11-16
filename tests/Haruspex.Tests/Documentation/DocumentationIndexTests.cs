namespace Haruspex.Tests.Documentation;

/// <summary>
/// The documentation's index, <c>docs/README.md</c>, lists every page a reader may need, and the project's README links it,
/// so that a page added later can't be left where nothing leads to it.
/// </summary>
/// <remarks>
/// A page is a Markdown file at the repository's root or directly in <c>docs/</c>, and the README of each folder in
/// <c>docs/</c>, which lists that folder's own pages (the guide's chapters, the decisions, the diagnostics). <c>CLAUDE.md</c> is
/// not a page: it only points one agent at <c>AGENTS.md</c>, which is listed.
/// </remarks>
public class DocumentationIndexTests
{
    private const string Index = "docs/README.md";

    private static readonly HashSet<string> NotPages = new(StringComparer.Ordinal) { "CLAUDE.md", Index };

    private static readonly List<string> All =
    [
        .. Directory.EnumerateFiles(RepositoryPaths.Root, "*.md")
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "docs"), "*.md"))
            .Concat(Directory.EnumerateDirectories(Path.Combine(RepositoryPaths.Root, "docs")).Select(d => Path.Combine(d, "README.md")).Where(File.Exists))
            .Select(MarkdownText.Relative)
            .Where(p => !NotPages.Contains(p))
            .Order(StringComparer.Ordinal),
    ];

    public static TheoryData<string> Pages { get; } = [.. All];

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_page_is_listed_in_the_documentation_index(string page) =>
        Assert.True(LinkedFrom(Index).Contains(page), $"{Index} doesn't link {page}.");

    [Fact]
    public void The_readme_links_the_documentation_index() =>
        Assert.Contains(Index, LinkedFrom("README.md"));

    [Fact]
    public void The_pages_include_the_security_policy_and_the_guide() =>
        Assert.Superset(new HashSet<string>(StringComparer.Ordinal) { "SECURITY.md", "docs/internals/README.md", "docs/configuration.md" }, All.ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// The files a Markdown file links to, read as <see cref="MarkdownLinkTests"/> reads its links, relative to the repository's
    /// root; a link to a folder is to its README.
    /// </summary>
    private static HashSet<string> LinkedFrom(string file)
    {
        var folder = Path.GetDirectoryName(Path.Combine(RepositoryPaths.Root, file))!;
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, link) in MarkdownLinkTests.Links(File.ReadAllText(Path.Combine(RepositoryPaths.Root, file))))
        {
            var path = link.Split('#')[0];
            if (path.Length == 0)
                continue;
            var target = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(path)));
            linked.Add(MarkdownText.Relative(Directory.Exists(target) ? Path.Combine(target, "README.md") : target));
        }

        return linked;
    }
}

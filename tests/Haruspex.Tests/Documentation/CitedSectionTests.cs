using System.Globalization;
using System.Text.RegularExpressions;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// A section cited by its title is there. The documentation and the code's comments point into a page by quoting a
/// section's title after naming the page (the Inference README, "Guards"; the guide's chapter 6, "What a guard proves"), and
/// a link's anchor is checked (<c>MarkdownLinkTests</c>), but a quoted title is prose, and a
/// section renamed leaves it pointing at nothing. Each quoted title after a named page must be a heading of that page, or
/// the bold label that starts one of its paragraphs or list items.
/// </summary>
/// <remarks>
/// <para>
/// A page is named as the prose names it: the Inference README (or "the inference reference", or the SQL Server front end's
/// README), the IR README, the README in this folder, chapter N (of the guide), a path to a Markdown file, or a link to one.
/// A Markdown page may also cite its own sections: "X", below; "X" above; see "X". A title is in straight double quotes and
/// doesn't start with a lower-case letter, so a quoted value (<c>"strip"</c>) is not taken for one; several may follow one
/// name, separated by commas or <i>and</i>. In C#, only comments are read.
/// </para>
/// <para>
/// A title matches a heading or bold label when it is the same text, backticks and a closing full stop or colon left out, or
/// the start of it up to a colon or an opening parenthesis: "Facets" cites "Facets: what a scalar's values are guaranteed to
/// be", and "How many rows" cites "How many rows (<c>Cardinality.cs</c>)".
/// </para>
/// </remarks>
public partial class CitedSectionTests
{
    private const string Inference = "src/Haruspex.SqlServer.Inference/README.md";
    private const string Ir = "src/Haruspex.Ir/README.md";

    public static TheoryData<string> Files { get; } = [.. Read().Select(f => f.Path)];

    [Fact]
    public void The_documentation_holds_cited_titles_to_check()
    {
        var count = Read().Sum(f => Citations(f).Count());

        // Over a hundred today; a fall to a handful would mean the scan stopped seeing them, not that they went away.
        Assert.True(count > 100, $"Only {count.ToString(CultureInfo.InvariantCulture)} cited titles found.");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_title_cited_after_a_page_is_a_section_of_that_page(string path)
    {
        var file = Read().Single(f => f.Path == path);
        var problems = new List<string>();
        foreach (var (line, page, title) in Citations(file))
        {
            var at = $"{path}:{line.ToString(CultureInfo.InvariantCulture)}";
            if (page is null || !File.Exists(Path.Combine(RepositoryPaths.Root, page)))
                problems.Add($"{at}: \"{title}\" is cited after a page that isn't there ({page ?? "no such chapter"}).");
            else if (!Sections(page).Any(s => Cites(title, s)))
                problems.Add($"{at}: {page} has no section \"{title}\"; cite one of its headings, or the bold label of a paragraph.");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("docs/README.md", "See the Inference README, \"Guards\".", Inference, "Guards")]
    [InlineData("docs/README.md", "the IR's README (\"Results\")", Ir, "Results")]
    [InlineData("docs/README.md", "the inference reference's \"Query files\" and \"SELECT *\"", Inference, "Query files|SELECT *")]
    [InlineData("docs/README.md", "the guide's chapter 6, \"What a guard proves\"", "docs/internals/06-flow-joins-and-fixed-points.md", "What a guard proves")]
    [InlineData("docs/internals/08-the-sql-server-type-system-as-data.md", "[Values](05-values-as-a-lattice.md), \"A knowledge join is not a type rule\"", "docs/internals/05-values-as-a-lattice.md", "A knowledge join is not a type rule")]
    [InlineData("docs/decisions/0001-infer-from-source-never-a-live-database.md", "[the guide, \"Binding\"](../internals/03-binding.md)", "docs/internals/03-binding.md", "Binding")]
    [InlineData("src/Haruspex.Emit.JsonSchema/Lowering/Scalars.cs", "/// the SQL Server front end's README (\"Scalars in JSON\") lists it.", Inference, "Scalars in JSON")]
    [InlineData("src/Haruspex.SqlServer.Inference/ProcedureCalls.cs", "/// README.md in this folder (\"Calls\") gives the model", Inference, "Calls")]
    [InlineData("tests/x.cs", "// (src/Haruspex.Ir/README.md, \"Facets\")", Ir, "Facets")]
    [InlineData("docs/README.md", "the column's collation (\"Value sets\", below)", "docs/README.md", "Value sets")]
    [InlineData("docs/README.md", "the IR README,\n\"Paths\", says so", Ir, "Paths")]
    public void A_title_cited_after_a_page_is_found(string path, string text, string page, string titles) =>
        Assert.Equal(
            titles.Split('|').Select(t => (page, t)),
            Citations(new SourceFile(path, text.Split('\n'))).Select(c => (c.Page!, c.Title)));

    [Theory]
    [InlineData("docs/README.md", "the IR README says \"must not\" there")]
    [InlineData("docs/README.md", "the IR README, \"strip\" or \"strict\"")]
    [InlineData("docs/internals/03-binding.md", "Andrew W. Appel, *Modern Compiler Implementation in ML* (Cambridge University Press, 1998), chapter 5, \"Semantic analysis\"")]
    [InlineData("tests/x.cs", "var s = \"the IR README, \\\"Facets\\\"\";")]
    public void A_quote_that_is_not_a_cited_title_is_not_checked(string path, string text) =>
        Assert.Empty(Citations(new SourceFile(path, text.Split('\n'))));

    [Theory]
    [InlineData("Facets", "Facets: what a scalar's values are guaranteed to be")]
    [InlineData("How many rows", "How many rows (`Cardinality.cs`)")]
    [InlineData("SELECT *", "`SELECT *`")]
    [InlineData("Why these lines", "Why these lines:")]
    [InlineData("Unknowns stay local", "Unknowns stay local.")]
    public void A_title_cites_a_section_that_starts_with_it(string title, string section) => Assert.True(Cites(title, section));

    [Theory]
    [InlineData("Versioning", "Changelog")]
    [InlineData("Why the server's rule is not a join", "A knowledge join is not a type rule")]
    [InlineData("Facet", "Facets: what a scalar's values are guaranteed to be")]
    [InlineData("Scalars", "Scalars in JSON")]
    public void A_title_does_not_cite_a_section_it_does_not_name(string title, string section) => Assert.False(Cites(title, section));

    /// <summary>A file read for citations: its path from the repository root, and its prose line by line (a C# file's comments, the rest blank).</summary>
    private sealed record SourceFile(string Path, IReadOnlyList<string> Lines);

    /// <summary>The Markdown documentation and every C# file, each read once.</summary>
    private static List<SourceFile> Read() => cache ??=
    [
        .. MarkdownText.Documentation.Select(p => new SourceFile(p, File.ReadAllLines(Path.Combine(RepositoryPaths.Root, p)))),
        .. CodeReferences.SourceFiles().Select(p => new SourceFile(MarkdownText.Relative(p), File.ReadAllLines(p))),
    ];

    private static List<SourceFile>? cache;

    /// <summary>
    /// Each title <paramref name="file"/> cites, with its line (1-based) and the page cited (null for a chapter that has no
    /// file). The file's lines are read as one text, so that a citation broken over two lines is found.
    /// </summary>
    private static IEnumerable<(int Line, string? Page, string Title)> Citations(SourceFile file)
    {
        var csharp = file.Path.EndsWith(".cs", StringComparison.Ordinal);
        var lines = file.Lines.Select(l => csharp ? CommentText(l) : l).ToList();
        var starts = new List<int>();
        var text = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            starts.Add(text.Length);
            text.Append(line).Append(' ');
        }

        var all = text.ToString();
        int LineOf(int index) => starts.FindLastIndex(s => s <= index) + 1;
        var folder = Path.GetDirectoryName(file.Path)!.Replace('\\', '/');

        foreach (Match m in Named().Matches(all))
        {
            var page = Page(m, folder);
            if (page is NotAPage)
                continue;
            foreach (Capture title in m.Groups["title"].Captures)
                yield return (LineOf(title.Index), page, Collapsed(title.Value));
        }

        // A link whose own text quotes the title, as a decision's "See also" links a chapter.
        foreach (Match m in TitledLink().Matches(all))
        {
            foreach (Capture title in m.Groups["title"].Captures)
                yield return (LineOf(title.Index), Joined(folder, m.Groups["path"].Value), Collapsed(title.Value));
        }

        if (csharp)
            yield break;

        // A page's own sections: "X", below; ("X" above); see "X".
        foreach (Match m in OwnSection().Matches(all))
            yield return (LineOf(m.Groups["title"].Index), file.Path, Collapsed(m.Groups["title"].Value));
    }

    /// <summary>What stands for "not a page of this repository": a chapter of a book the guide cites, not of the guide.</summary>
    private const string NotAPage = "\0";

    /// <summary>The page a name in <paramref name="m"/> stands for, from a file in <paramref name="folder"/>.</summary>
    private static string? Page(Match m, string folder)
    {
        if (m.Groups["inference"].Success)
            return Inference;
        if (m.Groups["ir"].Success)
            return Ir;
        if (m.Groups["here"].Success)
            return folder + "/README.md";
        if (m.Groups["path"].Success)
            return m.Groups["path"].Value;
        if (m.Groups["link"].Success)
            return Joined(folder, m.Groups["link"].Value);
        if (m.Groups["book"].Success)
            return NotAPage;
        var chapter = int.Parse(m.Groups["chapter"].Value, CultureInfo.InvariantCulture).ToString("00", CultureInfo.InvariantCulture);
        return Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "docs", "internals"), chapter + "-*.md")
            .Select(MarkdownText.Relative)
            .SingleOrDefault();
    }

    /// <summary>A link's target, from a file in <paramref name="folder"/>, as a path from the repository root.</summary>
    private static string Joined(string folder, string link) =>
        MarkdownText.Relative(Path.GetFullPath(Path.Combine(RepositoryPaths.Root, folder, link)));

    /// <summary>The headings of <paramref name="page"/> and the bold labels that start its paragraphs and list items, outside code.</summary>
    private static List<string> Sections(string page)
    {
        var sections = new List<string>();
        var fenced = false;
        foreach (var line in File.ReadLines(Path.Combine(RepositoryPaths.Root, page)))
        {
            if (MarkdownText.IsFence(line))
                fenced = !fenced;
            else if (!fenced && Section().Match(line) is { Success: true } m)
                sections.Add(m.Groups["text"].Value);
        }

        return sections;
    }

    /// <summary>Whether <paramref name="title"/> cites <paramref name="section"/>: the same words, or the start of them.</summary>
    private static bool Cites(string title, string section)
    {
        var plain = section.Replace("`", "", StringComparison.Ordinal).Trim().TrimEnd('.', ':');
        var cited = title.Replace("`", "", StringComparison.Ordinal).Trim();
        return plain == cited
            || plain.StartsWith(cited + ": ", StringComparison.Ordinal)
            || plain.StartsWith(cited + " (", StringComparison.Ordinal);
    }

    /// <summary>The text of a C# line's comment, or nothing when the line is code.</summary>
    private static string CommentText(string line) => Comment().Match(line) is { Success: true } m ? m.Groups["text"].Value : "";

    private static string Collapsed(string title) => Spaces().Replace(title, " ");

    /// <summary>A title in quotes: not starting with a lower-case letter, a space or a quote.</summary>
    private const string Title = @"""(?<title>[^""a-z\s][^""]{0,80})""";

    [GeneratedRegex(
        @"(?:(?<inference>\b(?:(?:Inference|inference) (?:README|reference)|SQL Server front end's README))"
        + @"|(?<ir>\bIR(?:'s)? README)"
        + @"|(?<here>\bREADME(?:\.md)? in this folder)"
        + @"|(?<book>\b\d{4}\), chapter \d{1,2})"
        + @"|(?:\b(?:Chapter|chapter) (?<chapter>\d{1,2})(?: of the guide)?)"
        + @"|(?<path>\b(?:src|docs|tests)/[A-Za-z0-9_./-]+\.md)\b"
        + @"|\]\((?<link>[^)#\s]+\.md)(?:#[^)]*)?\))"
        + @"(?:'s)?[,\s]*\(?\s*" + Title + @"(?:(?:\s*,\s*|\s+and\s+)" + Title + ")*")]
    private static partial Regex Named();

    [GeneratedRegex(@"\[[^\]]*?(?:" + Title + @"[^\]""]*)+\]\((?<path>[^)#\s]+\.md)(?:#[^)]*)?\)")]
    private static partial Regex TitledLink();

    [GeneratedRegex(Title + @",? (?:below|above)\b|\bsee " + Title)]
    private static partial Regex OwnSection();

    [GeneratedRegex(@"^\s*(?:#{1,6} (?<text>.+?)\s*$|(?:[-*] |\d+\. )?\*\*(?<text>[^*]+)\*\*)")]
    private static partial Regex Section();

    [GeneratedRegex(@"^\s*(?:///?|\*)\s?(?<text>.*)$")]
    private static partial Regex Comment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

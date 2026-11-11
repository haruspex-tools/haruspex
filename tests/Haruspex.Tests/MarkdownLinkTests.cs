using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Haruspex.Tests;

/// <summary>
/// Every relative link in the repository's Markdown resolves: the file or folder it names exists, and an anchor
/// (<c>#heading</c>) names a heading of the file it points into, as GitHub renders it. The README and the docs are read
/// on GitHub before anything is run, so a link that leads nowhere is a broken promise to the first reader.
/// </summary>
/// <remarks>
/// Links to the web (any scheme, such as <c>https:</c> or <c>mailto:</c>) are not followed: whether a site answers is not
/// a property of this commit. Links inside code, fenced or inline, are text, not links. A link may not leave the
/// repository, because what is outside it is not published with it.
/// </remarks>
public partial class MarkdownLinkTests
{
    /// <summary>Folders that hold build output or other projects' files, never the repository's own Markdown.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { ".git", "bin", "obj", "node_modules" };

    private static readonly List<string> All = [.. Files(TestPaths.RepoRoot).Select(f => Path.GetRelativePath(TestPaths.RepoRoot, f).Replace('\\', '/'))];

    public static TheoryData<string> MarkdownFiles { get; } = [.. All];

    [Fact]
    public void The_repository_has_markdown_to_check() => Assert.Contains("README.md", All);

    [Theory]
    [MemberData(nameof(MarkdownFiles))]
    public void Every_relative_link_in_a_markdown_file_resolves(string file)
    {
        var path = Path.Combine(TestPaths.RepoRoot, file);
        var problems = new List<string>();
        foreach (var (line, target) in Links(File.ReadAllText(path)))
        {
            if (Problem(path, target) is { } problem)
                problems.Add($"{file}:{line.ToString(CultureInfo.InvariantCulture)}: ({target}) {problem}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("## What it deliberately doesn't do", "what-it-deliberately-doesnt-do")]
    [InlineData("## `IrJsonWriter`: the IR as JSON", "irjsonwriter-the-ir-as-json")]
    [InlineData("### Step 6c *and* after", "step-6c-and-after")]
    [InlineData("## [Linked](elsewhere.md) heading", "linked-heading")]
    [InlineData("## Scalars in JSON (0.11)", "scalars-in-json-011")]
    [InlineData("## snake_case stays", "snake_case-stays")]
    public void A_heading_s_anchor_is_the_one_github_gives_it(string heading, string anchor) =>
        Assert.Equal(anchor, Slug(heading.TrimStart('#').Trim()));

    [Fact]
    public void A_repeated_heading_s_anchor_is_numbered_from_one() =>
        Assert.Equal(["notes", "notes-1", "notes-2"], Anchors("# Notes\n## Notes\n### Notes\n").Order(StringComparer.Ordinal));

    [Fact]
    public void A_link_inside_code_is_not_a_link() =>
        Assert.Equal(["real.md"], Links("[a](real.md)\n`[b](inline.md)`\n```\n[c](fenced.md)\n```\n").Select(l => l.Target));

    private static IEnumerable<string> Files(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.md").Order(StringComparer.Ordinal))
            yield return file;
        foreach (var sub in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
        {
            if (!Skipped.Contains(Path.GetFileName(sub)))
            {
                foreach (var file in Files(sub))
                    yield return file;
            }
        }
    }

    /// <summary>Why the link from <paramref name="from"/> to <paramref name="target"/> leads nowhere; null when it resolves.</summary>
    private static string? Problem(string from, string target)
    {
        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var pathPart = Uri.UnescapeDataString(hash < 0 ? target : target[..hash]);
        var anchor = hash < 0 ? null : target[(hash + 1)..];

        var resolved = pathPart.Length == 0 ? from : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(from)!, pathPart));
        var root = Path.GetFullPath(TestPaths.RepoRoot) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(root, StringComparison.Ordinal) && resolved != root.TrimEnd(Path.DirectorySeparatorChar))
            return "leads outside the repository";
        if (Directory.Exists(resolved))
            return anchor is null ? null : "puts an anchor on a folder";
        if (!File.Exists(resolved))
            return "names a file that does not exist";
        if (anchor is null)
            return null;
        if (!resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return "puts an anchor on a file that is not Markdown";
        return Anchors(File.ReadAllText(resolved)).Contains(anchor) ? null : $"names no heading of {Path.GetFileName(resolved)}";
    }

    /// <summary>The relative link targets in Markdown text, with the line each is on; code, fenced or inline, is skipped.</summary>
    private static IEnumerable<(int Line, string Target)> Links(string markdown)
    {
        var lines = markdown.Split('\n');
        var fenced = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
                continue;
            var text = InlineCode().Replace(lines[i], "");
            foreach (var m in InlineLink().Matches(text).Concat(ReferenceDefinition().Matches(text)))
            {
                var target = m.Groups["target"].Value.Trim('<', '>');
                if (!Scheme().IsMatch(target))
                    yield return (i + 1, target);
            }
        }
    }

    /// <summary>The anchors GitHub gives a Markdown file: one per heading, and any HTML <c>id</c> or <c>name</c>.</summary>
    private static HashSet<string> Anchors(string markdown)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var fenced = false;
        foreach (var line in markdown.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (!fenced && Heading().Match(line) is { Success: true } heading)
            {
                // A repeated heading gets -1, -2 and so on, in the order they appear.
                var slug = Slug(heading.Groups["text"].Value);
                var count = seen.GetValueOrDefault(slug);
                seen[slug] = count + 1;
                anchors.Add(count == 0 ? slug : $"{slug}-{count.ToString(CultureInfo.InvariantCulture)}");
            }

            foreach (Match id in HtmlAnchor().Matches(line))
                anchors.Add(id.Groups["id"].Value);
        }

        return anchors;
    }

    /// <summary>
    /// A heading's anchor as GitHub makes it: the rendered text (a link's text, not its target; code and emphasis
    /// unmarked), in lower case, keeping letters, digits, spaces, hyphens and underscores, with each space a hyphen.
    /// </summary>
    private static string Slug(string heading)
    {
        var text = InlineLink().Replace(heading, m => m.Groups["text"].Value).Trim();
        var slug = new StringBuilder();
#pragma warning disable CA1308 // GitHub lower-cases a heading to make its anchor.
        foreach (var c in text.ToLowerInvariant())
#pragma warning restore CA1308
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
                slug.Append(c);
            else if (c == ' ')
                slug.Append('-');
        }

        return slug.ToString();
    }

    [GeneratedRegex(@"(?<!\\)!?\[(?<text>[^\]]*)\]\((?<target>[^)\s]+)(\s+""[^""]*"")?\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex(@"^\s{0,3}\[[^\]]+\]:\s+(?<target>\S+)")]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"`[^`]*`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"<a\s+(?:id|name)=""(?<id>[^""]+)""")]
    private static partial Regex HtmlAnchor();
}

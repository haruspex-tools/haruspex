namespace Haruspex.Tests;

/// <summary>
/// The repository's Markdown, read as the documentation tests need it: which files there are, and the code in each,
/// fenced or inline, with the line it is on. One reading, so that the link test, the code reference test and the worked
/// example test agree on what is code and what is prose.
/// </summary>
internal static class MarkdownText
{
    /// <summary>Folders that hold build output or other projects' files, never the repository's own Markdown.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { ".git", "bin", "obj", "node_modules" };

    /// <summary>
    /// The Markdown that documents haruspex, as paths relative to the repository root: every <c>.md</c> file but the house
    /// style guide, whose examples are there to show a rule, and the prose checks' fixtures, which break rules on purpose.
    /// </summary>
    public static IReadOnlyList<string> Documentation { get; } =
    [
        .. Files(RepositoryPaths.Root)
            .Select(Relative)
            .Where(f => f != "docs/style.md" && !f.StartsWith("tests/prose/fixtures/", StringComparison.Ordinal)),
    ];

    /// <summary>Every Markdown file under <paramref name="folder"/>, in a stable order, build output and packages left out.</summary>
    public static IEnumerable<string> Files(string folder)
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

    /// <summary>A path relative to the repository root, with <c>/</c> on every platform.</summary>
    public static string Relative(string path) => Path.GetRelativePath(RepositoryPaths.Root, path).Replace('\\', '/');

    /// <summary>Whether a line opens or closes a fenced code block.</summary>
    public static bool IsFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    /// <summary>
    /// The fenced code blocks in <paramref name="markdown"/>: the line of the opening fence (1-based), the words after the
    /// backticks (<c>sql</c>, <c>ts haruspex-zod</c>), and the text between the fences, with LF line endings.
    /// </summary>
    public static IEnumerable<FencedBlock> FencedBlocks(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsFence(lines[i]))
                continue;
            var info = lines[i].Trim().TrimStart('`').Trim();
            var start = i;
            var body = new List<string>();
            for (i++; i < lines.Length && !IsFence(lines[i]); i++)
                body.Add(lines[i]);
            yield return new FencedBlock(start + 1, info, string.Join('\n', body));
        }
    }

    /// <summary>The inline code spans outside fenced blocks, each with the line it is on (1-based).</summary>
    public static IEnumerable<(int Line, string Code)> InlineCode(string markdown)
    {
        var lines = markdown.Split('\n');
        var fenced = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsFence(lines[i]))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
                continue;
            var parts = lines[i].Split('`');
            for (var p = 1; p < parts.Length - 1; p += 2)
                yield return (i + 1, parts[p]);
        }
    }

    /// <summary>A fenced code block: where it opens, its info string, and its text.</summary>
    public sealed record FencedBlock(int Line, string Info, string Text)
    {
        /// <summary>The words of the info string: the language first, then any others.</summary>
        public IReadOnlyList<string> Words { get; } = Info.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

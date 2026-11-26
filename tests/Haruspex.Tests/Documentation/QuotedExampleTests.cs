using System.Globalization;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// A code block in the documentation that names a file after its language quotes that file: its text is in the file, line
/// for line. Two kinds of file may be quoted. A file of an example application (<c>```ts examples/greenhouse/src/watering.ts</c>)
/// is compiled by the example's own test (<c>ExampleAppTests</c>); a C# file of haruspex's own
/// (<c>```csharp src/Haruspex.Cli/ModuleRun.cs</c>) by the build. So the code a page shows is code that compiles, and a change
/// to the file that the page doesn't follow fails here. A quote may sit less indented than in its file, as a method's body
/// does when it is shown alone, by the same amount on every line.
/// </summary>
public class QuotedExampleTests
{
    /// <summary>What a quoted file's path starts with: the example applications, which their tests compile, and haruspex's code.</summary>
    private static readonly string[] Quotable = ["examples/", "src/"];

    private static readonly List<(string Page, MarkdownText.FencedBlock Block)> Quotes =
    [
        .. MarkdownText.Documentation.SelectMany(page => MarkdownText.FencedBlocks(File.ReadAllText(Path.Combine(RepositoryPaths.Root, page)))
            .Where(b => b.Words.Count == 2 && Quotable.Any(q => b.Words[1].StartsWith(q, StringComparison.Ordinal)))
            .Select(b => (page, b))),
    ];

    public static TheoryData<string, int> Blocks { get; } = [.. Quotes.Select(q => (q.Page, q.Block.Line))];

    [Fact]
    public void The_layers_page_quotes_its_example_from_the_greenhouse_application() =>
        Assert.Contains(Quotes, q => q.Page == "docs/layers.md" && q.Block.Words[1] == "examples/greenhouse/src/watering.ts");

    [Fact]
    public void The_guide_quotes_the_pipeline_from_the_command_line_s_code()
    {
        const string Page = "docs/internals/12-from-input-to-output.md";
        Assert.Contains(Quotes, q => q.Page == Page && q.Block.Words[1] == "src/Haruspex.Cli/ModuleRun.cs");
        Assert.Contains(Quotes, q => q.Page == Page && q.Block.Words[1] == "src/Haruspex.Cli/Generate.cs");
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_block_naming_a_file_is_text_of_that_file(string page, int line)
    {
        var block = Quotes.Single(q => q.Page == page && q.Block.Line == line).Block;
        var path = Path.Combine(RepositoryPaths.Root, block.Words[1]);
        var at = $"{page}:{line.ToString(CultureInfo.InvariantCulture)}";

        Assert.True(File.Exists(path), $"{at} names {block.Words[1]}, which doesn't exist.");
        Assert.True(
            Holds(File.ReadAllText(path), block.Text),
            $"{at} quotes {block.Words[1]}, but the file doesn't hold that text; copy it from the file again.");
    }

    [Theory]
    [InlineData("a\n    b\n    c\nd", "b\nc")]
    [InlineData("a\n    b\n\n    c\nd", "b\n\nc")]
    [InlineData("a\nb\nc", "a\nb\nc")]
    public void A_quote_is_whole_lines_of_the_file_indented_alike(string file, string quote) => Assert.True(Holds(file, quote));

    [Theory]
    [InlineData("a\n    b\n      c\nd", "b\nc")]
    [InlineData("a\n    bb\n    c\nd", "b\nc")]
    [InlineData("a\n  x b\n    c\nd", "b\nc")]
    [InlineData("a\nb\nc", "b\nd")]
    public void A_quote_that_is_not_whole_lines_of_the_file_indented_alike_is_refused(string file, string quote) => Assert.False(Holds(file, quote));

    /// <summary>
    /// Whether <paramref name="file"/> holds <paramref name="quote"/> as whole lines: the quote's first line ends one of the
    /// file's, whose start before it is only spaces, and every later line is that file line's successor with the same spaces
    /// before it (an empty line may be empty in both).
    /// </summary>
    private static bool Holds(string file, string quote)
    {
        var lines = file.ReplaceLineEndings("\n").Split('\n');
        var quoted = quote.Split('\n');
        for (var i = 0; i + quoted.Length <= lines.Length; i++)
        {
            if (!lines[i].EndsWith(quoted[0], StringComparison.Ordinal))
                continue;
            var indent = lines[i][..^quoted[0].Length];
            if (indent.Any(c => c != ' '))
                continue;
            if (quoted.Select((q, j) => q.Length == 0 ? lines[i + j].Trim().Length == 0 : lines[i + j] == indent + q).All(same => same))
                return true;
        }

        return false;
    }
}

using System.Globalization;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// A code block in the documentation that names a file of an example application after its language
/// (<c>```ts examples/greenhouse/src/watering.ts</c>) quotes that file: its text is in the file, line for line. The example's
/// own test compiles the file (<c>ExampleAppTests</c>), so the code a page shows is code that compiles, and a change to the
/// file that the page doesn't follow fails here.
/// </summary>
public class QuotedExampleTests
{
    /// <summary>What a quoted file's path starts with: the example applications, which their tests compile.</summary>
    private const string Examples = "examples/";

    private static readonly List<(string Page, MarkdownText.FencedBlock Block)> Quotes =
    [
        .. MarkdownText.Documentation.SelectMany(page => MarkdownText.FencedBlocks(File.ReadAllText(Path.Combine(RepositoryPaths.Root, page)))
            .Where(b => b.Words.Count == 2 && b.Words[1].StartsWith(Examples, StringComparison.Ordinal))
            .Select(b => (page, b))),
    ];

    public static TheoryData<string, int> Blocks { get; } = [.. Quotes.Select(q => (q.Page, q.Block.Line))];

    [Fact]
    public void The_layers_page_quotes_its_example_from_the_greenhouse_application() =>
        Assert.Contains(Quotes, q => q.Page == "docs/layers.md" && q.Block.Words[1] == "examples/greenhouse/src/watering.ts");

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_block_naming_an_example_s_file_is_text_of_that_file(string page, int line)
    {
        var block = Quotes.Single(q => q.Page == page && q.Block.Line == line).Block;
        var path = Path.Combine(RepositoryPaths.Root, block.Words[1]);
        var at = $"{page}:{line.ToString(CultureInfo.InvariantCulture)}";

        Assert.True(File.Exists(path), $"{at} names {block.Words[1]}, which doesn't exist.");
        Assert.True(
            // Whole lines: the block's first line starts one of the file's, and its last ends one.
            ("\n" + File.ReadAllText(path).ReplaceLineEndings("\n")).Contains("\n" + block.Text + "\n", StringComparison.Ordinal),
            $"{at} quotes {block.Words[1]}, but the file doesn't hold that text; copy it from the file again.");
    }
}

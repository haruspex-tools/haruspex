using System.Text.RegularExpressions;
using Haruspex.Ir;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// Every code that can reach the IR as a reason (each null rule, unknown reason and cardinality) has its three parts: a short
/// text in <see cref="IrReasons"/>, a page under <c>docs/reasons</c> named for its code, and on that page a test it rests on that
/// exists. So a new rule can't reach a user's generated code or <c>haruspex explain</c> without saying what it means and how it
/// was established (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>).
/// </summary>
public partial class ReasonCatalogueTests
{
    private static string Folder => Path.Combine(RepositoryPaths.Root, "docs", "reasons");

    /// <summary>Every code, with its short text, as the IR writes it.</summary>
    public static TheoryData<string, string> Codes { get; } = [.. All()];

    private static IEnumerable<(string Code, string Text)> All() =>
    [
        .. Enum.GetValues<IrNullRule>().Select(r => (IrWireNames.Of(r), IrReasons.Text(r))),
        .. Enum.GetValues<IrUnknownReason>().Select(r => (IrWireNames.Of(r), IrReasons.Text(r))),
        .. Enum.GetValues<IrCardinality>().Select(c => (IrWireNames.Of(c), IrReasons.Text(c))),
    ];

    /// <summary>The name of every test method in both test projects, as declared.</summary>
    private static readonly Lazy<HashSet<string>> TestMethods = new(() =>
    [
        .. new[] { "Haruspex.Tests", "Haruspex.DifferentialTests" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "tests", project), "*.cs", SearchOption.AllDirectories))
            .SelectMany(file => Declared().Matches(File.ReadAllText(file)).Select(m => m.Groups["name"].Value)),
    ]);

    [Theory]
    [MemberData(nameof(Codes))]
    public void Each_code_has_a_short_text_a_page_and_a_test_that_exists(string code, string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), $"{code} has no short text.");
        var page = Path.Combine(Folder, code + ".md");
        Assert.True(File.Exists(page), $"{code} has no page: docs/reasons/{code}.md.");
        var content = File.ReadAllText(page);
        Assert.Matches($"^# The (null rule|unknown reason|cardinality) `{code}`\n", content);
        var rests = Assert.Single(RestsOn().Matches(content));
        var method = rests.Groups["method"].Value;
        Assert.True(TestMethods.Value.Contains(method), $"docs/reasons/{code}.md names the test {method}, which no test project declares.");
    }

    [Fact]
    public void The_folder_has_a_page_for_each_code_and_no_other_and_its_index_lists_them_all()
    {
        var codes = All().Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var pages = Directory.EnumerateFiles(Folder, "*.md").Select(Path.GetFileNameWithoutExtension).Where(n => n != "README").ToHashSet(StringComparer.Ordinal);
        var index = File.ReadAllText(Path.Combine(Folder, "README.md"));

        Assert.Equal(codes.Order(StringComparer.Ordinal), pages.Order(StringComparer.Ordinal));
        Assert.All(codes, code => Assert.Contains($"[`{code}`]({code}.md)", index, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?:public|internal)\s+(?:async\s+Task|void)\s+(?<name>\w+)\s*\(")]
    private static partial Regex Declared();

    [GeneratedRegex(@"^## What it rests on\n\n.*?`(?<method>\w+)` \(`\w+`\)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex RestsOn();
}

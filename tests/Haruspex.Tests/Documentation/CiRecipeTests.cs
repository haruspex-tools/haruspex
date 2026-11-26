using System.Text.RegularExpressions;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// The CI recipes in the documentation pin each action as haruspex's own workflow does: by full commit SHA, with the version
/// in a comment, at a commit <c>.github/workflows/ci.yml</c> uses. A recipe is what a team pastes into its pipeline, so one
/// pinned by tag (which can be moved) teaches the practice the workflow rejects, and one left at an old commit drifts from
/// the versions the project itself runs. No linter reads YAML inside Markdown, so this does.
/// </summary>
public partial class CiRecipeTests
{
    private static string Workflow => File.ReadAllText(Path.Combine(RepositoryPaths.Root, ".github", "workflows", "ci.yml"));

    /// <summary>
    /// Every <c>uses:</c> line in a fenced block of the documentation, with its page and line. Every block, not only those
    /// labelled YAML, so that a recipe whose fence loses its label is still read.
    /// </summary>
    private static readonly List<(string Page, int Line, string Text)> Uses =
    [
        .. MarkdownText.Documentation.SelectMany(page =>
            MarkdownText.FencedBlocks(File.ReadAllText(Path.Combine(RepositoryPaths.Root, page)))
                .SelectMany(b => b.Text.Split('\n').Select((text, i) => (page, b.Line + 1 + i, text)))
                .Where(u => UsesLine().IsMatch(u.text))),
    ];

    [Fact]
    public void The_pages_with_ci_recipes_are_read() =>
        Assert.All(
            ["docs/command-line.md", "src/Haruspex.Cli/README.md"],
            page => Assert.Contains(Uses, u => u.Page == page));

    [Fact]
    public void Every_action_a_recipe_uses_is_pinned_at_a_commit_the_workflow_uses()
    {
        var workflow = Workflow;
        Assert.Empty(Uses.Select(u => Problem(u.Text, workflow) is { } problem ? $"{u.Page}:{u.Line}: {problem}" : null).OfType<string>());
    }

    [Theory]
    [InlineData("      - uses: actions/checkout@v4", "not pinned by a full commit SHA with its version in a comment")]
    [InlineData("- uses: actions/setup-node@v7.1.0 # v7.1.0", "not pinned by a full commit SHA with its version in a comment")]
    [InlineData("- uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", "not pinned by a full commit SHA with its version in a comment")]
    [InlineData("- uses: actions/checkout@0000000000000000000000000000000000000000 # v7.0.1", "not the commit and version ci.yml uses")]
    [InlineData("- uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v4", "not the commit and version ci.yml uses")]
    public void A_recipe_pinned_otherwise_is_refused(string line, string problem) =>
        Assert.Equal(problem, Problem(line, Workflow));

    [Fact]
    public void A_recipe_pinned_as_the_workflow_pins_is_accepted() =>
        Assert.Null(Problem(
            "- uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1: haruspex itself",
            "      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1\n"));

    /// <summary>
    /// What is wrong with one <c>uses:</c> line of a recipe, or null when it names an action at a full commit SHA, with the
    /// version in a comment, and <paramref name="workflow"/> uses the same action at the same commit and version.
    /// </summary>
    private static string? Problem(string line, string workflow)
    {
        if (PinnedUses().Match(line) is not { Success: true } pinned)
            return "not pinned by a full commit SHA with its version in a comment";
        var action = pinned.Groups["action"].Value;
        var sha = pinned.Groups["sha"].Value;
        var version = pinned.Groups["version"].Value;
        return PinnedUses().Matches(workflow).Any(w =>
            w.Groups["action"].Value == action && w.Groups["sha"].Value == sha && w.Groups["version"].Value == version)
            ? null
            : "not the commit and version ci.yml uses";
    }

    [GeneratedRegex(@"^\s*(-\s*)?uses:")]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"^\s*(-\s*)?uses:\s*(?<action>[\w.-]+/[\w.-]+)@(?<sha>[0-9a-f]{40}) # (?<version>v\d+(\.\d+)*)(?=:|\s|$)", RegexOptions.Multiline)]
    private static partial Regex PinnedUses();
}

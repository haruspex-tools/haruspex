namespace Haruspex.Tests.Documentation;

/// <summary>
/// Every project under <c>src/</c> has a <c>README.md</c> beside it, headed with the project's name, optionally followed
/// by a colon and a subtitle. Each project is a
/// layer that may ship as a package of its own (docs/architecture.md), so its README is what a reader of NuGet or of
/// the folder on GitHub sees first. Each is its layer's chapter of the guide in brief (docs/internals/README.md): what the
/// project is for, a Mermaid diagram of its main types and how data flows through them, a short example, what it
/// guarantees, what it deliberately doesn't do, and a link to the guide. Two go on to more (the Inference README is also the
/// construct reference, the IR README the format's specification), as docs/architecture.md, "Each layer's README", says;
/// this test reads only the sections every README shares.
/// </summary>
/// <remarks>
/// Written as a test on 11 Nov 2026, when three of the nine projects turned out to have none. A convention that
/// nothing checks holds only where it was last swept, and new projects are added by copying a neighbour.
/// </remarks>
public class ProjectReadmeTests
{
    [Fact]
    public void Every_project_under_src_has_a_readme_headed_with_its_name()
    {
        var problems = new List<string>();
        foreach (var project in Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var readme = Path.Combine(Path.GetDirectoryName(project)!, "README.md");
            if (!File.Exists(readme))
            {
                problems.Add($"{name} has no README.md. Write one on the model of src/Haruspex.SqlServer.Sources/README.md.");
                continue;
            }

            // A subtitle after a colon is allowed: "# Haruspex.Ir: the haruspex IR, version 0.14".
            var heading = File.ReadLines(readme).FirstOrDefault()?.Trim();
            if (heading != "# " + name && heading?.StartsWith("# " + name + ": ", StringComparison.Ordinal) != true)
                problems.Add($"{name}/README.md should begin with the heading \"# {name}\" (or \"# {name}: a subtitle\"), not \"{heading}\".");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The sections of a layer's chapter in brief, in the order a reader meets them.</summary>
    private static readonly string[] Sections =
    [
        "## How data flows through it",
        "## Example",
        "## What it guarantees",
        "## What it deliberately doesn't do",
    ];

    [Fact]
    public void Every_project_readme_is_its_layer_s_chapter_in_brief()
    {
        var problems = new List<string>();
        foreach (var readme in Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "README.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = MarkdownText.Relative(readme);
            var lines = File.ReadAllLines(readme);
            var at = Sections.Select(h => Array.IndexOf(lines, h)).ToList();
            if (at.Contains(-1))
                problems.Add($"{name} lacks {string.Join(", ", Sections.Where((_, i) => at[i] < 0))}");
            else if (!at.SequenceEqual(at.Order()))
                problems.Add($"{name} has its sections out of order: {string.Join(", ", Sections)}");
            if (!MarkdownText.FencedBlocks(string.Join('\n', lines)).Any(b => b.Words is ["mermaid", ..]))
                problems.Add($"{name} has no Mermaid diagram of its main types");
            if (!lines.Any(l => l.Contains("docs/internals/", StringComparison.Ordinal)))
                problems.Add($"{name} doesn't link the guide (docs/internals/)");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}

namespace Haruspex.Tests;

/// <summary>
/// Every project under <c>src/</c> has a <c>README.md</c> beside it, headed with the project's name, optionally followed
/// by a colon and a subtitle. Each project is a
/// layer that may ship as a package of its own (docs/architecture.md), so its README is what a reader of NuGet or of
/// the folder on GitHub sees first. <c>Haruspex.SqlServer.Sources/README.md</c> is the model to follow: what the
/// project is for, a short example, what it guarantees, and "What it deliberately doesn't do".
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
        foreach (var project in Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
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
}

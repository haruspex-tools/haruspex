using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// A packable project's public API must be documented, and the build enforces it (<c>CONTRIBUTING.md</c>): a public member
/// without a comment is an error (<c>CS1591</c>) in the sections of <c>.editorconfig</c> that say so. Those sections name the
/// projects by folder, so a packable project added later, or left out, would let an undocumented member in unnoticed; this
/// reads both and fails on any packable project where the last section that sets it, which is the one the compiler obeys,
/// doesn't make it an error.
/// </summary>
public partial class PackableDocumentationTests
{
    [Fact]
    public void Every_packable_project_makes_a_public_member_without_a_comment_an_error()
    {
        var packable = Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(file => XDocument.Load(file).Descendants("IsPackable").Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file)))
            .Order(StringComparer.Ordinal)
            .ToList();
        var (severities, unread) = Severities(File.ReadAllLines(Path.Combine(RepositoryPaths.Root, ".editorconfig")), packable!);

        // So that the test can't pass by finding nothing: the five packable today, as CONTRIBUTING.md names them.
        Assert.Equal(["Haruspex.Collections", "Haruspex.Diagnostics", "Haruspex.SqlServer.Model", "Haruspex.SqlServer.Sources", "Haruspex.SqlServer.Syntax"], packable);
        Assert.Empty(unread);
        var lax = packable.Where(project => severities.GetValueOrDefault(project!) != "error").ToList();
        Assert.True(lax.Count == 0, "A public member without a comment isn't an error in: " + string.Join(", ", lax));
    }

    [Fact]
    public void The_last_section_that_matches_a_project_decides_as_the_compiler_reads_them()
    {
        string[] lines =
        [
            "[*.cs]",
            "dotnet_diagnostic.CS1591.severity = none",
            "[src/{Haruspex.A,Haruspex.B,Haruspex.E}/**.cs]",
            "dotnet_diagnostic.CS1591.severity = error",
            "[src/Haruspex.C/**.cs]",
            "# A comment between the header and the rule.",
            "dotnet_diagnostic.CS1591.severity = error",
            "[src/Haruspex.B/**.cs]",
            "dotnet_diagnostic.CS1591.severity = none",
            "[tests/**.cs]",
            "dotnet_diagnostic.CS1591.severity = error",
            "[src/Haruspex.E/**/*.cs]",
            "dotnet_diagnostic.CS1591.severity = none",
        ];

        var (severities, unread) = Severities(lines, ["Haruspex.A", "Haruspex.B", "Haruspex.C", "Haruspex.D", "Haruspex.E"]);

        Assert.Equal(
            ["Haruspex.A: error", "Haruspex.B: none", "Haruspex.C: error", "Haruspex.D: none", "Haruspex.E: error"],
            severities.Select(e => $"{e.Key}: {e.Value}").Order(StringComparer.Ordinal));
        Assert.Equal(["[src/Haruspex.E/**/*.cs]"], unread);
    }

    /// <summary>
    /// The severity of <c>CS1591</c> in each of <paramref name="projects"/> by <paramref name="editorConfig"/>, where the last
    /// section that matches a file decides, as the compiler reads them: a section under <c>src/</c> of the form
    /// <c>[src/{A,B}/**.cs]</c> or <c>[src/A/**.cs]</c> sets it for the projects it names, and one that starts with
    /// <c>[*</c> for every project. Any other section under <c>src/</c> that sets it is returned as unread, since this can't
    /// say which files it matches; sections elsewhere, such as <c>tests/</c>, don't reach a project under <c>src/</c>.
    /// </summary>
    private static (Dictionary<string, string> Severities, List<string> Unread) Severities(IEnumerable<string> editorConfig, IReadOnlyList<string> projects)
    {
        var severities = new Dictionary<string, string>(StringComparer.Ordinal);
        var unread = new List<string>();
        var header = "";
        IReadOnlyList<string>? section = [];
        foreach (var line in editorConfig.Select(l => l.Trim()))
        {
            if (line.StartsWith('['))
            {
                header = line;
                var match = Header().Match(line);
                section = match.Success ? (match.Groups["several"].Success ? match.Groups["several"].Value.Split(',') : [match.Groups["one"].Value])
                    : line.StartsWith("[*", StringComparison.Ordinal) ? projects
                    : line.StartsWith("[src/", StringComparison.Ordinal) ? null
                    : [];
            }
            else if (Severity().Match(line) is { Success: true } rule)
            {
                if (section is null)
                {
                    unread.Add(header);
                }
                else
                {
                    foreach (var project in section)
                        severities[project] = rule.Groups["severity"].Value;
                }
            }
        }

        return (severities, unread);
    }

    [GeneratedRegex(@"^dotnet_diagnostic\.CS1591\.severity\s*=\s*(?<severity>\w+)$")]
    private static partial Regex Severity();

    [GeneratedRegex(@"^\[src/(\{(?<several>[^{}/]+)\}|(?<one>[^{}/*]+))/\*\*\.cs\]$")]
    private static partial Regex Header();
}

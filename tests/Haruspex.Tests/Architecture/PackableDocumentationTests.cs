using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// A packable project's public API must be documented, and the build enforces it (<c>CONTRIBUTING.md</c>): a public member
/// without a comment is an error (<c>CS1591</c>) in the sections of <c>.editorconfig</c> that say so. Those sections name the
/// projects by folder, so a packable project added later, or left out, would let an undocumented member in unnoticed; this
/// reads both and fails on any packable project no such section covers.
/// </summary>
public partial class PackableDocumentationTests
{
    [Fact]
    public void Every_packable_project_makes_a_public_member_without_a_comment_an_error()
    {
        var documented = DocumentedProjects(File.ReadAllLines(Path.Combine(RepositoryPaths.Root, ".editorconfig")));
        var packable = Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(file => XDocument.Load(file).Descendants("IsPackable").Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file)))
            .Order(StringComparer.Ordinal)
            .ToList();

        // So that the test can't pass by finding nothing: the five packable today, as CONTRIBUTING.md names them.
        Assert.Equal(["Haruspex.Collections", "Haruspex.Diagnostics", "Haruspex.SqlServer.Model", "Haruspex.SqlServer.Sources", "Haruspex.SqlServer.Syntax"], packable);
        Assert.Empty(packable.Except(documented));
    }

    [Fact]
    public void The_sections_are_read_whether_they_name_one_project_or_several()
    {
        string[] lines =
        [
            "[src/{Haruspex.A,Haruspex.B}/**.cs]",
            "dotnet_diagnostic.CS1591.severity = error",
            "[src/Haruspex.C/**.cs]",
            "# A comment between the header and the rule.",
            "dotnet_diagnostic.CS1591.severity = error",
            "[src/Haruspex.D/**.cs]",
            "dotnet_diagnostic.CS1591.severity = none",
        ];

        Assert.Equal(["Haruspex.A", "Haruspex.B", "Haruspex.C"], DocumentedProjects(lines).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The project folders under <c>src/</c> named by a section of <paramref name="editorConfig"/> that makes <c>CS1591</c> an
    /// error, from headers of the form <c>[src/{A,B}/**.cs]</c> or <c>[src/A/**.cs]</c>.
    /// </summary>
    private static HashSet<string> DocumentedProjects(IEnumerable<string> editorConfig)
    {
        var projects = new HashSet<string>(StringComparer.Ordinal);
        string[] section = [];
        foreach (var line in editorConfig.Select(l => l.Trim()))
        {
            if (line.StartsWith('['))
            {
                var header = Header().Match(line);
                section = !header.Success ? [] : header.Groups["several"].Success ? header.Groups["several"].Value.Split(',') : [header.Groups["one"].Value];
            }
            else if (Regex.IsMatch(line, @"^dotnet_diagnostic\.CS1591\.severity\s*=\s*error$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                projects.UnionWith(section);
            }
        }

        return projects;
    }

    [GeneratedRegex(@"^\[src/(\{(?<several>[^{}/]+)\}|(?<one>[^{}/*]+))/\*\*\.cs\]$")]
    private static partial Regex Header();
}

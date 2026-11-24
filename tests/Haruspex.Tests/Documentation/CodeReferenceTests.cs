using System.Globalization;
using System.Text.RegularExpressions;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// Every code reference in the documentation resolves: each path, C# file, type, member and test it names in code exists.
/// The guide explains haruspex by pointing into its code, and a pointer to something renamed or removed teaches the reader
/// something false. The convention is in <c>docs/internals/README.md</c>, "How the guide is kept true", and
/// <see cref="CodeReferences"/> applies it.
/// </summary>
public partial class CodeReferenceTests
{
    public static TheoryData<string> Documents { get; } = [.. MarkdownText.Documentation];

    [Fact]
    public void The_documentation_holds_references_to_check()
    {
        var references = MarkdownText.Documentation
            .SelectMany(f => MarkdownText.InlineCode(File.ReadAllText(Path.Combine(RepositoryPaths.Root, f))))
            .Count(c => CodeReferences.IsReference(c.Code));

        // Hundreds today; a fall to a handful would mean the scan stopped seeing them, not that they went away.
        Assert.True(references > 500, $"Only {references.ToString(CultureInfo.InvariantCulture)} references found.");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void Every_code_reference_in_a_document_resolves(string file)
    {
        var markdown = File.ReadAllText(Path.Combine(RepositoryPaths.Root, file));
        var problems = new List<string>();
        foreach (var (line, code) in MarkdownText.InlineCode(markdown))
        {
            if (CodeReferences.Current.Problem(code) is { } problem)
                problems.Add($"{file}:{line.ToString(CultureInfo.InvariantCulture)}: `{code}` {problem}");
        }

        foreach (var (line, name) in CodeReferences.DiagramNames(markdown))
        {
            if (CodeReferences.Current.Problem(name) is { } problem)
                problems.Add($"{file}:{line.ToString(CultureInfo.InvariantCulture)}: the diagram's {name} {problem}");
        }

        foreach (var (line, path) in RepositoryLinks(markdown))
        {
            if (CodeReferences.PathProblem(path) is { } problem)
                problems.Add($"{file}:{line.ToString(CultureInfo.InvariantCulture)}: the link to {path} {problem}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_path_into_the_docs_written_in_the_source_resolves()
    {
        var problems = new List<string>();
        foreach (var file in CodeReferences.SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in DocsPath().Matches(lines[i]))
                {
                    if (CodeReferences.PathProblem(m.Value) is { } problem)
                        problems.Add($"{MarkdownText.Relative(file)}:{(i + 1).ToString(CultureInfo.InvariantCulture)}: {m.Value} {problem}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("ResultTrack")]
    [InlineData("ResultTrack.Then")]
    [InlineData("FixedPoint.WidenAfter")]
    [InlineData("FixedPoint.MostPasses")]
    [InlineData("Calls_nest_at_most_32_deep")]
    [InlineData("ExecObservationTests")]
    [InlineData("Runs.All")]
    [InlineData("Haruspex.SqlServer.Inference")]
    [InlineData("FlowState.cs")]
    [InlineData("src/Haruspex.SqlServer.Inference/FunctionSummaries.cs")]
    [InlineData("tests/fixtures/trees")]
    [InlineData("GetCategoryRow")]
    [InlineData("RecursionLevel")]
    public void A_reference_to_something_that_exists_resolves(string code)
    {
        Assert.True(CodeReferences.IsReference(code));
        Assert.Null(CodeReferences.Current.Problem(code));
    }

    [Theory]
    [InlineData("ResultQueue")]
    [InlineData("ResultTrack.Before")]
    [InlineData("FixedPoint.MostPasses.Nothing")]
    [InlineData("Calls_nest_at_most_33_deep")]
    [InlineData("Runs.Nowhere")]
    [InlineData("Haruspex.SqlServer.Inferences")]
    [InlineData("FlowStates.cs")]
    [InlineData("src/Haruspex.SqlServer.Inference/FunctionSummary.cs")]
    [InlineData("docs/internals/no-such-chapter")]
    [InlineData("ComponentDescription")]
    public void A_reference_to_something_that_does_not_exist_is_reported(string code)
    {
        Assert.True(CodeReferences.IsReference(code));
        Assert.NotNull(CodeReferences.Current.Problem(code));
    }

    [Theory]
    [InlineData("FOR JSON")]
    [InlineData("WITHOUT_ARRAY_WRAPPER")]
    [InlineData("nvarchar(max)")]
    [InlineData("count-overflow")]
    [InlineData("README.md")]
    [InlineData("haruspex.json")]
    [InlineData("path/to/database")]
    [InlineData("src/db")]
    [InlineData("tests/fixtures/*.sql")]
    [InlineData("SqlTypes.ToShape(SqlType)")]
    public void Code_that_is_not_a_reference_is_not_checked(string code) => Assert.False(CodeReferences.IsReference(code));

    [Fact]
    public void A_member_written_alone_is_not_taken_for_a_type()
    {
        // Redefinition is a type and also a property of Procedure; Body is only a property.
        Assert.Null(CodeReferences.Current.Problem("Redefinition"));
        Assert.Null(CodeReferences.Current.Problem("Procedure.Body"));
        Assert.NotNull(CodeReferences.Current.Problem("Body"));
    }

    [Fact]
    public void A_diagram_s_compound_names_are_references_and_its_plain_words_are_not() =>
        Assert.Equal(
            [(3, "IrBuilder"), (4, "ResultTrack.Then"), (4, "ILocalRelations")],
            CodeReferences.DiagramNames("```mermaid\nflowchart LR\n    a[\"IrBuilder\"] --> b[\"Sources: the Model\"]\n    b --> c[\"ResultTrack.Then, ILocalRelations, SQL, CTEs\"]\n```\n"));

    [Fact]
    public void A_link_into_this_repository_on_github_is_checked_on_disk() =>
        Assert.Equal(
            [(1, "src/Haruspex.Ir/README.md"), (2, "docs")],
            RepositoryLinks("[IR](https://github.com/haruspex-tools/haruspex/blob/main/src/Haruspex.Ir/README.md#paths)\n[docs](https://github.com/haruspex-tools/haruspex/tree/main/docs)\n"));

    /// <summary>
    /// Links into this repository on GitHub, as the packable projects' READMEs write them (a package's readme is read on
    /// NuGet, where a relative link leads nowhere), with the path each names.
    /// </summary>
    private static IEnumerable<(int Line, string Path)> RepositoryLinks(string markdown)
    {
        var lines = markdown.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match m in GitHubLink().Matches(lines[i]))
                yield return (i + 1, Uri.UnescapeDataString(m.Groups["path"].Value));
        }
    }

    [GeneratedRegex(@"https://github\.com/haruspex-tools/haruspex/(?:blob|tree)/main/(?<path>[^)\s#?]+)")]
    private static partial Regex GitHubLink();

    [GeneratedRegex(@"\bdocs/[A-Za-z0-9_./-]+\.md\b")]
    private static partial Regex DocsPath();
}

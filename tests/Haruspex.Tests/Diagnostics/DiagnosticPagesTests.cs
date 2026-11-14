using System.Text.RegularExpressions;
using Haruspex.Diagnostics;

namespace Haruspex.Tests.Diagnostics;

/// <summary>
/// The pages of <c>docs/diagnostics</c> and the codes are one set: every code, in use or retired, has a page, and every page
/// is a code's; each page is headed by its code and title and says its severity, and shows as its example the line its code
/// is reported with (the message snapshot's, so a page can't show a message haruspex no longer writes). The index lists
/// every page under the step its code was born in.
/// </summary>
public partial class DiagnosticPagesTests
{
    private static readonly string Folder = Path.Combine(RepositoryPaths.Root, "docs", "diagnostics");

    private static readonly IReadOnlyDictionary<string, DiagnosticCode> Codes =
        DiagnosticCodesTests.Declared(DiagnosticCodesTests.Layers()).ToDictionary(c => c.Code.ToString(), c => c.Code, StringComparer.Ordinal);

    private static IEnumerable<string> Pages() =>
        Directory.EnumerateFiles(Folder, "HAR*.md").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal);

    private static string Page(string code) => File.ReadAllText(Path.Combine(Folder, code + ".md")).ReplaceLineEndings("\n");

    [Fact]
    public void Every_code_has_a_page_and_every_page_is_a_code_s()
    {
        Assert.Empty(Codes.Keys.Except(Pages(), StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(c => $"no page for {c}"));
        Assert.Empty(Pages().Except(Codes.Keys, StringComparer.Ordinal).Select(p => $"no code for the page {p}.md"));
    }

    [Fact]
    public void Each_page_is_headed_by_its_code_and_title_and_says_its_severity_and_whether_it_is_retired()
    {
        var wrong = new List<string>();
        foreach (var (name, code) in Codes)
        {
            var page = Page(name);
            var heading = $"# {name}: {char.ToLowerInvariant(code.Title[0])}{code.Title[1..]}\n";
            var severity = $"**{code.Severity}.**";
            if (!page.StartsWith(heading, StringComparison.Ordinal))
                wrong.Add($"{name}: the page should start \"{heading.TrimEnd()}\"");
            if (!page.Contains("\n\n" + severity, StringComparison.Ordinal))
                wrong.Add($"{name}: the page should say {severity}");
            if (code.Retired != page.Contains("**Retired.**", StringComparison.Ordinal))
                wrong.Add($"{name}: the page should say **Retired.** exactly when the code is retired");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void Each_page_shows_as_its_example_the_line_its_code_is_reported_with()
    {
        var wrong = Codes.Where(c => !c.Value.Retired)
            .Where(c => Example().Match(Page(c.Key)) is not { Success: true } match || match.Groups["line"].Value != DiagnosticMessagesTests.Lines[c.Key])
            .Select(c => $"{c.Key}: its example should be\n{DiagnosticMessagesTests.Lines[c.Key]}")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void The_index_lists_every_page_under_the_step_its_code_was_born_in()
    {
        var index = File.ReadAllText(Path.Combine(Folder, "README.md")).ReplaceLineEndings("\n");
        var sections = Regex.Split(index, @"\n## ").Skip(1).ToList();
        var listed = sections.SelectMany((section, i) => Link().Matches(section).Select(m => (Code: m.Groups["code"].Value, Section: i))).ToList();

        Assert.Equal(Codes.Keys.Order(StringComparer.Ordinal), listed.Select(l => l.Code).Order(StringComparer.Ordinal));

        // One section for each step: every code in a section has the section's first digit, and no digit has two sections.
        var digitsBySection = listed.GroupBy(l => l.Section).Select(g => g.Select(l => l.Code[3]).Distinct().ToList()).ToList();
        Assert.All(digitsBySection, digits => Assert.Single(digits));
        Assert.Equal(digitsBySection.Count, digitsBySection.Select(d => d[0]).Distinct().Count());
    }

    [GeneratedRegex(@"## Example\n\n```text\n(?<line>[^\n]*)\n```")]
    private static partial Regex Example();

    [GeneratedRegex(@"\[(?<code>HAR\d{4})\]\(\k<code>\.md\)")]
    private static partial Regex Link();
}

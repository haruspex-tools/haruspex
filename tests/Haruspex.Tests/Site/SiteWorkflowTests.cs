using System.Text.RegularExpressions;

namespace Haruspex.Tests.Site;

/// <summary>
/// The Pages workflow, <c>.github/workflows/site.yml</c>, grants nothing by default and lets only its deploy job write: Pages and
/// the OIDC token, there alone. Neither actionlint nor zizmor's default persona notices a write permission added to the build job,
/// which checks out the repository and runs its code, so this reads the file for it.
/// </summary>
public partial class SiteWorkflowTests
{
    private static string Workflow => Path.Combine(RepositoryPaths.Root, ".github", "workflows", "site.yml");

    [Fact]
    public void Only_the_deploy_job_may_write_and_only_to_pages_and_the_oidc_token()
    {
        var text = File.ReadAllText(Workflow);

        Assert.Equal(
            ["deploy: id-token: write", "deploy: pages: write"],
            Permissions(text).Where(p => p.Level != "read").Select(p => $"{p.Job}: {p.Name}: {p.Level}").Order(StringComparer.Ordinal));
        Assert.Contains("\npermissions: {}\n", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_permission_in_the_build_job_is_found() =>
        Assert.Contains(
            ("build", "pages", "write"),
            Permissions("jobs:\n  build:\n    permissions:\n      contents: read\n      pages: write # planted\n  deploy:\n    permissions:\n      id-token: write\n"));

    [Theory]
    [InlineData("    permissions: write-all")]
    [InlineData("    permissions: { contents: read, pages: write }")]
    [InlineData("    permissions: read-all")]
    public void A_permissions_line_in_any_form_but_a_block_or_none_is_refused_rather_than_read(string line) =>
        Assert.Contains(
            ("build", "permissions", "not read: write each permission on a line of its own, under permissions:"),
            Permissions($"jobs:\n  build:\n{line}\n    steps: []\n"));

    [Fact]
    public void A_write_permission_at_the_top_is_found() =>
        Assert.Contains(("", "pages", "write"), Permissions("permissions:\n  pages: write\njobs:\n  build:\n    steps: []\n"));

    /// <summary>
    /// Every permission the workflow names, with its job (empty at the top level) and its level, from the file's text. A
    /// <c>permissions:</c> line in any other form than a block below it or <c>{}</c> (<c>write-all</c>, a flow mapping) is not read
    /// but given as a permission of its own whose level says so, so that the test fails rather than passing what it can't read.
    /// </summary>
    private static List<(string Job, string Name, string Level)> Permissions(string workflow)
    {
        var found = new List<(string, string, string)>();
        var job = "";
        var block = "";
        var indent = -1;
        foreach (var line in workflow.ReplaceLineEndings("\n").Split('\n'))
        {
            if (JobLine().Match(line) is { Success: true } j && block == "jobs")
            {
                job = j.Groups["job"].Value;
                indent = -1;
            }
            else if (TopLine().Match(line) is { Success: true } top)
            {
                block = top.Groups["key"].Value;
                job = "";
                indent = -1;
            }

            if (PermissionsLine().Match(line) is { Success: true } p)
            {
                indent = p.Groups["indent"].Length;
                continue;
            }

            if (AnyPermissionsLine().IsMatch(line))
            {
                found.Add((job, "permissions", "not read: write each permission on a line of its own, under permissions:"));
                indent = -1;
                continue;
            }

            if (indent >= 0 && EntryLine().Match(line) is { Success: true } e && e.Groups["indent"].Length > indent)
                found.Add((job, e.Groups["name"].Value, e.Groups["level"].Value));
            else if (indent >= 0 && line.Trim().Length > 0)
                indent = -1;
        }

        return found;
    }

    [GeneratedRegex(@"^(?<key>[a-z-]+):")]
    private static partial Regex TopLine();

    [GeneratedRegex(@"^  (?<job>[a-z-]+):\s*$")]
    private static partial Regex JobLine();

    [GeneratedRegex(@"^(?<indent>\s*)permissions:\s*(\{\})?\s*(#.*)?$")]
    private static partial Regex PermissionsLine();

    [GeneratedRegex(@"^\s*permissions:")]
    private static partial Regex AnyPermissionsLine();

    [GeneratedRegex(@"^(?<indent>\s+)(?<name>[a-z-]+):\s*(?<level>read|write|none)\s*(#.*)?$")]
    private static partial Regex EntryLine();
}

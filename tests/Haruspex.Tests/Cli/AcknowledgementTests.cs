using System.Text.Json;
using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The config file's <c>acknowledge</c>: a warning named by its code and its subject is counted rather than said, listed with
/// <c>--verbose</c>, and held apart in the machine formats; every other warning of the code is still said; an entry by code
/// alone, or for a code that can't be acknowledged, is refused; and an entry that matches nothing is said.
/// </summary>
public class AcknowledgementTests
{
    /// <summary>A procedure that writes to two tables the project doesn't define, each a HAR2101 warning whose subject is the table.</summary>
    private const string Writes = "CREATE TABLE dbo.t (id int NOT NULL);\nGO\nCREATE PROCEDURE dbo.p AS\nINSERT INTO audit.trail (id) VALUES (1);\nINSERT INTO audit.archive (id) VALUES (1);\nSELECT t.id FROM dbo.t AS t;\n";

    private static TempDirectory Project(string acknowledge)
    {
        var folder = new TempDirectory();
        folder.File("db/p.sql", Writes);
        folder.File("haruspex.json", $$"""{ "source": "db", "out": "out", "schemas": { "dbo": { "include": ["*"] } }, "acknowledge": {{acknowledge}} }""");
        return folder;
    }

    private const string Trail = """[{ "code": "HAR2101", "subject": "audit.trail", "why": "the trail has no triggers" }]""";

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, CliOutput.Shown(output.ToString(), folder.Path), CliOutput.Shown(error.ToString(), folder.Path));
    }

    [Fact]
    public void An_acknowledged_warning_is_counted_and_the_others_of_its_code_are_still_said()
    {
        using var folder = Project(Trail);

        var (exitCode, output, error) = Run(folder, "generate");

        Assert.Equal(0, exitCode);
        Assert.Contains("\n1 warning acknowledged (--verbose lists them).\n", output, StringComparison.Ordinal);
        Assert.Contains("warning HAR2101: audit.archive is written here", error, StringComparison.Ordinal);
        Assert.DoesNotContain("audit.trail", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbose_lists_an_acknowledged_warning_with_why()
    {
        using var folder = Project(Trail);

        var (_, _, error) = Run(folder, "generate", "--verbose");

        Assert.Contains("warning HAR2101: audit.trail is written here", error, StringComparison.Ordinal);
        Assert.Contains("(acknowledged: the trail has no triggers)\n", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_check_that_passes_counts_them_on_its_one_line()
    {
        using var folder = Project("""[{ "code": "HAR2101", "subject": "audit.trail", "why": "no triggers" }, { "code": "HAR2101", "subject": "audit.archive", "why": "no triggers" }]""");
        Run(folder, "generate");

        var (exitCode, output, error) = Run(folder, "check");

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Equal("haruspex: 1 procedure, no unknowns. out is up to date. 2 warnings acknowledged (--verbose lists them).\n", output);
    }

    [Fact]
    public void The_machine_formats_hold_them_apart_json_with_why_and_sarif_as_suppressed()
    {
        using var folder = Project(Trail);

        var json = JsonDocument.Parse(Run(folder, "generate", "--format", "json").Output).RootElement.GetProperty("modules")[0];
        var sarif = JsonDocument.Parse(Run(folder, "generate", "--format", "sarif").Output).RootElement.GetProperty("runs")[0].GetProperty("results");

        var acknowledged = Assert.Single(json.GetProperty("acknowledged").EnumerateArray());
        Assert.Equal(("audit.trail", "the trail has no triggers"), (acknowledged.GetProperty("subject").GetString(), acknowledged.GetProperty("why").GetString()));
        Assert.DoesNotContain(json.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("message").GetString()!.StartsWith("audit.trail", StringComparison.Ordinal));
        var suppressed = Assert.Single(sarif.EnumerateArray(), r => r.TryGetProperty("suppressions", out _));
        Assert.Equal("external", suppressed.GetProperty("suppressions")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void A_warning_is_never_acknowledged_by_its_code_alone()
    {
        using var folder = Project("""[{ "code": "HAR2101", "why": "triggers never return results here" }]""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.Equal(2, exitCode);
        Assert.Contains("error HAR3005: acknowledge[0]: an acknowledgement needs \"subject\"", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_that_can_t_be_acknowledged_is_refused()
    {
        using var folder = Project("""[{ "code": "HAR1001", "subject": "db/p.sql", "why": "it parses on the server" }]""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.Equal(2, exitCode);
        Assert.StartsWith("haruspex.json:1:", error, StringComparison.Ordinal);
        Assert.Contains("error HAR6024: acknowledge[0]: HAR1001 can't be acknowledged", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_that_matches_nothing_is_said_unless_the_command_line_narrows_the_run()
    {
        using var folder = Project("""[{ "code": "HAR2101", "subject": "audit.trial", "why": "a typo" }]""");

        var (exitCode, _, error) = Run(folder, "generate");
        var (_, _, narrowed) = Run(folder, "generate", "--include", "dbo.*");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning HAR6025: acknowledge[0]: acknowledges HAR2101 for \"audit.trial\", but this run gave no such warning.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("HAR6025", narrowed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_master_dacpac_is_acknowledged_by_its_file_name()
    {
        using var folder = new TempDirectory();
        File.Copy(Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "harbour", "harbour.dacpac"), Path.Combine(folder.Path, "harbour.dacpac"));
        folder.File("haruspex.json", """{ "source": "harbour.dacpac", "references": ["."], "out": "out", "schemas": { "dock": { "include": ["*"] } }, "acknowledge": [{ "code": "HAR1003", "subject": "master.dacpac", "why": "no procedure reads a system view" }] }""");

        var (exitCode, output, error) = Run(folder, "generate");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("master.dacpac", error, StringComparison.Ordinal);
        Assert.Contains("warning acknowledged", output, StringComparison.Ordinal);
    }
}

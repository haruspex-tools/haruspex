using Haruspex.Cli;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The reports for machines: <c>--format json</c>, specified by schemas/haruspex.output.schema.json; <c>--format sarif</c>, a
/// SARIF 2.1.0 log; and <c>--format github</c>, the text with an annotation for each error and warning, the default in GitHub
/// Actions. Each is snapshotted on the invented kennel, and the JSON and SARIF documents are validated against their schemas
/// with Ajv.
/// </summary>
public class ReportFormatTests
{
    /// <summary>A plain terminal with a fixed version, so the SARIF log's bytes don't change with each build.</summary>
    private static Terminal Plain(params string[] variables) => new()
    {
        Version = "0.0.0-test",
        Environment = variables.Select(v => v.Split('=', 2)).ToDictionary(p => p[0], p => p[1]),
    };

    /// <summary>
    /// Each document <see cref="Documents"/> gives, written to a folder for validation: a first run, one after a change, a run
    /// that fails, a check that fails, and a run over two modules, one of which fails.
    /// </summary>
    private static IEnumerable<(string Name, string Text)> Documents(string format)
    {
        using (var folder = OutputTests.Kennel())
        {
            yield return ("first", Stdout(folder, "generate", "--format", format));
            folder.File("db/tables.sql", File.ReadAllText(Path.Combine(folder.Path, "db", "tables.sql")).Replace("name nvarchar(50) NOT NULL", "name nvarchar(50) NULL", StringComparison.Ordinal));
            yield return ("changed", Stdout(folder, "generate", "--format", format));
            folder.File("db/tables.sql", File.ReadAllText(Path.Combine(folder.Path, "db", "tables.sql")).Replace("name nvarchar(50) NULL", "name nvarchar(50) NOT NULL", StringComparison.Ordinal));
            yield return ("check", Stdout(folder, "check", "--format", format));
            folder.File("db/broken.sql", "CREATE PROCEDURE kennel.broken AS SELECT FROM;");
            yield return ("failed", Stdout(folder, "generate", "--format", format));
        }

        using var modules = new TempDirectory();
        modules.File("a/db/p.sql", "CREATE PROCEDURE dbo.p AS SELECT 1 AS one;");
        modules.File("a/haruspex.json", """{ "source": "db", "out": "out", "schemas": { "dbo": { "include": ["*"] } } }""");
        modules.File("b/haruspex.json", """{ "source": "db", "out": "out", "schemas": { "dbo": { "include": ["*"] } } }""");
        yield return ("modules", Stdout(modules, "generate", "--all", "--format", format));
    }

    private static string Stdout(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        HaruspexCli.Run(args, output, error, folder.Path, Plain());

        // A machine format puts everything in its document; only the text, which --format github keeps, writes to standard error.
        if (!args.Contains("github"))
            Assert.Equal("", error.ToString());
        return CliOutput.Shown(output.ToString(), folder.Path);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("sarif")]
    public void Each_document_is_as_committed(string format)
    {
        foreach (var (name, text) in Documents(format))
            Snapshot.Match(text, $"{nameof(ReportFormatTests)}.{format}.{name}", format == "sarif" ? "sarif.json" : "json");
    }

    [Theory]
    [InlineData("json", "schemas/haruspex.output.schema.json")]
    [InlineData("sarif", "tests/sarif/sarif-2.1.0.json")]
    public void Each_document_is_valid_against_its_schema(string format, string schema)
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, $"the {format} reports were not validated");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
        using var folder = new TempDirectory();
        foreach (var (name, text) in Documents(format))
            folder.File($"{name}.{format}.json", text);

        // The folder, not each file: the command line stays short however many documents there are (EmitCheckPackage.TryRun).
        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "validate.mjs"), Path.Combine(RepositoryPaths.Root, schema), folder.Path], RepositoryPaths.EmitCheck, out var output, out var why),
            $"A {format} report doesn't validate against {schema} ({why}):\n{output}");
    }

    [Fact]
    public void The_output_schema_is_a_valid_2020_12_schema_that_compiles_strictly()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the output schema was not checked against the meta-schema");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "meta-schema.mjs"), Path.Combine(RepositoryPaths.Root, "schemas", "haruspex.output.schema.json")], RepositoryPaths.EmitCheck, out var output, out var why),
            $"schemas/haruspex.output.schema.json is not a valid schema ({why}):\n{output}");
    }

    [Fact]
    public void In_github_actions_errors_and_warnings_are_annotations_after_the_text()
    {
        using var folder = OutputTests.Kennel();
        folder.File("db/broken.sql", "CREATE PROCEDURE kennel.broken AS SELECT FROM;");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HaruspexCli.Run(["generate"], output, error, folder.Path, Plain("GITHUB_ACTIONS=true", $"GITHUB_WORKSPACE={Path.GetDirectoryName(folder.Path)}"));

        var name = Path.GetFileName(folder.Path);
        Assert.Equal(1, exitCode);
        Assert.Equal($"::error file={name}/db/broken.sql,line=1,col=42,title=HAR1001 The SQL doesn't parse::Incorrect syntax near 'FROM'.\n", output.ToString());
        Assert.Equal("db/broken.sql:1:42: error HAR1001: Incorrect syntax near 'FROM'.\nharuspex: 1 error; nothing was written.\n", CliOutput.Shown(error.ToString(), folder.Path));
    }

    [Fact]
    public void Notes_are_annotations_only_when_verbose()
    {
        using var folder = OutputTests.Kennel();

        var quiet = Stdout(folder, "generate", "--format", "github");
        var verbose = Stdout(folder, "generate", "--format", "github", "--verbose");

        Assert.DoesNotContain("::notice", quiet, StringComparison.Ordinal);
        Assert.Contains("::warning file=haruspex.json,title=HAR4204 ", quiet, StringComparison.Ordinal);
        Assert.Contains("::notice file=db/procedures.sql,line=5,col=26,title=HAR2009 A column that can't be found or is ambiguous::kennel.get_dog results[0][].nickname: The table kennel.dog has no column nickname.\n", verbose, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("100% sure\r\n2 lines", @"100%25 sure\r\n2 lines", @"100%25 sure\r\n2 lines")]
    [InlineData("red\u001b[31m\u0007", @"red\x1B[31m\x07", @"red\x1B[31m\x07")]
    [InlineData("a: b, c", "a: b, c", "a%3A b%2C c")]
    public void A_message_and_a_property_are_escaped_as_the_runner_reads_them(string text, string data, string property) =>
        Assert.Equal((data, property), (Haruspex.Cli.Output.GitHubAnnotations.Data(text), Haruspex.Cli.Output.GitHubAnnotations.Property(text)));
}

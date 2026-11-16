using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haruspex.Cli;
using Haruspex.Emit.JsonSchema;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Sources;
using SampleCorpus = Haruspex.Tests.Corpus.Corpus;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// Every worked example in the documentation is run through haruspex, and its output is the one shown. An example that
/// goes stale teaches the reader a rule haruspex no longer follows, so the examples are tests like any other.
/// </summary>
public class WorkedExampleTests
{
    private static readonly List<WorkedExample> All =
    [
        .. MarkdownText.Documentation.SelectMany(f => WorkedExample.Read(f, File.ReadAllText(Path.Combine(RepositoryPaths.Root, f))).Examples),
    ];

    public static TheoryData<string, int> Examples { get; } = [.. All.Select(e => (e.File, e.Line))];

    [Fact]
    public void The_guide_has_worked_examples_to_run() =>
        Assert.True(All.Count(e => e.File.StartsWith("docs/internals/", StringComparison.Ordinal)) >= 3, $"{All.Count.ToString(CultureInfo.InvariantCulture)} examples found.");

    [Fact]
    public void Every_worked_example_is_written_so_it_can_be_run()
    {
        var problems = MarkdownText.Documentation
            .SelectMany(f => WorkedExample.Read(f, File.ReadAllText(Path.Combine(RepositoryPaths.Root, f))).Problems)
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_worked_example_gives_the_output_shown(string file, int line)
    {
        var example = All.Single(e => e.File == file && e.Line == line);
        var problems = new List<string>();
        foreach (var output in example.Outputs)
        {
            var (expected, actual) = output.Kind switch
            {
                WorkedExample.OutputKind.Zod => (Text(output.Text), Text(WorkedExample.ZodShown(ZodEmitter.Emit(Infer(example)).Expected(), output.Text))),
                WorkedExample.OutputKind.Ir => (Json(JsonNode.Parse(output.Text)), Json(IrShown(Infer(example), output.Property))),
                WorkedExample.OutputKind.JsonSchema => (Json(JsonNode.Parse(output.Text)), Json(JsonSchemaShown(Infer(example), output.Property))),
                WorkedExample.OutputKind.Run => (Text(output.Text), Text(Run(example.Sql, output))),
                WorkedExample.OutputKind.Check => (Text(output.Text), Text(Check(output.Before!, example.Sql, output))),
            };
            if (expected != actual)
                problems.Add($"{file}:{output.Line.ToString(CultureInfo.InvariantCulture)}: haruspex gives\n{actual}");
        }

        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
    }

    [Fact]
    public void Every_corpus_an_example_is_read_beside_is_vendored()
    {
        var unknown = All.Where(e => e.Over is { } over && SampleCorpus.All.All(c => c.Name != over))
            .Select(e => $"{e.File}:{e.Line.ToString(CultureInfo.InvariantCulture)}: no vendored corpus is named {e.Over}")
            .ToList();
        Assert.True(unknown.Count == 0, string.Join("\n", unknown));
    }

    [Fact]
    public void An_example_may_name_the_corpus_it_is_read_beside()
    {
        const string markdown = """
            ```sql haruspex-example AdventureWorks
            CREATE PROCEDURE Production.p AS SELECT p.Name FROM Production.Product AS p;
            ```

            ```ts haruspex-zod
            ```

            ```sql haruspex-example
            CREATE TABLE dbo.t (id int NOT NULL);
            ```

            ```ts haruspex-zod
            ```
            """;
        var (examples, problems) = WorkedExample.Read("README.md", markdown);

        Assert.Empty(problems);
        Assert.Equal(["AdventureWorks", null], examples.Select(e => e.Over));
    }

    [Fact]
    public void An_example_read_beside_a_corpus_is_typed_from_the_corpus_s_tables_and_alone()
    {
        var document = InferOver("AdventureWorks", "CREATE PROCEDURE Production.p AS SELECT p.Name FROM Production.Product AS p;");

        var procedure = Assert.Single(document.Objects);
        Assert.Equal("Production.p", procedure.Id);
        Assert.DoesNotContain(document.Unknowns, u => u.Object == procedure.Id);
    }

    [Fact]
    public void An_example_is_paired_with_the_output_blocks_after_it()
    {
        const string markdown = """
            ```sql haruspex-example
            CREATE TABLE dbo.t (id int NOT NULL);
            ```

            Prose between them is allowed.

            ```ts haruspex-zod
            export const XSchema = z.int32()
            ```

            ```json haruspex-ir unknowns
            []
            ```

            ```json haruspex-jsonschema GetT
            {}
            ```

            ```sql
            -- not an example
            ```
            """;
        var (examples, problems) = WorkedExample.Read("guide.md", markdown);

        var example = Assert.Single(examples);
        Assert.Empty(problems);
        Assert.Equal((1, "CREATE TABLE dbo.t (id int NOT NULL);"), (example.Line, example.Sql));
        Assert.Equal(
            [(WorkedExample.OutputKind.Zod, null, 7), (WorkedExample.OutputKind.Ir, "unknowns", 11), (WorkedExample.OutputKind.JsonSchema, "GetT", 15)],
            example.Outputs.Select(o => (o.Kind, o.Property, o.Line)));
    }

    [Fact]
    public void A_run_takes_its_arguments_from_its_mark_and_the_config_file_shown_before_it()
    {
        const string markdown = """
            ```sql haruspex-example
            CREATE TABLE dbo.t (id int NOT NULL);
            ```

            ```text haruspex-run
            first
            ```

            ```jsonc haruspex-config
            { "source": "db" }
            ```

            ```text haruspex-run --include dbo.*
            second
            ```
            """;
        var (examples, problems) = WorkedExample.Read("guide.md", markdown);

        Assert.Empty(problems);
        Assert.Equal(
            [(WorkedExample.OutputKind.Run, null, ""), (WorkedExample.OutputKind.Run, "{ \"source\": \"db\" }", "--include dbo.*")],
            Assert.Single(examples).Outputs.Select(o => (o.Kind, o.Config, string.Join(' ', o.Arguments))));
    }

    [Fact]
    public void A_check_compares_with_the_example_before_and_needs_one()
    {
        const string markdown = """
            ```sql haruspex-example
            CREATE TABLE dbo.t (id int NOT NULL);
            ```

            ```text haruspex-check
            first
            ```

            ```sql haruspex-example
            CREATE TABLE dbo.t (id int NULL);
            ```

            ```text haruspex-check --include dbo.*
            second
            ```
            """;
        var (examples, problems) = WorkedExample.Read("guide.md", markdown);

        Assert.Equal(
            [
                "guide.md:5: a haruspex-check block in the first haruspex-example block's outputs, with no project before it to have generated",
                "guide.md:1: a haruspex-example block with no output block after it",
            ],
            problems);
        var check = Assert.Single(Assert.Single(examples).Outputs);
        Assert.Equal((WorkedExample.OutputKind.Check, "CREATE TABLE dbo.t (id int NOT NULL);", "--include dbo.*"), (check.Kind, check.Before, string.Join(' ', check.Arguments)));
    }

    [Fact]
    public void An_output_with_no_example_and_an_example_with_no_output_are_reported()
    {
        const string markdown = """
            ```ts haruspex-zod
            ```

            ```sql haruspex-example
            ```
            """;
        var (examples, problems) = WorkedExample.Read("guide.md", markdown);

        Assert.Empty(examples);
        Assert.Equal(
            ["guide.md:1: a haruspex-zod block with no haruspex-example block before it", "guide.md:4: a haruspex-example block with no output block after it"],
            problems);
    }

    [Fact]
    public void A_zod_block_may_leave_out_the_module_s_header()
    {
        const string module = "// Generated by haruspex from IR 9.9. Do not edit.\n// A note.\nimport { z } from 'zod'\n\nexport const XSchema = z.int32()\n";
        Assert.Equal("export const XSchema = z.int32()\n", WorkedExample.ZodShown(module, "export const XSchema = z.int32()"));
        Assert.Equal(module, WorkedExample.ZodShown(module, "// Generated by haruspex from IR 9.9. Do not edit."));
    }

    /// <summary>
    /// What <c>haruspex generate</c> prints, standard error then standard output, run in a folder holding the example's project
    /// as <c>db/project.sql</c> and its config file, if it has one, as <c>haruspex.json</c>; paths are shown from that folder.
    /// </summary>
    private static string Run(string sql, WorkedExample.Output run)
    {
        using var folder = new TempDirectory();
        folder.File("db/project.sql", sql);
        if (run.Config is { } config)
            folder.File("haruspex.json", config);
        string[] arguments = run.Arguments.Count > 0 ? [.. run.Arguments] : run.Config is null ? ["db", "--include", "*.*", "--out", "out"] : [];
        using var output = new StringWriter();
        using var error = new StringWriter();
        HaruspexCli.Run(["generate", .. arguments], output, error, folder.Path);
        return CliOutput.Shown(error.ToString() + output, folder.Path);
    }

    /// <summary>
    /// What <c>haruspex check</c> prints after <c>haruspex generate</c> has written the project <paramref name="before"/> (the
    /// example before), and the project has become <paramref name="sql"/>; set out as for <see cref="Run"/>.
    /// </summary>
    private static string Check(string before, string sql, WorkedExample.Output check)
    {
        using var folder = new TempDirectory();
        folder.File("db/project.sql", before);
        if (check.Config is { } config)
            folder.File("haruspex.json", config);
        string[] arguments = check.Arguments.Count > 0 ? [.. check.Arguments] : check.Config is null ? ["db", "--include", "*.*", "--out", "out"] : [];
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, HaruspexCli.Run(["generate", .. arguments], output, error, folder.Path));
        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        folder.File("db/project.sql", sql);
        HaruspexCli.Run(["check", .. arguments], output, error, folder.Path);
        return CliOutput.Shown(error.ToString() + output, folder.Path);
    }

    /// <summary>
    /// The example's project, read and inferred as the CLI does with no config file: every procedure of an example that stands
    /// alone, or, beside a corpus, the procedures the example defines.
    /// </summary>
    private static IrDocument Infer(WorkedExample example) =>
        example.Over is { } over ? InferOver(over, example.Sql) : InferProcedure.Infer("*.*", ("example.sql", example.Sql));

    /// <summary>
    /// The example read in one folder with the scripts of the corpus named <paramref name="over"/>, as the corpus figures read
    /// them, and the procedures it defines inferred with no config file. The corpus is read whole, so the example's tables are
    /// the corpus's own, with every constraint and key the sample declares.
    /// </summary>
    private static IrDocument InferOver(string over, string sql)
    {
        var corpus = SampleCorpus.All.Single(c => c.Name == over);
        using var folder = new TempDirectory();
        foreach (var script in corpus.Scripts)
        {
            var copy = Path.Combine(folder.Path, script);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(Path.Combine(corpus.ProjectFolder, script), copy);
        }

        var example = folder.File("example.sql", sql);
        var defined = SqlScriptFolder.Read(Path.GetDirectoryName(example)!, ["example.sql"]).Model.Procedures.Select(p => p.Name).ToList();
        var project = SqlProject.Load(folder.Path);
        var errors = project.Diagnostics.Where(d => d.IsError).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        return project.Infer(defined).Expected();
    }

    private static JsonNode? IrShown(IrDocument document, string? property)
    {
        var node = JsonNode.Parse(IrJsonWriter.Write(document))!;
        return property is null ? node : node[property];
    }

    /// <summary>The JSON Schema document, or the one definition of it the block shows.</summary>
    private static JsonNode? JsonSchemaShown(IrDocument document, string? definition)
    {
        var node = JsonNode.Parse(JsonSchemaEmitter.Emit(document).Expected())!;
        return definition is null ? node : node["$defs"]?[definition];
    }

    private static string Text(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string Json(JsonNode? node) => node?.ToJsonString(Indented) ?? "null";
}

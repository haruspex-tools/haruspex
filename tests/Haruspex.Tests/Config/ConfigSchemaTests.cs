using System.Text.Json;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Tests.Schemas;

namespace Haruspex.Tests.Config;

/// <summary>
/// The published JSON Schema (<c>schemas/haruspex.schema.json</c>), which editors use to complete and check
/// <c>haruspex.json</c>, checked against valid and invalid examples, and against the config reader: the two must agree on
/// every example, so the schema never accepts what haruspex refuses, nor the reverse.
/// </summary>
/// <remarks>
/// What a schema can't say, or can say only awkwardly, is the reader's alone, and has its own tests in <c>ConfigReaderTests</c>:
/// that a procedure key has exactly two parts (the schema only rejects wildcards and empty keys), that a pattern or a
/// path is well formed, that a regular expression compiles, that two schema keys differ in more than case, and that a
/// name is not a reserved word.
/// </remarks>
public class ConfigSchemaTests
{
    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static string SchemaPath => Path.Combine(RepositoryPaths.Root, "schemas", "haruspex.schema.json");

    private static string Examples => Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.Tests", "Config", "ConfigExamples");

    private static SchemaSubset Schema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        return new SchemaSubset(schema.RootElement.Clone());
    }

    private static List<string> SchemaErrors(string file)
    {
        using var instance = JsonDocument.Parse(File.ReadAllText(file), Jsonc);
        return Schema().Validate(instance.RootElement);
    }

    private static EquatableArray<Diagnostic> ReaderProblems(string file) => ConfigReader.Read(file).Diagnostics;

    public static TheoryData<string> Valid { get; } = [.. Directory.GetFiles(Path.Combine(Examples, "valid")).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    public static TheoryData<string> Invalid { get; } = [.. Directory.GetFiles(Path.Combine(Examples, "invalid")).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The schema is checked against the draft 2020-12 meta-schema and compiled in Ajv's strict mode, as an editor's validator or a
    /// consumer would compile it, as the IR's is (<c>IrJsonSchemaTests</c>): strict mode refuses a keyword it doesn't know, a
    /// <c>required</c> beside no <c>type</c> and a property required where none is defined.
    /// </summary>
    [Fact]
    public void The_schema_is_a_valid_2020_12_schema_that_ajv_compiles_in_strict_mode()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the config schema was not compiled with Ajv");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "meta-schema.mjs"), SchemaPath], RepositoryPaths.EmitCheck, out var output, out var why),
            $"schemas/haruspex.schema.json doesn't compile in Ajv's strict mode ({why}):\n{output}");
        Assert.Contains("1 document(s), 0 failed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_is_a_2020_12_schema_with_its_id()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal("https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json", schema.RootElement.GetProperty("$id").GetString());
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void A_valid_example_passes_the_schema_and_the_reader(string name)
    {
        var file = Path.Combine(Examples, "valid", name);

        Assert.Empty(SchemaErrors(file));
        Assert.Empty(ReaderProblems(file));
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_example_fails_the_schema_and_the_reader(string name)
    {
        var file = Path.Combine(Examples, "invalid", name);

        Assert.NotEmpty(SchemaErrors(file));
        Assert.Contains(ReaderProblems(file), p => p.IsError);
    }

    [Fact]
    public void The_shop_fixture_s_config_passes_the_schema() =>
        Assert.Empty(SchemaErrors(Path.Combine(Fixture.Named("shop").Folder, "haruspex.json")));

    [Fact]
    public void Every_example_names_the_schema() =>
        Assert.All(
            Directory.GetFiles(Examples, "*", SearchOption.AllDirectories),
            file => Assert.Contains("\"$schema\": \"../../../../schemas/haruspex.schema.json\"", File.ReadAllText(file), StringComparison.Ordinal));
}

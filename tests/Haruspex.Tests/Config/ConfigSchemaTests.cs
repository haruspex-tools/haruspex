using System.Text;
using System.Text.Json;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Tests.Schemas;

namespace Haruspex.Tests.Config;

/// <summary>
/// The published JSON Schema (<c>schemas/haruspex.schema.json</c>), which editors use to complete and check
/// <c>haruspex.json</c>, held to the config reader: every valid example passes both and every invalid one fails both, and each
/// config only the reader refuses names its rule, which the configuration page lists among the rules an editor can't check
/// (<see cref="ReaderOnly"/>). Ajv, the validator an editor or a consumer is likeliest to use, gives every example and every such
/// config the verdict the tests' own validator gives.
/// </summary>
/// <remarks>
/// The examples hold the agreement one case at a time, as the IR's schema and reader are held (<c>IrReaderSchemaAgreementTests</c>):
/// a new setting needs an invalid example for each way the schema refuses it, and a case here for each way only the reader does.
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
        Assert.Equal("https://haruspex.tools/schemas/main/haruspex.schema.json", schema.RootElement.GetProperty("$id").GetString());
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

    /// <summary>
    /// Configs the schema accepts and the reader refuses, each with the code the reader gives and the rule a schema can't say. The
    /// rules are those the configuration page lists under "What an editor can't check", in its words.
    /// </summary>
    private static readonly Dictionary<string, (string Config, string Code, string Rule)> ReaderOnlyCases = new(StringComparer.Ordinal)
    {
        ["a key written twice"] = ("""{ "objects": "strip", "objects": "strict" }""", "HAR3003", "A key written twice in one object"),
        ["a procedure named in three parts"] = ("""{ "procedures": { "sales.orders.get_order": { "name": "Order" } } }""", "HAR3006", "A procedure named in other than two parts"),
        ["a pattern left open"] = ("""{ "schemas": { "sales": { "include": ["[get_*"] } } }""", "HAR3006", "A pattern, a path or a regular expression that is not well formed"),
        ["a path left open"] = ("""{ "procedures": { "sales.get_order": { "paths": { "lines[.sku": { "name": "Line" } } } } }""", "HAR3017", "A pattern, a path or a regular expression that is not well formed"),
        ["a regular expression left open"] = ("""{ "naming": { "replace": [{ "pattern": "(", "with": "" }] } }""", "HAR3010", "A pattern, a path or a regular expression that is not well formed"),
        ["a reserved word as a name"] = ("""{ "procedures": { "sales.get_order": { "name": "delete" } } }""", "HAR3019", "A name that is a reserved word"),
        ["a reserved word as a set's name"] = ("""{ "procedures": { "sales.get_order": { "sets": ["order", "default"] } } }""", "HAR3023", "A name that is a reserved word"),
        ["two schemas differing in case"] = ("""{ "schemas": { "sales": { "include": ["*"] }, "Sales": { "include": ["*"] } } }""", "HAR3008", "Two names that differ only in case"),
        ["two modules differing in case"] = ("""{ "split": { "sales": ["sales"], "Sales": ["shop"] } }""", "HAR3024", "Two names that differ only in case"),
        ["a schema in two modules"] = ("""{ "split": { "a": ["sales"], "b": ["Sales"] } }""", "HAR3025", "A schema in two modules"),
        ["a warning acknowledged twice"] = ("""{ "acknowledge": [{ "code": "HAR1008", "subject": "Stamp", "why": "it stamps the build" }, { "code": "HAR1008", "subject": "Stamp", "why": "it adds no scripts" }] }""", "HAR3005", "A warning acknowledged twice"),
        ["a collation SQL Server lacks"] = ("""{ "collation": "Latin1_General_100_CI_XX" }""", "HAR3005", "A collation SQL Server does not have"),
        ["a folder that isn't there"] = ("""{ "references": ["no-such-folder"] }""", "HAR3021", "A file or folder that is not there"),
        ["a schema file that isn't there"] = ("""{ "columns": { "shop.orders.settings": { "json": "no-such.schema.json" } } }""", "HAR3001", "A file or folder that is not there"),
        ["a rowset that isn't an object"] = ("""{ "procedures": { "sales.search": { "results": { "rowset": { "type": "string" } }, "why": "dynamic SQL" } } }""", "HAR3101", "A declared JSON Schema haruspex can't type"),
    };

    public static TheoryData<string> ReaderOnly { get; } = [.. ReaderOnlyCases.Keys];

    [Theory]
    [MemberData(nameof(ReaderOnly))]
    public void A_config_only_the_reader_refuses_breaks_a_rule_the_configuration_page_lists(string name)
    {
        var (config, code, rule) = ReaderOnlyCases[name];
        using var folder = new TempDirectory();

        using (var instance = JsonDocument.Parse(config, Jsonc))
            Assert.Empty(Schema().Validate(instance.RootElement));
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(config), Path.Combine(folder.Path, "haruspex.json"), folder.Path, new ReservedWords(TypeScriptNames.Language, TypeScriptNames.IsReserved));
        Assert.Contains(read.Diagnostics, d => d.IsError && d.Code.ToString() == code);
        Assert.Contains($"- **{rule}**", EditorCantCheck(), StringComparison.Ordinal);
    }

    /// <summary>Every rule the configuration page lists as one an editor can't check has a config here that breaks it.</summary>
    [Fact]
    public void Every_rule_the_configuration_page_lists_has_a_config_that_breaks_it()
    {
        var listed = EditorCantCheck().Split('\n').Where(l => l.StartsWith("- **", StringComparison.Ordinal)).Select(l => l[4..l.IndexOf("**", 4, StringComparison.Ordinal)]);

        Assert.Equal(listed.Order(StringComparer.Ordinal), ReaderOnlyCases.Values.Select(c => c.Rule).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Ajv gives every example, and every config only the reader refuses, the verdict the tests' own validator gives, so a keyword
    /// <see cref="SchemaSubset"/> read differently from a real validator (the <c>not</c> and <c>items</c> that say "contains") can't
    /// pass the schema and these tests both.
    /// </summary>
    [Fact]
    public void Ajv_gives_every_example_the_verdict_the_schema_subset_gives()
    {
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Examples, "*", SearchOption.AllDirectories))
            documents[Path.GetRelativePath(Examples, file)] = Plain(File.ReadAllText(file));
        foreach (var (name, (config, _, _)) in ReaderOnlyCases)
            documents[name] = Plain(config);

        var verdicts = AjvVerdicts.Of(SchemaPath, documents);

        Assert.All(documents, d =>
        {
            using var instance = JsonDocument.Parse(d.Value);
            var subset = Schema().Validate(instance.RootElement);
            Assert.True(subset.Count == 0 == (verdicts[d.Key].Count == 0), $"{d.Key}: Ajv says {Verdict(verdicts[d.Key])}, the schema subset {Verdict(subset)}.");
        });
    }

    /// <summary>The configuration page's list of the rules an editor can't check.</summary>
    private static string EditorCantCheck()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "configuration.md"));
        var start = page.IndexOf("## What an editor can't check", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/configuration.md has no section \"What an editor can't check\"");
        var end = page.IndexOf("\n## ", start + 3, StringComparison.Ordinal);
        return page[start..(end < 0 ? page.Length : end)];
    }

    /// <summary>The text as plain JSON, its comments and trailing commas left out, as Ajv's <c>JSON.parse</c> reads it.</summary>
    private static string Plain(string jsonc)
    {
        using var document = JsonDocument.Parse(jsonc, Jsonc);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string Verdict(List<string> errors) => errors.Count == 0 ? "valid" : string.Join("; ", errors);

    [Fact]
    public void The_shop_fixture_s_config_passes_the_schema() =>
        Assert.Empty(SchemaErrors(Path.Combine(Fixture.Named("shop").Folder, "haruspex.json")));

    [Fact]
    public void Every_example_names_the_schema() =>
        Assert.All(
            Directory.GetFiles(Examples, "*", SearchOption.AllDirectories),
            file => Assert.Contains("\"$schema\": \"../../../../schemas/haruspex.schema.json\"", File.ReadAllText(file), StringComparison.Ordinal));
}

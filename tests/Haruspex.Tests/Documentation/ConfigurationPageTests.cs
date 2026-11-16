using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haruspex.Tests.Schemas;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// The configuration reference, <c>docs/configuration.md</c>, held to the config file's published JSON Schema,
/// <c>schemas/haruspex.schema.json</c>: every setting the schema has is documented, and every setting the page shows is
/// one the schema has. The schema is what editors check a file against and <c>ConfigSchemaTests</c> holds it to the reader,
/// so a page that agrees with it agrees with haruspex.
/// </summary>
/// <remarks>
/// A setting is documented when its name is written as code in the page's prose, outside the examples, alone or as the key of
/// a value (<c>`objects`</c>, <c>`"values": "exact"`</c>), so that a reader searching the page for it finds what it does. A
/// setting the page shows is a key in one of its <c>jsonc</c> examples, or a name in code in a heading, which is where the page
/// says which setting a section is about. An example may be a fragment of a file, such as one <c>"procedures"</c> member; it
/// is checked as the file that holds only it.
/// </remarks>
public partial class ConfigurationPageTests
{
    private const string Page = "docs/configuration.md";

    private static string Markdown => File.ReadAllText(Path.Combine(RepositoryPaths.Root, Page));

    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static TheoryData<int> Examples { get; } = [.. MarkdownText.FencedBlocks(Markdown).Where(b => b.Words.Count > 0 && b.Words[0] == "jsonc").Select(b => b.Line)];

    [Fact]
    public void The_page_has_examples_of_the_config_file() => Assert.True(Examples.Count >= 3, $"{Examples.Count.ToString(CultureInfo.InvariantCulture)} examples found.");

    [Fact]
    public void Every_setting_in_the_schema_is_documented_on_the_page()
    {
        var prose = MarkdownText.InlineCode(Markdown).Select(c => c.Code).ToList();
        var missing = Settings(SchemaDocument().RootElement, "#")
            .Where(s => !prose.Any(code => Names(code, s.Name)))
            .Select(s => $"{s.Name} ({s.At})")
            .ToList();

        Assert.True(missing.Count == 0, $"{Page} doesn't document these settings of the schema:\n{string.Join("\n", missing)}");
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_example_on_the_page_is_valid_against_the_schema(int line)
    {
        var block = MarkdownText.FencedBlocks(Markdown).Single(b => b.Line == line);
        using var example = Parse(block.Text);
        var errors = new SchemaSubset(SchemaDocument().RootElement.Clone()).Validate(example.RootElement);

        Assert.True(errors.Count == 0, $"{Page}:{line.ToString(CultureInfo.InvariantCulture)}: the example isn't a valid config file:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void Every_setting_a_heading_names_is_in_the_schema()
    {
        var settings = Settings(SchemaDocument().RootElement, "#").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = MarkdownText.InlineCode(Markdown)
            .Where(c => Heading().IsMatch(Markdown.Split('\n')[c.Line - 1]) && Identifier().IsMatch(c.Code))
            .Where(c => !settings.Contains(c.Code))
            .Select(c => $"{Page}:{c.Line.ToString(CultureInfo.InvariantCulture)}: {c.Code}")
            .ToList();

        Assert.True(unknown.Count == 0, $"Headings name settings the schema doesn't have:\n{string.Join("\n", unknown)}");
    }

    [Theory]
    [InlineData("`objects`", "objects", true)]
    [InlineData("`\"values\": \"exact\"`", "values", true)]
    [InlineData("`nullable: false`", "nullable", true)]
    [InlineData("`mayBeAbsent`", "absent", false)]
    [InlineData("`$schema`", "schema", false)]
    public void A_setting_is_named_in_code_only_as_a_whole_word(string code, string setting, bool named) =>
        Assert.Equal(named, Names(code.Trim('`'), setting));

    [Fact]
    public void A_fragment_of_a_file_is_read_as_the_file_that_holds_only_it()
    {
        using var fragment = Parse("\"session\": { \"ansiWarnings\": true }");

        Assert.True(fragment.RootElement.GetProperty("session").GetProperty("ansiWarnings").GetBoolean());
    }

    /// <summary>
    /// The settings of the config file: every property the schema declares, wherever it is declared, with where. The JSON
    /// Schemas a declared result holds are the user's own, not settings, and the schema leaves them open.
    /// </summary>
    private static IEnumerable<(string Name, string At)> Settings(JsonElement schema, string at)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (var keyword in schema.EnumerateObject())
        {
            if (keyword.Name == "properties")
            {
                foreach (var property in keyword.Value.EnumerateObject())
                {
                    yield return (property.Name, $"{at}/properties/{property.Name}");
                    foreach (var nested in Settings(property.Value, $"{at}/properties/{property.Name}"))
                        yield return nested;
                }
            }
            else if (keyword.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in Settings(keyword.Value, $"{at}/{keyword.Name}"))
                    yield return nested;
            }
            else if (keyword.Value.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var item in keyword.Value.EnumerateArray())
                {
                    foreach (var nested in Settings(item, $"{at}/{keyword.Name}/{i++.ToString(CultureInfo.InvariantCulture)}"))
                        yield return nested;
                }
            }
        }
    }

    /// <summary>Whether code written in prose names the setting: as a word of its own, not part of a longer name.</summary>
    private static bool Names(string code, string setting) =>
        Regex.IsMatch(code, $@"(?<![A-Za-z0-9_$]){Regex.Escape(setting)}(?![A-Za-z0-9_])", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>An example as a document: a whole file, or a fragment of one (members without their braces) inside braces.</summary>
    private static JsonDocument Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text, Jsonc);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse($"{{\n{text}\n}}", Jsonc);
        }
    }

    private static JsonDocument SchemaDocument() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root, "schemas", "haruspex.schema.json")));

    [GeneratedRegex(@"^#{1,6}\s")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[A-Za-z$][A-Za-z0-9]*$")]
    private static partial Regex Identifier();
}

using System.Text;

namespace Haruspex.Tests;

/// <summary>
/// Reading <c>haruspex.json</c>: JSON with comments, every setting checked, and every problem reported with the path
/// inside the file and its line, as <c>file:line:column: error: path: message</c>.
/// </summary>
public class ConfigReaderTests
{
    private static ConfigReadResult Read(string text) =>
        ConfigReader.Read(Encoding.UTF8.GetBytes(text), "haruspex.json", Path.Combine(Path.GetTempPath(), "project"));

    private static string Single(string text)
    {
        var result = Read(text);
        Assert.Null(result.Config);
        return Assert.Single(result.Problems).ToString();
    }

    [Fact]
    public void Reads_every_setting_from_json_with_comments_and_trailing_commas()
    {
        var directory = Path.Combine(Path.GetTempPath(), "project");
        var result = Read("""
            // A comment before the object.
            {
              "$schema": "./haruspex.schema.json",
              "source": "db", /* a block comment */
              "out": "../generated",
              "objects": "strict",
              "naming": { "replace": [{ "pattern": "#$", "with": "Row" }], "case": "camel" },
              "schemas": {
                "sales": { "include": ["get_*", "list_*",], "exclude": ["*_internal"] },
                "RDF.": { "include": ["GetPropertyList"], "prefix": "Rdf", "naming": { "case": "preserve", "replace": [{ "pattern": "^Get", "with": "" }] } },
                "reference": {},
              },
              "procedures": {
                "sales.get_order": {
                  "name": "OrderDetail",
                  "paths": {
                    "lines[]": { "name": "OrderLine" },
                    "note": { "nullable": false, "why": "always written" },
                    "status": { "values": "exact", "why": "lower case only" },
                    "settings": { "type": "SettingsSchema[]", "import": "./settings" },
                  },
                },
              },
            }
            """);

        Assert.Empty(result.Problems);
        var config = Assert.IsType<HaruspexConfig>(result.Config);
        Assert.Equal(Path.Combine(directory, "db"), config.Source);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory, "..", "generated")), config.Out);
        Assert.Equal(["sales", "RDF.", "reference"], config.Schemas.Select(s => s.Name));
        Assert.Equal(["get_*", "list_*"], config.Schemas[0].Include!.Select(p => p.Pattern.Text));
        Assert.Equal(9, config.Schemas[0].Include![0].At.Line);
        Assert.Equal(["*_internal"], config.Schemas[0].Exclude.Select(p => p.Pattern.Text));
        Assert.Equal("", config.Schemas[0].Prefix);
        Assert.Equal("Rdf", config.Schemas[1].Prefix);
        Assert.Null(config.Schemas[2].Include);
        Assert.True(config.SelectsAnything);
        Assert.Equal(ObjectsPolicy.Strict, config.Objects);
        Assert.Equal(NamingCase.Camel, config.Naming.Case);
        Assert.Equal("Row", Assert.Single(config.Naming.Replace).With);

        // A schema's naming merges over the top level's: its case replaces it, its replacements run after.
        var rdf = config.NamingFor("rdf.");
        Assert.Equal(NamingCase.Preserve, rdf.Case);
        Assert.Equal(["Row", ""], rdf.Replace.Select(r => r.With));
        Assert.Equal("Rdf", rdf.Prefix);
        Assert.Equal(config.Naming, config.NamingFor("hr"));
        var procedure = Assert.Single(config.Procedures);
        Assert.Equal("OrderDetail", procedure.Name);
        Assert.Equal(["lines[]", "note", "status", "settings"], procedure.Paths.Select(p => p.Path));
        Assert.Equal("OrderLine", procedure.Paths[0].Name);
        Assert.True(procedure.Paths[1].NotNull);
        Assert.Equal("always written", procedure.Paths[1].Why);
        Assert.True(procedure.Paths[2].ExactValues);
        Assert.Equal(("SettingsSchema[]", "./settings"), (procedure.Paths[3].Type, procedure.Paths[3].Import));
    }

    [Fact]
    public void An_empty_object_is_a_valid_config_with_every_default()
    {
        var config = Assert.IsType<HaruspexConfig>(Read("{}").Config);

        Assert.Empty(config.Schemas);
        Assert.False(config.SelectsAnything);
        Assert.Equal(ObjectsPolicy.Strip, config.Objects);
        Assert.Equal(NamingRules.Default, config.Naming);
        Assert.Null(config.Source);
        Assert.Null(config.Out);
    }

    [Fact]
    public void A_byte_order_mark_is_allowed()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{ \"objects\": \"strict\" }")).ToArray();

        var result = ConfigReader.Read(bytes, "haruspex.json", Path.GetTempPath());

        Assert.Equal(ObjectsPolicy.Strict, result.Config!.Objects);
    }

    [Fact]
    public void Every_problem_is_reported_not_only_the_first()
    {
        var result = Read("""{ "objects": "loose", "colour": 1, "schemas": { "sales": { "include": [] } } }""");

        Assert.Equal(3, result.Problems.Count);
    }

    /// <summary>Text that is not JSON with comments: the reader's message, at the line and column it stopped.</summary>
    [Theory]
    [InlineData("{\n  \"include\": [\"a.b\"\n}", "haruspex.json:3:1: error: '}' is invalid without a matching open.")]
    [InlineData("{} {}", "haruspex.json:1:4: error: '{' is invalid after a single JSON value. Expected end of data.")]
    [InlineData("{\n  \"objects\": \"strip\",\n  \"objects\": \"strict\"\n}", "haruspex.json:3:3: error: objects: The key \"objects\" is written twice in one object.")]
    [InlineData("{ \"schemas\": { \"sales\": { \"include\": [\"*\"], \"include\": [\"x\"] } } }", "haruspex.json:1:45: error: schemas.sales.include: The key \"include\" is written twice in one object.")]
    public void Text_that_is_not_json_is_an_error_with_its_line(string text, string expected) =>
        Assert.Equal(expected, Single(text));

    [Fact]
    public void An_empty_file_is_an_error()
    {
        var problem = Single("");

        Assert.StartsWith("haruspex.json:1:1: error: ", problem, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Errors { get; } = new()
    {
        // The file as a whole, and its settings' types.
        { "[]", "haruspex.json:1:1: error: must be an object." },
        { """{ "colour": "red" }""", "haruspex.json:1:3: error: colour: unknown setting \"colour\". The settings here are $schema, source, out, schemas, objects, naming, procedures." },
        { """{ "include": ["sales.*"] }""", "haruspex.json:1:3: error: include: unknown setting \"include\". The settings here are $schema, source, out, schemas, objects, naming, procedures." },
        { """{ "$schema": 1 }""", "haruspex.json:1:14: error: $schema: must be a string." },
        { """{ "source": "" }""", "haruspex.json:1:13: error: source: must not be empty." },
        { """{ "out": 3 }""", "haruspex.json:1:10: error: out: must be a string." },
        { """{ "objects": "loose" }""", "haruspex.json:1:14: error: objects: must be \"strip\" or \"strict\", not \"loose\"." },

        // Schemas and selection.
        { """{ "schemas": [] }""", "haruspex.json:1:14: error: schemas: must be an object." },
        { """{ "schemas": { "": {} } }""", "haruspex.json:1:16: error: schemas[\"\"]: a schema's name must not be empty." },
        { """{ "schemas": { "sales": [] } }""", "haruspex.json:1:25: error: schemas.sales: must be an object." },
        { """{ "schemas": { "sales": { "output": "x" } } }""", "haruspex.json:1:27: error: schemas.sales.output: unknown setting \"output\". The settings here are include, exclude, prefix, naming." },
        { "{ \"schemas\": {\n\"sales\": {},\n\"SALES\": {} } }", "haruspex.json:3:1: error: schemas.SALES: the schema \"sales\" already has a block, at line 2; schema names are compared ignoring case, as SQL Server compares them. Merge the two." },
        { """{ "schemas": { "sales": { "include": "*" } } }""", "haruspex.json:1:38: error: schemas.sales.include: must be an array of patterns matching the schema's procedures by name, such as \"get_*\"." },
        { """{ "schemas": { "sales": { "include": [] } } }""", "haruspex.json:1:38: error: schemas.sales.include: must list at least one pattern; with none, nothing is selected. Use \"*\" to select every procedure of the schema." },
        { """{ "schemas": { "sales": { "include": [1] } } }""", "haruspex.json:1:39: error: schemas.sales.include[0]: must be a string." },
        { """{ "schemas": { "sales": { "include": [""] } } }""", "haruspex.json:1:39: error: schemas.sales.include[0]: '' is empty: a pattern names procedures of its schema, such as get_order, list_* or *." },
        { """{ "schemas": { "sales": { "include": ["*"], "exclude": ["[x"] } } }""", "haruspex.json:1:57: error: schemas.sales.exclude[0]: '[x' has a '[' without its closing ']'." },
        { """{ "schemas": { "audit": { "exclude": ["nothing_here"] } } }""", "haruspex.json:1:27: error: schemas.audit.exclude: does nothing without an \"include\" in the same block, since nothing in the schema is selected. Add the include, or remove the exclude." },
        { """{ "schemas": { "sales": { "prefix": 1 } } }""", "haruspex.json:1:37: error: schemas.sales.prefix: must be a string." },
        { """{ "schemas": { "sales": { "naming": { "prefix": "S" } } } }""", "haruspex.json:1:39: error: schemas.sales.naming.prefix: unknown setting \"prefix\". The settings here are replace, case." },

        // Naming.
        { """{ "naming": [] }""", "haruspex.json:1:13: error: naming: must be an object." },
        { """{ "naming": { "style": "pascal" } }""", "haruspex.json:1:15: error: naming.style: unknown setting \"style\". The settings here are replace, case." },
        { """{ "naming": { "stripSchema": ["dbo"] } }""", "haruspex.json:1:15: error: naming.stripSchema: unknown setting \"stripSchema\". The settings here are replace, case." },
        { """{ "naming": { "case": "snake" } }""", "haruspex.json:1:23: error: naming.case: must be \"pascal\" or \"camel\" or \"preserve\", not \"snake\"." },
        { """{ "naming": { "replace": {} } }""", "haruspex.json:1:26: error: naming.replace: must be an array of { \"pattern\": \"…\", \"with\": \"…\" }." },
        { """{ "naming": { "replace": [1] } }""", "haruspex.json:1:27: error: naming.replace[0]: must be an object." },
        { """{ "naming": { "replace": [{ "with": "x" }] } }""", "haruspex.json:1:27: error: naming.replace[0]: needs \"pattern\", the regular expression to replace." },
        { """{ "naming": { "replace": [{ "pattern": "x" }] } }""", "haruspex.json:1:27: error: naming.replace[0]: needs \"with\", the replacement (\"\" to remove the match)." },
        { """{ "naming": { "replace": [{ "pattern": "x", "with": "", "flags": "i" }] } }""", "haruspex.json:1:57: error: naming.replace[0].flags: unknown setting \"flags\". The settings here are pattern, with." },

        // Procedures.
        { """{ "procedures": [] }""", "haruspex.json:1:17: error: procedures: must be an object." },
        { """{ "procedures": { "get_order": {} } }""", "haruspex.json:1:19: error: procedures.get_order: 'get_order' is not a two-part name such as sales.get_order, sales.* or *.list_*." },
        { """{ "procedures": { "sales.*": {} } }""", "haruspex.json:1:19: error: procedures[\"sales.*\"]: names one procedure, so it cannot use * or ?; write a part that holds them in brackets, as [a*b]." },
        { """{ "procedures": { "sales.p": 1 } }""", "haruspex.json:1:30: error: procedures[\"sales.p\"]: must be an object." },
        { """{ "procedures": { "sales.p": { "rename": "X" } } }""", "haruspex.json:1:32: error: procedures[\"sales.p\"].rename: unknown setting \"rename\". The settings here are name, paths." },
        { """{ "procedures": { "sales.p": { "name": "Order Detail" } } }""", "haruspex.json:1:40: error: procedures[\"sales.p\"].name: 'Order Detail' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word." },
        { """{ "procedures": { "sales.p": { "name": "delete" } } }""", "haruspex.json:1:40: error: procedures[\"sales.p\"].name: 'delete' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word." },
        { """{ "procedures": { "sales.p": { "paths": [] } } }""", "haruspex.json:1:41: error: procedures[\"sales.p\"].paths: must be an object." },

        // Path overrides.
        { """{ "procedures": { "sales.p": { "paths": { "a..b": { "name": "X" } } } } }""", "haruspex.json:1:43: error: procedures[\"sales.p\"].paths[\"a..b\"]: 'a..b' is not a path: at 1, a '.' must be followed by a key; write a key that is not an identifier as [\"key\"]." },
        { """{ "procedures": { "sales.p": { "paths": { "a": 1 } } } }""", "haruspex.json:1:48: error: procedures[\"sales.p\"].paths.a: must be an object." },
        { """{ "procedures": { "sales.p": { "paths": { "a": {} } } } }""", "haruspex.json:1:48: error: procedures[\"sales.p\"].paths.a: says nothing to override: give \"name\", \"nullable\", \"type\" or \"values\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "optional": true } } } } }""", "haruspex.json:1:50: error: procedures[\"sales.p\"].paths.a.optional: unknown setting \"optional\". The settings here are name, nullable, why, type, import, values." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": true, "why": "x" } } } } }""", "haruspex.json:1:62: error: procedures[\"sales.p\"].paths.a.nullable: can only be false. A value is nullable unless the database proves otherwise, so there is nothing to declare." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": "no", "why": "x" } } } } }""", "haruspex.json:1:62: error: procedures[\"sales.p\"].paths.a.nullable: must be false." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": false } } } } }""", "haruspex.json:1:48: error: procedures[\"sales.p\"].paths.a: \"nullable\": false needs \"why\": the database doesn't prove it, so say what does." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": false, "why": "" } } } } }""", "haruspex.json:1:76: error: procedures[\"sales.p\"].paths.a.why: must not be empty." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "values": "exact" } } } } }""", "haruspex.json:1:48: error: procedures[\"sales.p\"].paths.a: \"values\": \"exact\" needs \"why\": the database compares the values under its collation, so say why they are exact." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "values": "enum", "why": "x" } } } } }""", "haruspex.json:1:60: error: procedures[\"sales.p\"].paths.a.values: must be \"exact\", not \"enum\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "type": "Tag" } } } } }""", "haruspex.json:1:48: error: procedures[\"sales.p\"].paths.a: \"type\" needs \"import\": the module that exports the schema." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "type": "Tag Schema", "import": "x" } } } } }""", "haruspex.json:1:58: error: procedures[\"sales.p\"].paths.a.type: must be the name of an exported schema, with [] for each level of array, such as \"TagSchema[]\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "type": "Tag", "import": "" } } } } }""", "haruspex.json:1:75: error: procedures[\"sales.p\"].paths.a.import: must not be empty." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "import": "x", "name": "A" } } } } }""", "haruspex.json:1:48: error: procedures[\"sales.p\"].paths.a: \"import\" needs \"type\": the schema to import from it." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "name": "A", "why": "x" } } } } }""", "haruspex.json:1:48: error: procedures[\"sales.p\"].paths.a: \"why\" explains a claim, but there is none here: give \"nullable\", \"type\" or \"values\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "name": "1st" } } } } }""", "haruspex.json:1:58: error: procedures[\"sales.p\"].paths.a.name: '1st' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word." },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Each_problem_has_its_path_line_and_message(string text, string expected) =>
        Assert.Equal(expected, Single(text));

    [Theory]
    [InlineData("(", "naming.replace[0].pattern: is not a regular expression haruspex can use: ")]
    [InlineData("(a)\\1", "naming.replace[0].pattern: is not a regular expression haruspex can use: ")]
    public void A_regular_expression_that_cannot_be_used_is_an_error(string pattern, string expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(pattern);

        var problem = Single($$"""{ "naming": { "replace": [{ "pattern": {{json}}, "with": "" }] } }""");

        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_looks_in_the_folder_and_every_folder_above_it()
    {
        using var root = new TempDirectory();
        var file = root.File("haruspex.json", "{}");
        var nested = Path.Combine(root.Path, "a", "b");
        Directory.CreateDirectory(nested);

        Assert.Equal(file, ConfigReader.Find(nested));
        Assert.Equal(file, ConfigReader.Find(root.Path));
    }

    [Fact]
    public void Find_gives_null_when_there_is_no_file()
    {
        using var root = new TempDirectory();

        // A temp folder has no haruspex.json above it on a test machine; if one ever did, this would say so.
        Assert.Null(ConfigReader.Find(root.Path));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_an_error()
    {
        using var root = new TempDirectory();
        var missing = Path.Combine(root.Path, "haruspex.json");

        var result = ConfigReader.Read(missing);

        Assert.Null(result.Config);
        Assert.StartsWith($"{missing}: error: cannot read the config file: ", Assert.Single(result.Problems).ToString(), StringComparison.Ordinal);
    }
}

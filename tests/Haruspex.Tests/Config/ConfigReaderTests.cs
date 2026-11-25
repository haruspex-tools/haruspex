using System.Text;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Config;

/// <summary>
/// Reading <c>haruspex.json</c>: JSON with comments, every setting checked, and every problem reported with the path
/// inside the file and its line, as <c>file:line:column: error HAR3005: path: message</c>.
/// </summary>
public class ConfigReaderTests
{
    private static Outcome<HaruspexConfig> Read(string text) =>
        ConfigReader.Read(Encoding.UTF8.GetBytes(text), "haruspex.json", Path.Combine(Path.GetTempPath(), "project"), new ReservedWords(TypeScriptNames.Language, TypeScriptNames.IsReserved));

    private static string Single(string text)
    {
        var result = Read(text);
        Assert.IsType<Failed<HaruspexConfig>>(result);
        return Assert.Single(result.Diagnostics).ToString();
    }

    [Fact]
    public void Comments_explain_the_surprises_unless_the_file_asks_for_none_or_all()
    {
        Assert.Equal(IrComments.Surprises, Read("{}").Expected().Comments);
        Assert.Equal(IrComments.None, Read("""{ "comments": "none" }""").Expected().Comments);
        Assert.Equal(IrComments.All, Read("""{ "comments": "all" }""").Expected().Comments);
        Assert.Contains("comments", Single("""{ "comments": "some" }"""), StringComparison.Ordinal);
    }

    [Fact]
    public void Queries_are_one_entry_or_several_each_relative_to_the_config_file_s_folder()
    {
        var one = Read("""{ "queries": "./sql/" }""").Expected();
        var several = Read("""{ "queries": ["sql/**/*.sql", ".", "reports\\monthly.sql"] }""").Expected();

        Assert.Equal(["sql"], one.Queries.Select(q => q.Text));
        Assert.Equal(["sql/**/*.sql", ".", "reports/monthly.sql"], several.Queries.Select(q => q.Text));
        Assert.Equal([true, false, false], several.Queries.Select(q => q.IsPattern));
        Assert.True(one.SelectsAnything);
        Assert.False(one.SelectsProcedures);
    }

    [Fact]
    public void The_output_is_one_module_unless_split_by_schema_or_into_listed_modules()
    {
        var none = Read("{}").Expected();
        var bySchema = Read("""{ "split": "schema" }""").Expected();
        var listed = Read("""{ "split": { "sales": ["sales", "shop"], "admin": ["admin"], "reports": [] } }""").Expected();

        Assert.Null(none.Split);
        Assert.True(bySchema.Split?.BySchema);
        var modules = listed.Split?.Modules ?? [];
        Assert.Equal(["sales", "admin", "reports"], modules.Select(m => m.Name));
        Assert.Equal(["sales", "shop"], modules[0].Schemas);
        Assert.Empty(modules[2].Schemas);
    }

    [Fact]
    public void A_baseline_is_read_as_a_path_from_the_file_s_folder_whatever_it_names()
    {
        var directory = Path.Combine(Path.GetTempPath(), "project");

        var dacpac = Read("""{ "baseline": "build/release/Shop.dacpac" }""").Expected();
        var ir = Read("""{ "baseline": "../shop-app/src/db/haruspex.ir.json" }""").Expected();

        Assert.Equal(Path.Combine(directory, "build", "release", "Shop.dacpac"), dacpac.Baseline);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory, "..", "shop-app", "src", "db", "haruspex.ir.json")), ir.Baseline);
        Assert.Null(Read("{}").Expected().Baseline);
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
              "check": { "unknowns": "fail" },
              "session": { "ansiWarnings": true, "concatNullYieldsNull": false, "numericRoundAbort": true },
              "naming": { "replace": [{ "pattern": "#$", "with": "Row" }], "case": "camel" },
              "schemas": {
                "sales": { "include": ["get_*", "list_*",], "exclude": ["*_internal"] },
                "Ledger.": { "include": ["GetEntryList"], "prefix": "Ledger", "naming": { "case": "preserve", "replace": [{ "pattern": "^Get", "with": "" }] } },
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

        Assert.Empty(result.Diagnostics);
        var config = result.Expected();
        Assert.Equal(CheckUnknowns.Fail, config.CheckUnknowns);
        Assert.Equal(Path.Combine(directory, "db"), config.Source);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory, "..", "generated")), config.Out);
        Assert.Equal(["sales", "Ledger.", "reference"], config.Schemas.Select(s => s.Name));
        Assert.Equal(["get_*", "list_*"], config.Schemas[0].Include!.Select(p => p.Pattern.Text));
        Assert.Equal(11, config.Schemas[0].Include![0].At.Line);
        Assert.Equal(["*_internal"], config.Schemas[0].Exclude.Select(p => p.Pattern.Text));
        Assert.Equal("", config.Schemas[0].Prefix);
        Assert.Equal("Ledger", config.Schemas[1].Prefix);
        Assert.Null(config.Schemas[2].Include);
        Assert.True(config.SelectsAnything);
        Assert.Equal(ObjectsPolicy.Strict, config.Objects);
        Assert.Equal(new SessionOptions(AnsiWarnings: true, ConcatNullYieldsNull: false, NumericRoundAbort: true), config.Session);
        Assert.Equal(NamingCase.Camel, config.Naming.Case);
        Assert.Equal("Row", Assert.Single(config.Naming.Replace).With);

        // A schema's naming merges over the top level's: its case replaces it, its replacements run after.
        var ledger = config.NamingFor("ledger.");
        Assert.Equal(NamingCase.Preserve, ledger.Case);
        Assert.Equal(["Row", ""], ledger.Replace.Select(r => r.With));
        Assert.Equal("Ledger", ledger.Prefix);
        Assert.Equal(config.Naming, config.NamingFor("hr"));
        var procedure = Assert.IsType<InferredResultsOverride>(Assert.Single(config.Procedures));
        Assert.Equal("OrderDetail", procedure.Name);
        Assert.Equal(["lines[]", "note", "status", "settings"], procedure.Paths.Select(p => p.Path));
        Assert.Equal("OrderLine", procedure.Paths[0].Name);
        Assert.True(procedure.Paths[1].NotNull);
        Assert.Equal("always written", procedure.Paths[1].Why);
        Assert.True(procedure.Paths[2].ExactValues);
        Assert.Equal(new ImportedType("SettingsSchema[]", "./settings"), procedure.Paths[3].Imported);
    }

    [Fact]
    public void The_packages_declared_builds_are_read_by_file_name_and_none_without_the_setting()
    {
        Assert.Equal(["sales.dacpac", "Shared.DACPAC"], Read("""{ "builds": ["sales.dacpac", "Shared.DACPAC"] }""").Expected().Builds);
        Assert.Empty(Read("{}").Expected().Builds);
    }

    [Fact]
    public void The_collation_is_read_and_a_value_that_names_none_is_an_error()
    {
        Assert.Equal("Latin1_General_100_CI_AS", Read("""{ "collation": "Latin1_General_100_CI_AS" }""").Expected().Collation);
        Assert.Null(Read("{}").Expected().Collation);

        var wrong = Read("""{ "collation": "Latin1 General" }""");
        Assert.Contains("no collation's name", Assert.Single(wrong.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Theory]
    // A misspelt name would otherwise be read as a single-byte collation's, and a char(n) typed as exactly n characters.
    [InlineData("Latin1_General_100_CI_AS_SC_UFT8")]
    [InlineData("Japanes_XJIS_140_CI_AS")]
    [InlineData("Nonsense")]
    public void A_collation_sql_server_does_not_have_is_an_error_at_the_setting(string name)
    {
        var wrong = Read($$"""{ "collation": "{{name}}" }""");

        var diagnostic = Assert.Single(wrong.Diagnostics);
        Assert.Equal(ConfigCodes.WrongKind, diagnostic.Code);
        Assert.Contains($"\"{name}\", which SQL Server has no collation of", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    // A collation of no code page holds only nvarchar, and SQL Server refuses it as a database's (Msg 453, observed:
    // A_unicode_only_collation_is_no_databases_collation).
    [InlineData("Indic_General_100_CI_AS")]
    [InlineData("assamese_100_ci_as")]
    public void A_unicode_only_collation_is_an_error_at_the_setting(string name)
    {
        var wrong = Read($$"""{ "collation": "{{name}}" }""");

        var diagnostic = Assert.Single(wrong.Diagnostics);
        Assert.Equal(ConfigCodes.WrongKind, diagnostic.Code);
        Assert.Contains($"\"{name}\", a collation of Unicode only, which no database can have", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_collation_name_is_read_ignoring_case_as_sql_server_reads_it() =>
        Assert.Equal("latin1_general_100_ci_as", Read("""{ "collation": "latin1_general_100_ci_as" }""").Expected().Collation);

    [Fact]
    public void An_empty_object_is_a_valid_config_with_every_default()
    {
        var config = Read("{}").Expected();

        Assert.Empty(config.Schemas);
        Assert.False(config.SelectsAnything);
        Assert.Equal(ObjectsPolicy.Strip, config.Objects);
        Assert.True(config.Session.IsNotKnown);
        Assert.Equal(NamingRules.Default, config.Naming);
        Assert.Null(config.Source);
        Assert.Null(config.Out);
    }

    [Theory]
    [InlineData("""{}""", new[] { Emitter.Zod })]
    [InlineData("""{ "emit": ["json-schema"] }""", new[] { Emitter.JsonSchema })]
    [InlineData("""{ "emit": ["json-schema", "zod"] }""", new[] { Emitter.JsonSchema, Emitter.Zod })]
    [InlineData("""{ "emit": ["zod", "mssql"] }""", new[] { Emitter.Zod, Emitter.Mssql })]
    [InlineData("""{ "emit": ["zod", "mssql", "mapping"] }""", new[] { Emitter.Zod, Emitter.Mssql, Emitter.Mapping })]
    public void What_is_emitted_is_the_zod_unless_the_file_names_its_emitters(string text, Emitter[] emitters) =>
        Assert.Equal(emitters, Read(text).Expected().Emitters);

    [Theory]
    [InlineData("""{ "emit": ["zod", "mssql"] }""", false)]
    [InlineData("""{ "emit": ["zod", "mssql"], "serverOnly": true }""", true)]
    [InlineData("""{ "serverOnly": false, "emit": ["zod", "mssql"] }""", false)]
    public void The_functions_import_server_only_when_the_file_asks(string text, bool serverOnly) =>
        Assert.Equal(serverOnly, Read(text).Expected().ServerOnly);

    [Fact]
    public void A_byte_order_mark_is_allowed()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{ \"objects\": \"strict\" }")).ToArray();

        var result = ConfigReader.Read(bytes, "haruspex.json", Path.GetTempPath());

        Assert.Equal(ObjectsPolicy.Strict, result.Expected().Objects);
    }

    [Fact]
    public void Every_problem_is_reported_not_only_the_first()
    {
        var result = Read("""{ "objects": "loose", "colour": 1, "schemas": { "sales": { "include": [] } } }""");

        Assert.Equal(3, result.Diagnostics.Count);
    }

    /// <summary>Text that is not JSON with comments: the reader's message, at the line and column it stopped.</summary>
    [Theory]
    [InlineData("{\n  \"include\": [\"a.b\"\n}", "haruspex.json:3:1: error HAR3002: '}' is invalid without a matching open.")]
    [InlineData("{} {}", "haruspex.json:1:4: error HAR3002: '{' is invalid after a single JSON value. Expected end of data.")]
    [InlineData("{\n  \"objects\": \"strip\",\n  \"objects\": \"strict\"\n}", "haruspex.json:3:3: error HAR3003: objects: The key \"objects\" is written twice in one object.")]
    [InlineData("{ \"schemas\": { \"sales\": { \"include\": [\"*\"], \"include\": [\"x\"] } } }", "haruspex.json:1:45: error HAR3003: schemas.sales.include: The key \"include\" is written twice in one object.")]
    public void Text_that_is_not_json_is_an_error_with_its_line(string text, string expected) =>
        Assert.Equal(expected, Single(text));

    [Fact]
    public void An_empty_file_is_an_error()
    {
        var problem = Single("");

        Assert.StartsWith("haruspex.json:1:1: error HAR3002: ", problem, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Errors { get; } = new()
    {
        // The file as a whole, and its settings' types.
        { "[]", "haruspex.json:1:1: error HAR3005: must be an object." },
        { """{ "colour": "red" }""", "haruspex.json:1:3: error HAR3004: colour: unknown setting \"colour\". The settings here are $schema, source, baseline, references, builds, collation, queries, out, split, schemas, objects, comments, emit, serverOnly, naming, procedures, columns, session, check, acknowledge." },
        { """{ "include": ["sales.*"] }""", "haruspex.json:1:3: error HAR3004: include: unknown setting \"include\". The settings here are $schema, source, baseline, references, builds, collation, queries, out, split, schemas, objects, comments, emit, serverOnly, naming, procedures, columns, session, check, acknowledge." },
        { """{ "$schema": 1 }""", "haruspex.json:1:14: error HAR3005: $schema: must be a string." },
        { """{ "source": "" }""", "haruspex.json:1:13: error HAR3005: source: must not be empty." },
        { """{ "baseline": "" }""", "haruspex.json:1:15: error HAR3005: baseline: must not be empty." },
        { """{ "out": 3 }""", "haruspex.json:1:10: error HAR3005: out: must be a string." },
        { """{ "queries": 3 }""", "haruspex.json:1:14: error HAR3005: queries: must name the application's query files: a folder, a file or a pattern such as \"queries/**/*.sql\", or an array of them." },
        { """{ "queries": [] }""", "haruspex.json:1:14: error HAR3005: queries: must name the application's query files: a folder, a file or a pattern such as \"queries/**/*.sql\", or an array of them." },
        { """{ "queries": ["sql", "../shared/sql"] }""", "haruspex.json:1:22: error HAR3022: queries[1]: \"../shared/sql\" reaches outside the folder of the config file; a query file belongs to the module whose folder holds it, so name only files below this one." },
        { """{ "queries": "/srv/sql" }""", "haruspex.json:1:14: error HAR3022: queries: \"/srv/sql\" reaches outside the folder of the config file; a query file belongs to the module whose folder holds it, so name only files below this one." },
        { """{ "references": "lib" }""", "haruspex.json:1:17: error HAR3005: references: must be an array of folders to look for referenced DACPACs in, such as \"../shared/bin/Release\"." },
        { """{ "references": ["no/such/folder"] }""", "haruspex.json:1:18: error HAR3021: references[0]: \"no/such/folder\" is not a folder, so no referenced DACPAC can be found in it." },
        { """{ "builds": "sales.dacpac" }""", "haruspex.json:1:13: error HAR3005: builds: must be an array of DACPACs' file names, such as \"sales.dacpac\", each built from a project though nothing in it says so." },
        { """{ "builds": ["bin/sales.dacpac"] }""", "haruspex.json:1:14: error HAR3005: builds[0]: is \"bin/sales.dacpac\", which is no DACPAC's file name: name the package as \"sales.dacpac\", without its folder." },
        { """{ "builds": ["C:sales.dacpac"] }""", "haruspex.json:1:14: error HAR3005: builds[0]: is \"C:sales.dacpac\", which is no DACPAC's file name: name the package as \"sales.dacpac\", without its folder." },
        { """{ "builds": ["sales.sqlproj"] }""", "haruspex.json:1:14: error HAR3005: builds[0]: is \"sales.sqlproj\", which is no DACPAC's file name: name the package as \"sales.dacpac\", without its folder." },
        { """{ "objects": "loose" }""", "haruspex.json:1:14: error HAR3005: objects: must be \"strip\" or \"strict\", not \"loose\"." },

        // What generate writes beside the IR.
        { """{ "emit": "zod" }""", "haruspex.json:1:11: error HAR3005: emit: must be an array of what to write beside the IR: any of \"zod\", \"json-schema\", \"mssql\" and \"mapping\"." },
        { """{ "emit": [] }""", "haruspex.json:1:11: error HAR3005: emit: must name at least one emitter; with none, generate would write the IR alone. Use [\"zod\"], the default, or leave it out." },
        { """{ "emit": ["typescript"] }""", "haruspex.json:1:12: error HAR3005: emit[0]: must be \"zod\" or \"json-schema\" or \"mssql\" or \"mapping\", not \"typescript\"." },
        { """{ "emit": ["mssql"] }""", "haruspex.json:1:11: error HAR3012: emit: names \"mssql\" without \"zod\": the functions import the Zod module's schemas, so name both." },
        { """{ "emit": ["json-schema", "mapping"] }""", "haruspex.json:1:11: error HAR3012: emit: names \"mapping\" without \"zod\": the mapping helper works on the types the Zod module declares, so name both." },
        { """{ "serverOnly": true }""", "haruspex.json:1:17: error HAR3012: serverOnly: is about the functions' file, which \"emit\" writes when it names \"mssql\"; it names none here." },
        { """{ "serverOnly": "yes", "emit": ["zod", "mssql"] }""", "haruspex.json:1:17: error HAR3005: serverOnly: must be true or false." },
        { """{ "emit": ["zod", "zod"] }""", "haruspex.json:1:19: error HAR3005: emit[1]: names \"zod\" again; each emitter is listed once." },

        // What check does beyond comparing.
        { """{ "check": { "unknowns": "warn" } }""", "haruspex.json:1:26: error HAR3005: check.unknowns: must be \"report\" or \"fail\", not \"warn\"." },
        { """{ "check": { "changes": "ignore" } }""", "haruspex.json:1:14: error HAR3004: check.changes: unknown setting \"changes\". The settings here are unknowns." },

        // The callers' session: only the options a rule reads, each true or false.
        { """{ "session": [] }""", "haruspex.json:1:14: error HAR3005: session: must be an object." },
        { """{ "session": { "ansiWarnings": "on" } }""", "haruspex.json:1:32: error HAR3005: session.ansiWarnings: must be true or false; leave the option out when it is not known." },
        { """{ "session": { "arithAbort": null } }""", "haruspex.json:1:30: error HAR3005: session.arithAbort: must be true or false; leave the option out when it is not known." },
        { """{ "session": { "language": "us_english" } }""", "haruspex.json:1:16: error HAR3004: session.language: unknown setting \"language\". The settings here are ansiWarnings, arithAbort, concatNullYieldsNull, numericRoundAbort, unmask." },

        // The split into modules.
        { """{ "split": "table" }""", "haruspex.json:1:12: error HAR3005: split: must be \"schema\", for a module per schema, or an object naming each module and the schemas it holds, such as { \"sales\": [\"sales\", \"shop\"] }." },
        { """{ "split": { "sales": "sales" } }""", "haruspex.json:1:23: error HAR3005: split.sales: must be an array of the schemas the module holds, such as [\"sales\", \"shop\"]; [] for a module of query files alone." },
        { """{ "split": { "sales": [""] } }""", "haruspex.json:1:24: error HAR3005: split.sales[0]: must not be empty." },
        { """{ "split": { "sales team": ["sales"] } }""", "haruspex.json:1:14: error HAR3024: split[\"sales team\"]: 'sales team' can't name a module: a module's name starts with a letter and has only letters, digits, '_' and '-', since it names the module's files." },
        { """{ "split": { "sales.v2": ["sales"] } }""", "haruspex.json:1:14: error HAR3024: split[\"sales.v2\"]: 'sales.v2' can't name a module: a module's name starts with a letter and has only letters, digits, '_' and '-', since it names the module's files." },
        { """{ "split": { "Shared": ["sales"] } }""", "haruspex.json:1:14: error HAR3024: split.Shared: 'Shared' can't name a module: it is the name of the files that hold what modules share. Name the module otherwise." },
        { "{ \"split\": {\n\"sales\": [\"sales\"],\n\"Sales\": [\"shop\"] } }", "haruspex.json:3:1: error HAR3024: split.Sales: 'Sales' can't name a module: the module sales, at line 2, has the name in another case, and a file system that ignores case would hold one file for both." },
        { "{ \"split\": {\n\"sales\": [\"sales\"],\n\"shop\": [\"Sales\"] } }", "haruspex.json:3:10: error HAR3025: split.shop[0]: the schema Sales is in the module sales already, at line 2; a schema's procedures are in one module. Schema names are compared ignoring case, as SQL Server compares them." },

        // Schemas and selection.
        { """{ "schemas": [] }""", "haruspex.json:1:14: error HAR3005: schemas: must be an object." },
        { """{ "schemas": { "": {} } }""", "haruspex.json:1:16: error HAR3005: schemas[\"\"]: a schema's name must not be empty." },
        { """{ "schemas": { "sales": [] } }""", "haruspex.json:1:25: error HAR3005: schemas.sales: must be an object." },
        { """{ "schemas": { "sales": { "output": "x" } } }""", "haruspex.json:1:27: error HAR3004: schemas.sales.output: unknown setting \"output\". The settings here are include, exclude, prefix, naming." },
        { "{ \"schemas\": {\n\"sales\": {},\n\"SALES\": {} } }", "haruspex.json:3:1: error HAR3008: schemas.SALES: the schema \"sales\" already has a block, at line 2; schema names are compared ignoring case, as SQL Server compares them. Merge the two." },
        { """{ "schemas": { "sales": { "include": "*" } } }""", "haruspex.json:1:38: error HAR3005: schemas.sales.include: must be an array of patterns matching the schema's procedures by name, such as \"get_*\"." },
        { """{ "schemas": { "sales": { "include": [] } } }""", "haruspex.json:1:38: error HAR3005: schemas.sales.include: must list at least one pattern; with none, nothing is selected. Use \"*\" to select every procedure of the schema." },
        { """{ "schemas": { "sales": { "include": [1] } } }""", "haruspex.json:1:39: error HAR3005: schemas.sales.include[0]: must be a string." },
        { """{ "schemas": { "sales": { "include": [""] } } }""", "haruspex.json:1:39: error HAR3006: schemas.sales.include[0]: '' is empty: a pattern names procedures of its schema, such as get_order, list_* or *." },
        { """{ "schemas": { "sales": { "include": ["*"], "exclude": ["[x"] } } }""", "haruspex.json:1:57: error HAR3006: schemas.sales.exclude[0]: '[x' has a '[' without its closing ']'." },
        { """{ "schemas": { "audit": { "exclude": ["nothing_here"] } } }""", "haruspex.json:1:27: error HAR3009: schemas.audit.exclude: does nothing without an \"include\" in the same block, since nothing in the schema is selected. Add the include, or remove the exclude." },
        { """{ "schemas": { "sales": { "prefix": 1 } } }""", "haruspex.json:1:37: error HAR3005: schemas.sales.prefix: must be a string." },
        { """{ "schemas": { "sales": { "naming": { "prefix": "S" } } } }""", "haruspex.json:1:39: error HAR3004: schemas.sales.naming.prefix: unknown setting \"prefix\". The settings here are replace, case." },

        // Naming.
        { """{ "naming": [] }""", "haruspex.json:1:13: error HAR3005: naming: must be an object." },
        { """{ "naming": { "style": "pascal" } }""", "haruspex.json:1:15: error HAR3004: naming.style: unknown setting \"style\". The settings here are replace, case." },
        { """{ "naming": { "stripSchema": ["dbo"] } }""", "haruspex.json:1:15: error HAR3004: naming.stripSchema: unknown setting \"stripSchema\". The settings here are replace, case." },
        { """{ "naming": { "case": "snake" } }""", "haruspex.json:1:23: error HAR3005: naming.case: must be \"pascal\" or \"camel\" or \"preserve\", not \"snake\"." },
        { """{ "naming": { "replace": {} } }""", "haruspex.json:1:26: error HAR3005: naming.replace: must be an array of { \"pattern\": \"…\", \"with\": \"…\" }." },
        { """{ "naming": { "replace": [1] } }""", "haruspex.json:1:27: error HAR3005: naming.replace[0]: must be an object." },
        { """{ "naming": { "replace": [{ "with": "x" }] } }""", "haruspex.json:1:27: error HAR3012: naming.replace[0]: needs \"pattern\", the regular expression to replace." },
        { """{ "naming": { "replace": [{ "pattern": "x" }] } }""", "haruspex.json:1:27: error HAR3012: naming.replace[0]: needs \"with\", the replacement (\"\" to remove the match)." },
        { """{ "naming": { "replace": [{ "pattern": "x", "with": "", "flags": "i" }] } }""", "haruspex.json:1:57: error HAR3004: naming.replace[0].flags: unknown setting \"flags\". The settings here are pattern, with." },

        // Procedures.
        { """{ "procedures": [] }""", "haruspex.json:1:17: error HAR3005: procedures: must be an object." },
        { """{ "procedures": { "get_order": {} } }""", "haruspex.json:1:19: error HAR3006: procedures.get_order: 'get_order' is not a two-part name such as sales.get_order, sales.* or *.list_*." },
        { """{ "procedures": { "sales.*": {} } }""", "haruspex.json:1:19: error HAR3007: procedures[\"sales.*\"]: names one procedure, so it cannot use * or ?; write a part that holds them in brackets, as [a*b]." },
        { """{ "procedures": { "sales.p": 1 } }""", "haruspex.json:1:30: error HAR3005: procedures[\"sales.p\"]: must be an object." },
        { """{ "procedures": { "sales.p": { "rename": "X" } } }""", "haruspex.json:1:32: error HAR3004: procedures[\"sales.p\"].rename: unknown setting \"rename\". The settings here are name, paths, results, sets, why." },
        { """{ "procedures": { "sales.p": { "name": "Order Detail" } } }""", "haruspex.json:1:40: error HAR3019: procedures[\"sales.p\"].name: 'Order Detail' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word." },
        { """{ "procedures": { "sales.p": { "name": "delete" } } }""", "haruspex.json:1:40: error HAR3019: procedures[\"sales.p\"].name: 'delete' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word." },
        { """{ "procedures": { "sales.p": { "sets": [] } } }""", "haruspex.json:1:40: error HAR3023: procedures[\"sales.p\"].sets: must be an array of names, one for each result set in order, such as [\"order\", \"lines\"]." },
        { """{ "procedures": { "sales.p": { "sets": "order" } } }""", "haruspex.json:1:40: error HAR3023: procedures[\"sales.p\"].sets: must be an array of names, one for each result set in order, such as [\"order\", \"lines\"]." },
        { """{ "procedures": { "sales.p": { "sets": ["Order"] } } }""", "haruspex.json:1:41: error HAR3023: procedures[\"sales.p\"].sets[0]: 'Order' can't name a set: a set's name is a key in camel case, a lower-case ASCII letter then letters and digits, and not a reserved word." },
        { """{ "procedures": { "sales.p": { "sets": ["order_lines"] } } }""", "haruspex.json:1:41: error HAR3023: procedures[\"sales.p\"].sets[0]: 'order_lines' can't name a set: a set's name is a key in camel case, a lower-case ASCII letter then letters and digits, and not a reserved word." },
        { """{ "procedures": { "sales.p": { "sets": ["delete"] } } }""", "haruspex.json:1:41: error HAR3023: procedures[\"sales.p\"].sets[0]: 'delete' can't name a set: a set's name is a key in camel case, a lower-case ASCII letter then letters and digits, and not a reserved word." },
        { """{ "procedures": { "sales.p": { "sets": ["rows", "output"] } } }""", "haruspex.json:1:49: error HAR3023: procedures[\"sales.p\"].sets[1]: 'output' can't name a set: the function already returns its OUTPUT parameters under it." },
        { """{ "procedures": { "sales.p": { "sets": ["returnValue"] } } }""", "haruspex.json:1:41: error HAR3023: procedures[\"sales.p\"].sets[0]: 'returnValue' can't name a set: the function already returns its return code under it." },
        { """{ "procedures": { "sales.p": { "sets": ["rows", "rows"] } } }""", "haruspex.json:1:49: error HAR3023: procedures[\"sales.p\"].sets[1]: 'rows' names two sets; give each its own name." },
        { """{ "procedures": { "sales.p": { "sets": [1] } } }""", "haruspex.json:1:41: error HAR3005: procedures[\"sales.p\"].sets[0]: must be a string." },
        { """{ "procedures": { "sales.p": { "paths": [] } } }""", "haruspex.json:1:41: error HAR3005: procedures[\"sales.p\"].paths: must be an object." },

        // Path overrides.
        { """{ "procedures": { "sales.p": { "paths": { "a..b": { "name": "X" } } } } }""", "haruspex.json:1:43: error HAR3017: procedures[\"sales.p\"].paths[\"a..b\"]: 'a..b' is not a path: at 1, a '.' must be followed by a key; write a key that is not an identifier as [\"key\"]." },
        { """{ "procedures": { "sales.p": { "paths": { "a": 1 } } } }""", "haruspex.json:1:48: error HAR3005: procedures[\"sales.p\"].paths.a: must be an object." },
        { """{ "procedures": { "sales.p": { "paths": { "a": {} } } } }""", "haruspex.json:1:48: error HAR3020: procedures[\"sales.p\"].paths.a: says nothing to override: give \"name\", \"nullable\", \"type\" or \"values\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "optional": true } } } } }""", "haruspex.json:1:50: error HAR3004: procedures[\"sales.p\"].paths.a.optional: unknown setting \"optional\". The settings here are name, nullable, why, type, import, values." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": true, "why": "x" } } } } }""", "haruspex.json:1:62: error HAR3018: procedures[\"sales.p\"].paths.a.nullable: can only be false. A value is nullable unless the database proves otherwise, so there is nothing to declare." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": "no", "why": "x" } } } } }""", "haruspex.json:1:62: error HAR3005: procedures[\"sales.p\"].paths.a.nullable: must be false." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": false } } } } }""", "haruspex.json:1:48: error HAR3011: procedures[\"sales.p\"].paths.a: \"nullable\": false needs \"why\": the database doesn't prove it, so say what does." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "nullable": false, "why": "" } } } } }""", "haruspex.json:1:76: error HAR3005: procedures[\"sales.p\"].paths.a.why: must not be empty." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "values": "exact" } } } } }""", "haruspex.json:1:48: error HAR3011: procedures[\"sales.p\"].paths.a: \"values\": \"exact\" needs \"why\": the database compares the values under its collation, so say why they are exact." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "values": "enum", "why": "x" } } } } }""", "haruspex.json:1:60: error HAR3005: procedures[\"sales.p\"].paths.a.values: must be \"exact\", not \"enum\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "type": "Tag" } } } } }""", "haruspex.json:1:48: error HAR3012: procedures[\"sales.p\"].paths.a: \"type\" needs \"import\": the module that exports the schema." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "type": "Tag Schema", "import": "x" } } } } }""", "haruspex.json:1:58: error HAR3019: procedures[\"sales.p\"].paths.a.type: must be the name of an exported schema, with [] for each level of array, such as \"TagSchema[]\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "type": "Tag", "import": "" } } } } }""", "haruspex.json:1:75: error HAR3005: procedures[\"sales.p\"].paths.a.import: must not be empty." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "import": "x", "name": "A" } } } } }""", "haruspex.json:1:48: error HAR3012: procedures[\"sales.p\"].paths.a: \"import\" needs \"type\": the schema to import from it." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "name": "A", "why": "x" } } } } }""", "haruspex.json:1:48: error HAR3013: procedures[\"sales.p\"].paths.a: \"why\" explains a claim, but there is none here: give \"nullable\", \"type\" or \"values\"." },
        { """{ "procedures": { "sales.p": { "paths": { "a": { "name": "1st" } } } } }""", "haruspex.json:1:58: error HAR3019: procedures[\"sales.p\"].paths.a.name: '1st' is not a valid name: an ASCII letter, _ or $, then letters, digits, _ or $, and not a reserved word." },
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

        Assert.IsType<Failed<HaruspexConfig>>(result);
        Assert.StartsWith($"{DiagnosticFormatter.PathAsShown(missing)}: error HAR3001: cannot read the config file: ", Assert.Single(result.Diagnostics).ToString(), StringComparison.Ordinal);
    }
}

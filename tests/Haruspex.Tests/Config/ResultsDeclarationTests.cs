using System.Text;
using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Config;

/// <summary>
/// Declared results (<c>procedures["s.p"].results</c>): read from the config file as a JSON Schema, inline or in a
/// file, into the IR; applied to the procedure itself when its results cannot be inferred; checked against inference when
/// they can; and contributed to every caller of it. Messages are checked word for word where they tell a user what to do.
/// </summary>
public class ResultsDeclarationTests
{
    private const string Tables = """
        CREATE SCHEMA rpt;
        GO
        CREATE TABLE rpt.item (item_id int NOT NULL PRIMARY KEY, label nvarchar(20) NULL);
        GO
        """;

    private const string Search = """
        CREATE PROCEDURE rpt.search @where nvarchar(200) AS
        DECLARE @sql nvarchar(max) = N'SELECT i.item_id, i.label FROM rpt.item AS i WHERE ' + @where;
        EXEC sp_executesql @sql;
        GO
        CREATE PROCEDURE rpt.search_page @where nvarchar(200) AS
        SELECT COUNT(*) AS total FROM rpt.item AS i;
        EXEC rpt.search @where;
        GO
        CREATE PROCEDURE rpt.list_items AS
        SELECT i.item_id, i.label FROM rpt.item AS i;
        GO
        CREATE PROCEDURE rpt.staged AS
        CREATE TABLE #s (item_id int NOT NULL);
        INSERT INTO #s EXEC (N'SELECT 1');
        SELECT s.item_id FROM #s AS s;
        GO
        CREATE PROCEDURE rpt.staged_through AS
        CREATE TABLE #s (item_id int NOT NULL);
        INSERT INTO #s (item_id) EXEC rpt.search N'1 = 1';
        SELECT s.item_id FROM #s AS s;
        GO
        CREATE PROCEDURE rpt.logged AS
        EXEC audit.write_log N'listed';
        SELECT i.item_id FROM rpt.item AS i;
        GO
        """;

    private const string SearchRows = """
        "results": { "rowset": { "type": "object", "properties": { "item_id": { "type": "integer", "format": "int32" }, "label": { "type": ["string", "null"] } } } },
        "why": "the filter is SQL the caller builds"
        """;

    private static ConfigReadResult Read(string config, TempDirectory? dir = null) =>
        ConfigReader.Read(Encoding.UTF8.GetBytes(config), "haruspex.json", dir?.Path ?? Path.GetTempPath());

    private static ProcedureOverride Only(ConfigReadResult read)
    {
        Assert.Empty(read.Problems);
        return Assert.Single(read.Config!.Procedures);
    }

    private static string[] Problems(ConfigReadResult read) => [.. read.Problems.Select(p => p.ToString())];

    private sealed record Run(ConfigApplied Applied)
    {
        public IrDocument Document => Applied.Document ?? throw new InvalidOperationException(string.Join("\n", Applied.Problems));

        public string[] Problems => [.. Applied.Problems.Select(p => p.ToString())];
    }

    private static Run Apply(string config, string include)
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        dir.File("procedures.sql", Search);
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);

        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(config), "haruspex.json", dir.Path);
        Assert.Empty(read.Problems);
        var (selected, _) = ProcedureSelection.Select(project.Procedures, [ObjectPattern.Parse(include)], read.Config!);
        return new Run(project.Infer(selected, read.Config!));
    }

    // ---- reading -----------------------------------------------------------------------------------------

    [Fact]
    public void Results_none_declares_no_result_sets()
    {
        var declared = Only(Read("""{ "procedures": { "audit.write_log": { "results": "none", "why": "it only writes the log" } } }""")).Results!;

        Assert.Empty(declared.Results);
        Assert.Equal("it only writes the log", declared.Why);
    }

    [Fact]
    public void A_rowset_is_declared_by_the_schema_of_one_row()
    {
        var declared = Only(Read($$"""{ "procedures": { "rpt.search": { {{SearchRows}} } } }""")).Results!;

        var result = Assert.Single(declared.Results);
        Assert.Equal(IrEncoding.Rowset, result.Encoding);
        Assert.False(result.MayBeAbsent);
        var row = declared.Types[Assert.IsType<IrRef>(Assert.IsType<IrArray>(result.Type).Items).Name];
        Assert.Equal(["item_id", "label"], row.Fields.Select(f => f.Name));
        Assert.All(row.Fields, f => Assert.Equal(IrPresence.Always, f.Presence));
        Assert.False(row.Fields[0].Nullable);
        Assert.Equal(new IrScalar(IrScalarKind.Number, IrFormat.Int32, null), row.Fields[0].Type);
        Assert.Equal([new IrNullReason(IrNullRule.Declared, "rpt.search")], row.Fields[1].NullableBecause);
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(row.Fields[1].Type).Kind);
    }

    [Fact]
    public void Json_results_take_objects_arrays_optional_keys_formats_records_and_refs()
    {
        var declared = Only(Read("""
            { "procedures": { "rpt.export": {
              "results": [
                { "json": {
                    "type": "object",
                    "$defs": { "tag": { "type": "object", "properties": { "name": { "type": "string" } }, "required": ["name"] } },
                    "properties": {
                      "on": { "type": "string", "format": "date" },
                      "at": { "type": "string", "format": "date-time" },
                      "local": { "type": "string", "format": "date-time-local" },
                      "time": { "type": "string", "format": "time" },
                      "id": { "type": "string", "format": "uuid" },
                      "count": { "type": "integer" },
                      "ratio": { "type": "number" },
                      "done": { "type": "boolean" },
                      "note": { "type": ["string", "null"] },
                      "tags": { "type": "array", "items": { "$ref": "#/$defs/tag" }, "minItems": 1 },
                      "extra": { "type": "object", "additionalProperties": { "type": "integer" } }
                    },
                    "required": ["on", "at", "local", "time", "id", "count", "ratio", "done", "note", "tags"]
                  },
                  "mayBeAbsent": true },
                { "rowset": { "type": "object", "properties": { "n": { "type": "integer" } } } }
              ],
              "why": "built from a template table"
            } } }
            """)).Results!;

        Assert.Equal(2, declared.Results.Count);
        var json = declared.Results[0];
        Assert.Equal(IrEncoding.Json, json.Encoding);
        Assert.Equal([new IrNullReason(IrNullRule.Declared, "rpt.export")], json.AbsentBecause);
        var fields = declared.Types[Assert.IsType<IrRef>(json.Type).Name].Fields.ToDictionary(f => f.Name);
        Assert.Equal(IrScalarKind.Date, ((IrScalar)fields["on"].Type).Kind);
        Assert.Equal((IrScalarKind.Datetime, IrFormat.Offset), (((IrScalar)fields["at"].Type).Kind, ((IrScalar)fields["at"].Type).Format));
        Assert.Equal((IrScalarKind.Datetime, IrFormat.Local), (((IrScalar)fields["local"].Type).Kind, ((IrScalar)fields["local"].Type).Format));
        Assert.Equal(IrScalarKind.Time, ((IrScalar)fields["time"].Type).Kind);
        Assert.Equal(IrScalarKind.Uuid, ((IrScalar)fields["id"].Type).Kind);
        Assert.Equal((IrScalarKind.Number, null), (((IrScalar)fields["count"].Type).Kind, ((IrScalar)fields["count"].Type).Format));
        Assert.Equal(IrScalarKind.Boolean, ((IrScalar)fields["done"].Type).Kind);
        Assert.Equal(IrPresence.Always, fields["note"].Presence);
        Assert.True(fields["note"].Nullable);
        var tags = Assert.IsType<IrArray>(fields["tags"].Type);
        Assert.Equal(1, tags.MinItems);
        Assert.Equal(["name"], declared.Types[Assert.IsType<IrRef>(tags.Items).Name].Fields.Select(f => f.Name));

        // Not required, never null: may be left out, never null (FOR JSON's omittedWhenNull).
        Assert.Equal(IrPresence.OmittedWhenNull, fields["extra"].Presence);
        Assert.True(fields["extra"].Nullable);
        Assert.IsType<IrRecord>(fields["extra"].Type);
        Assert.Equal(IrEncoding.Rowset, declared.Results[1].Encoding);
    }

    [Fact]
    public void A_schema_can_be_a_file_beside_the_config()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/row.schema.json", """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "title": "A search row",
              "type": "object",
              "properties": { "item_id": { "type": "integer" } }
            }
            """);

        var declared = Only(Read("""{ "procedures": { "rpt.search": { "results": { "rowset": "schemas/row.schema.json" }, "why": "dynamic" } } }""", dir)).Results!;

        Assert.Equal(["item_id"], declared.Types.Values.Single().Fields.Select(f => f.Name));
    }

    [Fact]
    public void A_problem_in_a_schema_file_is_reported_in_that_file()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/row.schema.json", "{\n  \"type\": \"object\",\n  \"properties\": { \"x\": { \"type\": [\"string\", \"integer\"] } }\n}\n");

        var read = Read("""{ "procedures": { "rpt.search": { "results": { "rowset": "schemas/row.schema.json" }, "why": "dynamic" } } }""", dir);

        var problem = Assert.Single(read.Problems);
        Assert.Equal(Path.Combine(dir.Path, "schemas/row.schema.json"), problem.File);
        Assert.Equal(3, problem.At!.Line);
        Assert.Contains("one type", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_schema_file_is_an_error()
    {
        using var dir = new TempDirectory();

        var read = Read("""{ "procedures": { "rpt.search": { "results": { "rowset": "schemas/none.json" }, "why": "dynamic" } } }""", dir);

        Assert.Contains("cannot read the schema", Assert.Single(read.Problems).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "results": "none" }""", "\"results\" needs \"why\"")]
    [InlineData("""{ "why": "x" }""", "\"why\" explains declared results")]
    [InlineData("""{ "results": "none", "why": "x", "paths": { "id": { "name": "X" } } }""", "\"paths\" can't be used with \"results\"")]
    [InlineData("""{ "results": "some", "why": "x" }""", "must be \"none\"")]
    [InlineData("""{ "results": [], "why": "x" }""", "use \"none\"")]
    [InlineData("""{ "results": { "rowset": {}, "json": {} }, "why": "x" }""", "either \"rowset\" or \"json\"")]
    [InlineData("""{ "results": { "rowset": { "type": "object", "properties": {} }, "mayBeAbsent": true }, "why": "x" }""", "\"mayBeAbsent\" is for a JSON result")]
    [InlineData("""{ "results": { "rowset": { "type": "object", "properties": { "a": { "type": "object", "properties": {} } } } }, "why": "x" }""", "a rowset's column holds a scalar")]
    [InlineData("""{ "results": { "rowset": { "type": "object", "properties": { "a": { "type": "string" } }, "required": [] } }, "why": "x" }""", "every column of a rowset is always there")]
    [InlineData("""{ "results": { "rowset": { "type": "array" } }, "why": "x" }""", "a rowset is declared by the schema of one row")]
    [InlineData("""{ "results": { "json": { "type": ["object", "null"], "properties": {} } }, "why": "x" }""", "use \"mayBeAbsent\"")]
    [InlineData("""{ "results": { "json": { "properties": {} } }, "why": "x" }""", "needs a \"type\"")]
    [InlineData("""{ "results": { "json": { "type": "object", "properties": { "a": { "type": ["string", "null"] } } } }, "why": "x" }""", "may be both left out and null")]
    [InlineData("""{ "results": { "json": { "type": "object" } }, "why": "x" }""", "needs \"properties\"")]
    [InlineData("""{ "results": { "json": { "type": "array" } }, "why": "x" }""", "needs \"items\"")]
    [InlineData("""{ "results": { "json": { "$ref": "#/$defs/a", "$defs": { "a": { "type": "array", "items": { "$ref": "#/$defs/a" } } } } }, "why": "x" }""", "inside itself: a recursive schema")]
    [InlineData("""{ "results": { "json": { "$ref": "other.json#/a" } }, "why": "x" }""", "only to \"#/$defs/")]
    public void A_wrong_declaration_is_an_error(string block, string expected)
    {
        var read = Read($$"""{ "procedures": { "rpt.search": {{block}} } }""");

        Assert.Null(read.Config);
        Assert.Contains(read.Problems, p => p.Severity == ConfigSeverity.Error && p.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_keyword_not_supported_is_a_warning_and_is_not_checked()
    {
        var read = Read("""{ "procedures": { "rpt.search": { "results": { "rowset": { "type": "object", "properties": { "a": { "type": "string", "maxLength": 5 } } } }, "why": "x" } } }""");

        Assert.NotNull(read.Config);
        Assert.Equal(
            ["haruspex.json:1:119: warning: procedures[\"rpt.search\"].results.rowset.properties.a.maxLength: is not supported yet, so the generated schema does not check it."],
            Problems(read));
    }

    // ---- applying ----------------------------------------------------------------------------------------

    [Fact]
    public void A_declaration_gives_a_procedure_whose_results_are_unknowable_its_results()
    {
        var run = Apply($$"""{ "procedures": { "rpt.search": { {{SearchRows}} } } }""", "rpt.search");

        var doc = run.Document;
        Assert.Empty(run.Problems);
        var result = Assert.Single(doc.Objects.Single().Results!);
        Assert.Equal(["item_id", "label"], Fields(doc, result.Type).Select(f => f.Name));
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "rpt.search");
        Assert.Equal([new IrOverride("rpt.search", "results", IrOverrideSetting.Results, "the filter is SQL the caller builds")], doc.Overrides);

        var zod = ZodEmitter.Emit(doc);
        Assert.Contains("export const SearchRowSchema = z.object({", zod, StringComparison.Ordinal);
        Assert.Contains("item_id: z.int32(),", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_flows_into_every_caller()
    {
        var run = Apply($$"""{ "schemas": { "rpt": { "include": ["search_page"] } }, "procedures": { "rpt.search": { {{SearchRows}} } } }""", "rpt.search_page");

        var doc = run.Document;
        Assert.Empty(run.Problems);
        var results = doc.Objects.Single().Results!;
        Assert.Equal(2, results.Count);
        Assert.Equal(["item_id", "label"], Fields(doc, results[1].Type).Select(f => f.Name));
        Assert.Equal([new IrOverride("rpt.search_page", "results", IrOverrideSetting.Results, "the filter is SQL the caller builds") { Via = "rpt.search" }], doc.Overrides);
    }

    [Fact]
    public void A_procedure_outside_the_project_can_be_declared()
    {
        var run = Apply("""{ "procedures": { "audit.write_log": { "results": "none", "why": "it only writes the log" } } }""", "rpt.logged");

        Assert.Empty(run.Problems);
        Assert.Single(run.Document.Objects.Single().Results!);
    }

    [Fact]
    public void A_declaration_the_inference_agrees_with_is_a_warning()
    {
        var run = Apply("""
            { "procedures": { "rpt.list_items": { "results": { "rowset": { "type": "object", "properties": { "item_id": { "type": "integer" }, "label": { "type": ["string", "null"] } } } }, "why": "x" } } }
            """, "rpt.list_items");

        Assert.Equal(
            ["haruspex.json:1:19: warning: procedures[\"rpt.list_items\"]: the declared results agree with the inference, which already infers the results of rpt.list_items. Remove the declaration."],
            run.Problems);
        Assert.Empty(run.Document.Overrides);
    }

    [Fact]
    public void A_declaration_the_inference_disagrees_with_is_an_error()
    {
        var run = Apply("""
            { "procedures": { "rpt.list_items": { "results": { "rowset": { "type": "object", "properties": { "item_id": { "type": "integer" }, "label": { "type": "string" } } } }, "why": "x" } } }
            """, "rpt.list_items");

        Assert.Null(run.Applied.Document);
        Assert.Equal(
            ["haruspex.json:1:19: error: procedures[\"rpt.list_items\"]: haruspex infers the results of rpt.list_items, and the declaration disagrees at results[0][].label: the inference says it may be null, the declaration that it is never null. A declaration is for results that can't be inferred; to correct inferred results, override a path under \"paths\"."],
            run.Problems);
    }

    [Fact]
    public void A_declaration_of_another_type_names_both_types_as_the_ir_does()
    {
        // The kind and format are written by their names in the IR's JSON (int32), never as the enum's members (Int32).
        var run = Apply("""
            { "procedures": { "rpt.list_items": { "results": { "rowset": { "type": "object", "properties": { "item_id": { "type": "string" }, "label": { "type": ["string", "null"] } } } }, "why": "x" } } }
            """, "rpt.list_items");

        Assert.Contains(run.Problems, p => p.Contains("results[0][].item_id: the inference says a number (int32), the declaration a string.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_declaration_of_a_procedure_not_selected_is_still_checked_against_the_inference()
    {
        var run = Apply("""
            { "procedures": { "rpt.list_items": { "results": "none", "why": "x" } } }
            """, "rpt.logged");

        Assert.Contains(run.Problems, p => p.Contains("error: procedures[\"rpt.list_items\"]: haruspex infers the results of rpt.list_items, and the declaration disagrees at results: the inference says 1 result set, the declaration 0.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_declaration_nothing_uses_is_a_warning()
    {
        var run = Apply("""
            { "procedures": { "audit.write_log": { "results": "none", "why": "it only writes the log" } } }
            """, "rpt.list_items");

        Assert.Equal(
            ["haruspex.json:1:19: warning: procedures[\"audit.write_log\"]: the declared results are not used: audit.write_log is not selected, and no selected procedure calls it."],
            run.Problems);
    }

    [Theory]
    [InlineData("rpt.staged", "the call at procedures.sql:14:1, which is not followed: EXEC (N'SELECT 1') runs dynamic SQL")]
    // Under INSERT … EXEC, rpt.search's own dynamic SQL is the call not followed: its result sets go into #s too.
    [InlineData("rpt.staged_through", "the call at procedures.sql:3:1, which is not followed: EXEC sp_executesql @sql runs dynamic SQL")]
    public void An_unknowable_exec_under_insert_exec_is_a_warning_naming_the_statement(string procedure, string statement)
    {
        var run = Apply("{}", procedure);

        Assert.NotNull(run.Applied.Document);
        var warning = Assert.Single(run.Problems);
        Assert.StartsWith($"haruspex.json: warning: {procedure}: results[0][].item_id is unknown: #s may be changed by ", warning, StringComparison.Ordinal);
        Assert.Contains(statement, warning, StringComparison.Ordinal);
        Assert.EndsWith("A declaration can't type it: after a call that is not followed, temporary tables are unknown even when its results are declared. To type the value, override its path under \"paths\" with \"type\" and \"import\".", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_selected_procedure_with_an_undeclared_unknowable_exec_is_a_warning_naming_the_statement()
    {
        var run = Apply("{}", "rpt.search");

        Assert.NotNull(run.Applied.Document);
        Assert.Equal(
            ["haruspex.json: warning: rpt.search: its result sets are unknown: EXEC sp_executesql @sql runs dynamic SQL, a string built when the procedure runs, so its result sets are not known. (procedures.sql:3:1) Declare them in haruspex.json under procedures[\"rpt.search\"].results, as \"none\" or a JSON Schema, or declare the results of the procedure that runs it."],
            run.Problems);
    }
}

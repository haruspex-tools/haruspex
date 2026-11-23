using System.Text;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A column the config file declares to hold JSON of a schema, read by inference as part of the column's type wherever the
/// column is read: plainly (JSON text of the schema's type), through <c>json_query</c> (live JSON of it), and through the routes
/// a value takes, with the claims the IR lists and the declarations it records. Why:
/// docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md. What SQL Server does with stored JSON is
/// observed in <c>DeclaredColumnObservationTests</c>; the fixture is <c>tests/fixtures/parcels</c>.
/// </summary>
public partial class DeclaredColumnTests
{
    private const string Tables = """
        CREATE SCHEMA shop;
        GO
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            settings nvarchar(max) NULL,
            extras varchar(2000) NOT NULL,
            code nvarchar(100) NULL,
            doc json NULL,
            notes nvarchar(max) NULL,
            total int NOT NULL);
        GO
        CREATE VIEW shop.order_view AS SELECT o.order_id, o.settings FROM shop.orders AS o;
        GO
        CREATE FUNCTION shop.settings_of(@id int) RETURNS nvarchar(max) AS BEGIN RETURN (SELECT o.settings FROM shop.orders AS o WHERE o.order_id = @id); END;
        GO
        """;

    private const string Settings = """
        { "type": "object", "required": ["mode"], "properties": {
            "mode": { "enum": ["fast", "slow"] },
            "note": { "type": ["string", "null"] } } }
        """;

    private const string Columns = """
        "columns": {
            "shop.orders.settings": { "json": "schemas/settings.schema.json" },
            "shop.orders.extras": { "json": "schemas/any.schema.json" },
            "shop.orders.code": { "json": "schemas/code.schema.json" },
            "shop.orders.doc": { "json": "schemas/settings.schema.json" } }
        """;

    /// <summary>Infers the procedures in <paramref name="procedures"/>, with the standard declarations and <paramref name="columns"/>.</summary>
    private static Outcome<IrDocument> Run(string procedures, string columns = Columns, string code = """{ "anyOf": [{ "type": "string" }, { "type": "array", "items": { "type": "integer" } }] }""")
    {
        using var dir = new TempDirectory();
        dir.File("db/tables.sql", Tables);
        dir.File("db/procedures.sql", procedures);
        dir.File("schemas/settings.schema.json", Settings);
        dir.File("schemas/any.schema.json", "{}");
        dir.File("schemas/code.schema.json", code);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        Assert.Empty(project.Diagnostics);
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes($"{{ {columns} }}"), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        return project.Infer(project.Procedures, read.Expected());
    }

    private static IrDocument Infer(string procedures) => Run(procedures).Expected();

    /// <summary>The fields of the object type the field <paramref name="name"/> holds, through JSON text or not.</summary>
    private static IReadOnlyList<IrField> Inside(IrDocument doc, string name) => Fields(doc, Field(doc, name).Type is IrJsonText { Content: { } content } ? content : Field(doc, name).Type);

    // ---- the read ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_plain_read_of_a_declared_column_is_json_text_of_its_schema_s_type_null_as_the_column_is()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT o.settings, o.extras FROM shop.orders AS o;");

        Assert.IsType<IrRef>(Assert.IsType<IrJsonText>(Field(doc, "settings").Type).Content);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "settings"));
        Assert.Equal(new IrJsonText(new IrAnyJson()), Field(doc, "extras").Type);
        Assert.Equal(["mode", "note"], Inside(doc, "settings").Select(f => f.Name));
    }

    [Fact]
    public void Json_query_of_a_declared_column_is_live_json_of_its_schema_s_type_with_its_keys_presence()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT json_query(o.settings) AS settings FROM shop.orders AS o FOR JSON PATH;");

        var settings = Fields(doc, Field(doc, "settings").Type);
        Assert.Equal((IrPresence.Always, false), (settings[0].Presence, settings[0].Nullable));
        Assert.Equal(IrPresence.Optional, settings[1].Presence);
        Assert.Equal(new IrNullReason(IrNullRule.Declared, "shop.orders.settings"), settings[1].NullableBecause.Single());
    }

    [Fact]
    public void Json_query_narrows_any_json_to_an_object_or_an_array()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT json_query(o.extras) AS extras FROM shop.orders AS o FOR JSON PATH;");

        Assert.Equal(new IrAnyJson(ObjectOrArray: true), Field(doc, "extras").Type);
    }

    [Fact]
    public void Json_query_keeps_only_the_variants_that_are_an_object_or_an_array_since_a_scalar_is_an_error()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT json_query(o.code) AS code FROM shop.orders AS o FOR JSON PATH;");

        Assert.Equal(new IrArray(new IrScalar(IrScalarKind.Number, null, null)), Field(doc, "code").Type);
    }

    [Fact]
    public void Json_query_of_json_declared_a_scalar_is_unknown_since_every_call_fails()
    {
        var doc = Run("CREATE PROCEDURE shop.p AS SELECT json_query(o.code) AS code FROM shop.orders AS o FOR JSON PATH;", code: """{ "type": "string" }""").Expected();

        Assert.Contains("13609", Unknown(doc, "results[0][].code").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_json_column_is_live_json_of_its_schema_s_type_as_a_json_column_is()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT o.doc FROM shop.orders AS o FOR JSON PATH;");

        Assert.Equal(["mode", "note"], Fields(doc, Field(doc, "doc").Type).Select(f => f.Name));
    }

    [Fact]
    public void For_json_nests_a_declared_string_column_as_a_string_holding_its_json()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT o.settings FROM shop.orders AS o FOR JSON PATH;");

        Assert.IsType<IrRef>(Assert.IsType<IrJsonText>(Field(doc, "settings").Type).Content);
    }

    [Fact]
    public void A_column_not_declared_is_read_as_before()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT json_query(o.notes) AS notes FROM shop.orders AS o FOR JSON PATH;");

        AssertUnknown(doc, "results[0][].notes", IrUnknownReason.JsonShapeUnknown);
    }

    [Fact]
    public void An_expression_over_a_declared_column_is_typed_from_the_column_s_own_type()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT LEN(o.extras) AS length, o.extras + 'x' AS more FROM shop.orders AS o;");

        Assert.Equal(IrScalarKind.Number, Assert.IsType<IrScalar>(Field(doc, "length").Type).Kind);
        Assert.StartsWith("varchar(", Assert.IsType<IrScalar>(Field(doc, "more").Type).Sql, StringComparison.Ordinal);
    }

    // ---- paths into it, by the rules for JSON built here, which declared columns observe and extend -------------

    [Fact]
    public void A_lax_path_to_a_key_not_required_may_find_nothing_so_is_nullable()
    {
        var doc = Infer("""
            CREATE PROCEDURE shop.p AS
            SELECT JSON_VALUE(o.settings, '$.mode') AS mode, JSON_VALUE(o.settings, '$.note') AS note, j.mode AS opened
            FROM shop.orders AS o CROSS APPLY OPENJSON(o.settings) WITH (mode varchar(10) '$.mode', note nvarchar(100) '$.note') AS j
            WHERE o.settings IS NOT NULL;
            """);

        Assert.Empty(Field(doc, "opened").NullableBecause);
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "note"));
    }

    [Fact]
    public void A_key_an_open_object_leaves_unlisted_may_hold_anything_so_a_path_to_it_is_read_as_any_json_s_and_its_keys_are_no_set()
    {
        var doc = Infer("""
            CREATE PROCEDURE shop.p AS
            SELECT JSON_VALUE(o.settings, '$.colour') AS colour FROM shop.orders AS o;
            GO
            CREATE PROCEDURE shop.q AS
            SELECT j.[key] FROM shop.orders AS o CROSS APPLY OPENJSON(o.settings) AS j;
            """);

        // As over JSON not built here: a string of at most 4,000 characters, which may be NULL.
        Assert.Equal(("nvarchar(4000)", true), (Assert.IsType<IrScalar>(Field(doc, "colour").Type).Sql, Field(doc, "colour").Nullable));
        Assert.Null(Assert.IsType<IrScalar>(Fields(doc, Object(doc, "shop.q").Results![0].Type).Single().Type).Facets?.Values);
    }

    // ---- the routes a value takes ------------------------------------------------------------------------------

    [Theory]
    [InlineData("view", "CREATE PROCEDURE shop.p AS SELECT json_query(v.settings) AS settings FROM shop.order_view AS v FOR JSON PATH;")]
    [InlineData("variable", "CREATE PROCEDURE shop.p @id int AS DECLARE @s nvarchar(max) = (SELECT o.settings FROM shop.orders AS o WHERE o.order_id = @id); SELECT json_query(@s) AS settings FOR JSON PATH;")]
    [InlineData("function", "CREATE PROCEDURE shop.p @id int AS SELECT json_query(shop.settings_of(@id)) AS settings FOR JSON PATH;")]
    [InlineData("temporary table", "CREATE PROCEDURE shop.p AS CREATE TABLE #t (settings nvarchar(max) NULL); INSERT INTO #t (settings) SELECT o.settings FROM shop.orders AS o; SELECT json_query(t.settings) AS settings FROM #t AS t FOR JSON PATH;")]
    [InlineData("table variable", "CREATE PROCEDURE shop.p AS DECLARE @t TABLE (settings nvarchar(max) NULL); INSERT INTO @t (settings) SELECT o.settings FROM shop.orders AS o; SELECT json_query(t.settings) AS settings FROM @t AS t FOR JSON PATH;")]
    [InlineData("OUTPUT … INTO a table variable", "CREATE PROCEDURE shop.p AS DECLARE @t TABLE (settings nvarchar(max) NULL); UPDATE TOP (50) o SET total = 0 OUTPUT inserted.settings INTO @t FROM shop.orders AS o; SELECT json_query(t.settings) AS settings FROM @t AS t FOR JSON PATH;")]
    [InlineData("OUTPUT … INTO a temporary table", "CREATE PROCEDURE shop.p AS CREATE TABLE #t (settings nvarchar(max) NULL); DELETE FROM shop.orders OUTPUT deleted.settings INTO #t; SELECT json_query(t.settings) AS settings FROM #t AS t FOR JSON PATH;")]
    [InlineData("INSERT … EXEC", "CREATE PROCEDURE shop.q AS SELECT o.settings FROM shop.orders AS o;\nGO\nCREATE PROCEDURE shop.p AS CREATE TABLE #t (settings nvarchar(max) NULL); INSERT INTO #t (settings) EXEC shop.q; SELECT json_query(t.settings) AS settings FROM #t AS t FOR JSON PATH;")]
    public void The_declaration_goes_wherever_the_column_s_value_goes(string route, string procedures)
    {
        var doc = Infer(procedures);

        var row = Assert.IsType<IrArray>(Assert.Single(Object(doc, "shop.p").Results!).Type).Items;
        Assert.True(Fields(doc, row).Single().Type is IrRef, route);
        Assert.Equal(["mode", "note"], Fields(doc, Fields(doc, row).Single().Type).Select(f => f.Name));
    }

    // ---- what the IR records -----------------------------------------------------------------------------------

    [Fact]
    public void Each_object_whose_results_may_rest_on_a_declaration_lists_the_claim_however_deep_the_read()
    {
        var doc = Infer("""
            CREATE PROCEDURE shop.direct AS SELECT o.settings FROM shop.orders AS o;
            GO
            CREATE PROCEDURE shop.through_view AS SELECT v.settings FROM shop.order_view AS v;
            GO
            CREATE PROCEDURE shop.through_call AS EXEC shop.through_view;
            GO
            CREATE PROCEDURE shop.through_function @id int AS SELECT shop.settings_of(@id) AS settings;
            GO
            CREATE PROCEDURE shop.unrelated AS SELECT o.total FROM shop.orders AS o;
            """);

        Assert.Equal(
            ["shop.direct", "shop.through_call", "shop.through_function", "shop.through_view"],
            doc.Overrides.Where(o => o.Setting == IrOverrideSetting.ColumnJson).Select(o => o.Object).Order(StringComparer.Ordinal));
        Assert.All(doc.Overrides.Where(o => o.Setting == IrOverrideSetting.ColumnJson), o => Assert.Equal(("results", "shop.orders.settings"), (o.Path, o.Via)));
    }

    [Fact]
    public void The_document_records_every_declaration_used_or_not_and_is_labelled_0_28()
    {
        var doc = Infer("CREATE PROCEDURE shop.p AS SELECT o.total FROM shop.orders AS o;");

        Assert.Equal(["shop.orders.code", "shop.orders.doc", "shop.orders.extras", "shop.orders.settings"], doc.Columns.Select(c => c.Column).Order(StringComparer.Ordinal));
        Assert.Equal("0.28", doc.Version);
    }

    // ---- what can be declared ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("shop.order_view.settings", "shop.order_view is a view, whose column holds what it reads: declare shop.orders.settings instead")]
    [InlineData("shop.orders.total", "shop.orders.total is of the type int, which can't hold JSON text")]
    [InlineData("shop.orders.missing", "the table shop.orders has no column missing")]
    [InlineData("shop.parcels.settings", "there is no table shop.parcels in the project")]
    public void A_column_that_can_t_hold_declared_json_is_an_error_saying_why(string column, string problem)
    {
        var run = Run("CREATE PROCEDURE shop.p AS SELECT o.total FROM shop.orders AS o;", $$"""
            "columns": { "{{column}}": { "json": "schemas/settings.schema.json" } }
            """);

        var error = Assert.Single(Assert.IsType<Failed<IrDocument>>(run).Diagnostics);
        Assert.Equal("HAR4113", error.Code.ToString());
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_when_a_schema_file_s_text_changes_though_no_type_does()
    {
        using var dir = new TempDirectory();
        dir.File("db/tables.sql", Tables);
        dir.File("db/procedures.sql", "CREATE PROCEDURE shop.p AS SELECT o.settings FROM shop.orders AS o;");
        dir.File("schemas/settings.schema.json", Settings);
        dir.File("haruspex.json", """{ "source": "db", "out": "out", "schemas": { "shop": { "include": ["*"] } }, "columns": { "shop.orders.settings": { "json": "schemas/settings.schema.json" } } }""");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, HaruspexCli.Run(["generate"], output, error, dir.Path));

        dir.File("schemas/settings.schema.json", Settings.Replace("\"type\": \"object\"", "\"type\": \"object\", \"description\": \"An order's settings\"", StringComparison.Ordinal));
        error.GetStringBuilder().Clear();
        var exit = HaruspexCli.Run(["check"], output, error, dir.Path);

        Assert.NotEqual(0, exit);
        Assert.Contains("columns[\"shop.orders.settings\"]: changed from schemas/settings.schema.json", error.ToString(), StringComparison.Ordinal);
    }
}

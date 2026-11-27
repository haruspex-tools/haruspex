using System.Text;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A procedure's or function's parameter declared to hold JSON of a schema, by <c>parameters</c> in the config file or by the
/// extended property <c>haruspex.jsonSchema</c> on the parameter: the body's reads of it are typed as declared until it is assigned,
/// with NULL kept wherever the server can give it; it is recorded as declared, with the claim on each object that takes it; the two
/// forms must agree; and what can't be declared is said. Why: docs/decisions/0143-a-parameter-s-json-may-be-declared.md. What the
/// server does with text that isn't JSON, or of another shape, is observed in <c>ParameterJsonObservationTests</c>.
/// </summary>
public class ParameterDeclarationTests
{
    private const string Objects = """
        CREATE SCHEMA shop;
        GO
        CREATE PROCEDURE shop.place_order @order nvarchar(max), @count int = 1, @receipt nvarchar(max) = NULL OUTPUT
        AS SELECT json_value(@order, '$.mode') AS mode;
        GO
        CREATE PROCEDURE shop.place_other @order nvarchar(max) AS SELECT json_value(@order, '$.mode') AS mode;
        GO
        CREATE FUNCTION shop.lines_of(@doc nvarchar(max)) RETURNS nvarchar(max) AS BEGIN RETURN json_query(@doc, '$.lines'); END;
        GO
        CREATE FUNCTION shop.modes(@doc nvarchar(max)) RETURNS TABLE AS RETURN SELECT json_value(@doc, '$.mode') AS mode;
        GO
        CREATE FUNCTION shop.modes_or_none(@doc nvarchar(max) = N'{}') RETURNS TABLE AS RETURN SELECT json_value(@doc, '$.mode') AS mode;
        GO
        CREATE FUNCTION shop.line_docs(@doc nvarchar(max)) RETURNS @t TABLE (lines nvarchar(max) NULL) AS
        BEGIN
            IF @doc IS NULL RETURN;
            INSERT @t (lines) SELECT json_query(@doc, '$.lines');
            RETURN;
        END;
        GO
        """;

    private const string Order = """
        { "type": "object", "required": ["mode"], "properties": {
            "mode": { "enum": ["fast", "slow"] },
            "note": { "type": "string" },
            "lines": { "type": "array", "items": { "type": "object", "required": ["sku"], "properties": { "sku": { "type": "string" } } } } },
          "$defs": { "other": { "type": "object", "required": ["speed"], "properties": { "speed": { "enum": ["low", "high"] } } } } }
        """;

    /// <summary>The annotation of <c>shop.place_order.@order</c> naming <paramref name="schema"/>.</summary>
    private static string Annotation(string schema = "schemas/order.schema.json", string owner = "PROCEDURE', N'place_order", string parameter = "@order") =>
        $"EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'{schema}', N'SCHEMA', N'shop', N'{owner}', N'PARAMETER', N'{parameter}';";

    private const string Configured = """ "shop.place_order.@order": { "json": "schemas/order.schema.json" } """;

    /// <summary>
    /// Infers <paramref name="procedures"/> beside the objects, with <paramref name="annotations"/> (a script of its own) and
    /// <paramref name="parameters"/> in the config file; only the procedures named <paramref name="selected"/> when given.
    /// </summary>
    private static Outcome<IrDocument> Run(string procedures, string annotations = "", string parameters = "", params string[] selected)
    {
        using var dir = new TempDirectory();
        dir.File("db/objects.sql", Objects);
        if (procedures.Length > 0)
            dir.File("db/procedures.sql", procedures);
        if (annotations.Length > 0)
            dir.File("db/annotations.sql", annotations);
        dir.File("schemas/order.schema.json", Order);
        dir.File("schemas/copy.schema.json", Order);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        Assert.DoesNotContain(project.Diagnostics, d => d.IsError);
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(parameters.Length == 0 ? "{}" : $$"""{ "parameters": { {{parameters}} } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        var chosen = selected.Length == 0 ? project.Procedures : [.. project.Procedures.Where(p => selected.Contains(p.ToString()))];
        return project.Infer(chosen, read.Expected());
    }

    /// <summary>The field of this name in the first result of the object <paramref name="id"/>.</summary>
    private static IrField FieldOf(IrDocument doc, string id, string name) =>
        Assert.Single(Fields(doc, (Object(doc, id).Results ?? throw new InvalidOperationException("unknown"))[0].Type), f => f.Name == name);

    private static string[] Values(IrField field) => [.. (field.Type as IrScalar)?.Facets?.Values?.Items.Select(v => v.Text) ?? []];

    /// <summary>The one diagnostic of a run, as its code and message.</summary>
    private static (string Code, string Message) Only(Outcome<IrDocument> run)
    {
        var found = Assert.Single(run.Diagnostics);
        return (found.Code.ToString(), found.Message);
    }

    // ---- what it declares ---------------------------------------------------------------------------------------

    [Fact]
    public void A_declared_parameter_s_json_value_is_typed_from_its_schema_and_stays_nullable_as_the_parameter_may_be_null()
    {
        var doc = Run("", parameters: Configured).Expected();

        var mode = FieldOf(doc, "shop.place_order", "mode");
        Assert.Equal(["fast", "slow"], Values(mode));
        Assert.True(mode.Nullable);
        Assert.Contains(IrNullRule.Parameter, Rules(mode));
    }

    [Fact]
    public void An_annotation_on_a_parameter_declares_what_the_same_entry_in_the_config_file_declares()
    {
        var annotated = Run("", Annotation()).Expected();
        var configured = Run("", parameters: Configured).Expected();

        Assert.Equal(configured.Objects, annotated.Objects);
        Assert.Equal(configured.Parameters, annotated.Parameters);
        Assert.Equal(configured.Overrides, annotated.Overrides);
    }

    [Fact]
    public void A_declaration_is_recorded_with_its_files_the_parameter_s_content_and_the_claim_of_the_object_that_takes_it()
    {
        var doc = Run("", parameters: Configured).Expected();

        Assert.Equal(IrDocument.CurrentVersion, doc.Version);
        var declared = Assert.Single(doc.Parameters);
        Assert.Equal(("shop.place_order.@order", "schemas/order.schema.json", null), (declared.Parameter, declared.Schema, declared.Pointer));
        Assert.Equal(["schemas/order.schema.json"], declared.Files.Keys);
        var parameter = Assert.Single(Object(doc, "shop.place_order").Parameters, p => p.Name == "@order");
        Assert.IsType<IrScalar>(parameter.Type);
        Assert.Equal(["lines", "mode", "note"], Fields(doc, Assert.IsType<IrRef>(parameter.Content)).Select(f => f.Name).Order(StringComparer.Ordinal));
        Assert.All(Object(doc, "shop.place_order").Parameters.Where(p => p.Name != "@order"), p => Assert.Null(p.Content));
        var claim = Assert.Single(doc.Overrides, o => o.Setting == IrOverrideSetting.ParameterJson);
        Assert.Equal(("shop.place_order", "results", "shop.place_order.@order"), (claim.Object, claim.Path, claim.Via));
    }

    [Fact]
    public void A_document_that_declares_no_parameter_is_written_as_before()
    {
        var doc = Run("").Expected();

        Assert.NotEqual(IrDocument.CurrentVersion, doc.Version);
        Assert.Empty(doc.Parameters);
        Assert.All(doc.Objects.SelectMany(o => o.Parameters), p => Assert.Null(p.Content));
    }

    [Fact]
    public void A_pointer_names_a_schema_inside_its_file()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) AS SELECT json_value(@order, '$.speed') AS speed;",
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json#/$defs/other" } """).Expected();

        Assert.Equal(["low", "high"], Values(FieldOf(doc, "shop.p", "speed")));
        Assert.Equal("/$defs/other", Assert.Single(doc.Parameters).Pointer);
    }

    // ---- NULL where the server can give it ---------------------------------------------------------------------

    [Fact]
    public void A_required_key_of_a_parameter_proved_not_null_is_not_null_as_declared()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) AS IF @order IS NULL RETURN; SELECT json_value(@order, '$.mode') AS mode;",
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json" } """).Expected();

        var mode = FieldOf(doc, "shop.p", "mode");
        Assert.False(mode.Nullable);
        Assert.Equal(["fast", "slow"], Values(mode));
    }

    [Fact]
    public void A_key_the_schema_leaves_optional_is_nullable_under_a_lax_path_though_the_parameter_is_proved_not_null()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) AS IF @order IS NULL RETURN; SELECT json_value(@order, '$.note') AS note, json_value(@order, '$.lines[0].sku') AS sku;",
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json" } """).Expected();

        Assert.True(FieldOf(doc, "shop.p", "note").Nullable);
        Assert.True(FieldOf(doc, "shop.p", "sku").Nullable);
    }

    // ---- assignments --------------------------------------------------------------------------------------------

    [Fact]
    public void A_variable_assigned_a_declared_parameter_holds_its_json()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) AS DECLARE @copy nvarchar(max) = @order; SELECT json_value(@copy, '$.mode') AS mode;",
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json" } """).Expected();

        Assert.Equal(["fast", "slow"], Values(FieldOf(doc, "shop.p", "mode")));
    }

    [Fact]
    public void A_parameter_assigned_before_the_read_loses_its_declaration_there()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) AS SELECT json_value(@order, '$.mode') AS before_set; SET @order = N'{\"mode\":\"other\"}'; SELECT json_value(@order, '$.mode') AS after_set;",
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json" } """).Expected();

        var results = Object(doc, "shop.p").Results!;
        Assert.Equal(["fast", "slow"], Values(Assert.Single(Fields(doc, results[0].Type))));
        Assert.Empty(Values(Assert.Single(Fields(doc, results[1].Type))));
    }

    // ---- the other JSON reads ----------------------------------------------------------------------------------

    [Fact]
    public void Json_query_and_openjson_over_a_declared_parameter_read_its_schema()
    {
        var doc = Run(
            """
            CREATE PROCEDURE shop.p @order nvarchar(max) AS
            SELECT l.sku FROM OPENJSON(@order, '$.lines') WITH (sku nvarchar(20) '$.sku') AS l;
            SELECT json_query(@order, '$.lines') AS lines FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """,
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json" } """).Expected();

        AllTyped(doc);
        var results = Object(doc, "shop.p").Results!;
        Assert.Equal("sku", Assert.Single(Fields(doc, results[0].Type)).Name);
        var lines = Assert.Single(Fields(doc, results[1].Type));
        Assert.IsType<IrArray>(lines.Type);
    }

    // ---- functions ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_scalar_function_s_declared_parameter_types_its_reads_and_its_caller_lists_the_claim()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) AS SELECT shop.lines_of(@order) AS lines;",
            Annotation(owner: "FUNCTION', N'lines_of", parameter: "@doc"),
            selected: "shop.p").Expected();

        var lines = Assert.IsType<IrJsonText>(FieldOf(doc, "shop.p", "lines").Type);
        Assert.Equal(["sku"], Fields(doc, Assert.IsType<IrArray>(lines.Content).Items).Select(f => f.Name));
        var claim = Assert.Single(doc.Overrides, o => o.Setting == IrOverrideSetting.ParameterJson);
        Assert.Equal(("shop.p", "shop.lines_of.@doc"), (claim.Object, claim.Via));
        Assert.Null(Assert.Single(Object(doc, "shop.p").Parameters).Content);
    }

    [Fact]
    public void An_inline_function_s_declared_parameter_holds_its_json_null_exactly_when_the_argument_may_be()
    {
        var doc = Run(
            """
            CREATE PROCEDURE shop.p AS
            SELECT m.mode FROM shop.modes(N'{"mode":"fast"}') AS m;
            SELECT m.mode FROM shop.modes(NULL) AS m;
            """,
            parameters: """ "shop.modes.@doc": { "json": "schemas/order.schema.json" } """).Expected();

        var results = Object(doc, "shop.p").Results!;
        var given = Assert.Single(Fields(doc, results[0].Type));
        var missing = Assert.Single(Fields(doc, results[1].Type));
        Assert.Equal(["fast", "slow"], Values(given));
        Assert.False(given.Nullable);
        Assert.True(missing.Nullable);
        Assert.Contains(doc.Overrides, o => o.Setting == IrOverrideSetting.ParameterJson && o.Via == "shop.modes.@doc");
    }

    [Fact]
    public void A_multi_statement_function_s_declared_parameter_types_its_reads_and_its_caller_lists_the_claim()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) AS SELECT json_query(d.lines) AS lines FROM shop.line_docs(@order) AS d FOR JSON PATH;",
            parameters: """ "shop.line_docs.@doc": { "json": "schemas/order.schema.json" } """,
            selected: "shop.p").Expected();

        var lines = FieldOf(doc, "shop.p", "lines");
        Assert.Equal(["sku"], Fields(doc, Assert.IsType<IrArray>(lines.Type).Items).Select(f => f.Name));
        var claim = Assert.Single(doc.Overrides, o => o.Setting == IrOverrideSetting.ParameterJson);
        Assert.Equal(("shop.p", "shop.line_docs.@doc"), (claim.Object, claim.Via));
    }

    // ---- a varchar parameter's code page ------------------------------------------------------------------------

    [Fact]
    public void A_varchar_parameter_s_reads_keep_a_value_set_only_when_it_is_ascii_and_what_a_caller_sends_keeps_all()
    {
        using var dir = new TempDirectory();
        dir.File("db/objects.sql", """
            CREATE SCHEMA shop;
            GO
            CREATE PROCEDURE shop.grade @doc varchar(max) AS
            IF @doc IS NULL RETURN;
            SELECT json_value(@doc, '$.grade') AS grade, json_value(@doc, '$.mode') AS mode;
            """);
        dir.File("schemas/grade.schema.json", """
            { "type": "object", "required": ["grade", "mode"], "properties": {
                "grade": { "enum": ["Ω", "alpha"] }, "mode": { "enum": ["fast", "slow"] } } }
            """);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        var config = ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "parameters": { "shop.grade.@doc": { "json": "schemas/grade.schema.json" } } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path).Expected();
        var doc = project.Infer(project.Procedures, config).Expected();

        // Read through the code page, Ω may come back as O (observed in ParameterJsonObservationTests).
        Assert.Empty(Values(FieldOf(doc, "shop.grade", "grade")));
        Assert.Equal(["fast", "slow"], Values(FieldOf(doc, "shop.grade", "mode")));
        var sent = Fields(doc, Assert.IsType<IrRef>(Assert.Single(Object(doc, "shop.grade").Parameters).Content));
        Assert.Equal(["alpha", "Ω"], Assert.IsType<IrScalar>(Assert.Single(sent, f => f.Name == "grade").Type).Facets?.Values?.Items.Select(v => v.Text).Order(StringComparer.Ordinal));
    }

    // ---- defaults ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_default_not_of_the_declared_shape_is_what_a_caller_that_leaves_the_parameter_out_sends()
    {
        // The generated function lets a caller leave @order out, and the server then sends N'{}', which has no mode: NULL, though
        // the schema requires it (observed by ann2-review: EXEC with no argument returns NULL).
        var doc = Run(
            "CREATE PROCEDURE shop.p @order nvarchar(max) = N'{}' AS IF @order IS NULL RETURN; SELECT json_value(@order, '$.mode') AS mode;",
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json" } """).Expected();

        Assert.True(FieldOf(doc, "shop.p", "mode").Nullable);
    }

    [Fact]
    public void A_default_of_the_declared_shape_keeps_the_declaration_and_a_null_default_adds_nothing()
    {
        var doc = Run(
            """
            CREATE PROCEDURE shop.p @order nvarchar(max) = N'{"mode":"fast"}', @other nvarchar(max) = NULL AS
            IF @order IS NULL OR @other IS NULL RETURN;
            SELECT json_value(@order, '$.mode') AS mode, json_value(@other, '$.mode') AS other_mode;
            """,
            parameters: """ "shop.p.@order": { "json": "schemas/order.schema.json" }, "shop.p.@other": { "json": "schemas/order.schema.json" } """).Expected();

        Assert.False(FieldOf(doc, "shop.p", "mode").Nullable);
        Assert.Equal(["fast", "slow"], Values(FieldOf(doc, "shop.p", "mode")));
        Assert.False(FieldOf(doc, "shop.p", "other_mode").Nullable);
    }

    [Fact]
    public void A_scalar_function_s_default_is_read_beside_its_declaration()
    {
        // shop.p calls it with DEFAULT, so the body reads {"mode":"warp"}, which the schema's enum doesn't allow.
        var doc = Run(
            """
            CREATE FUNCTION shop.mode_doc(@doc nvarchar(max) = N'{"mode":"warp"}') RETURNS nvarchar(max) WITH RETURNS NULL ON NULL INPUT
            AS BEGIN RETURN json_query(@doc); END;
            GO
            CREATE PROCEDURE shop.p AS SELECT json_value(shop.mode_doc(DEFAULT), '$.mode') AS mode;
            """,
            parameters: """ "shop.mode_doc.@doc": { "json": "schemas/order.schema.json" } """,
            selected: "shop.p").Expected();

        Assert.Contains("warp", Values(FieldOf(doc, "shop.p", "mode")));
    }

    [Fact]
    public void An_inline_function_called_with_default_reads_the_default_not_the_declaration()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p AS SELECT m.mode FROM shop.modes_or_none(DEFAULT) AS m;",
            parameters: """ "shop.modes_or_none.@doc": { "json": "schemas/order.schema.json" } """).Expected();

        var mode = FieldOf(doc, "shop.p", "mode");
        Assert.Empty(Values(mode));
        Assert.True(mode.Nullable);
    }

    // ---- which parameter --------------------------------------------------------------------------------------

    [Fact]
    public void An_annotation_declares_its_own_procedure_s_parameter_and_not_one_of_the_same_name_in_another()
    {
        var doc = Run("", Annotation()).Expected();

        Assert.Equal(["fast", "slow"], Values(FieldOf(doc, "shop.place_order", "mode")));
        Assert.Empty(Values(FieldOf(doc, "shop.place_other", "mode")));
        Assert.Null(Assert.Single(Object(doc, "shop.place_other").Parameters).Content);
        Assert.DoesNotContain(doc.Overrides, o => o.Object == "shop.place_other");
    }

    [Fact]
    public void A_numbered_procedure_s_parameter_can_t_be_declared_in_the_config_file()
    {
        var run = Run("", parameters: """ "shop.place_order;2.@order": { "json": "schemas/order.schema.json" } """);

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4117", code);
        Assert.Contains("shop.place_order;2 is a numbered procedure, whose parameters can't be declared", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_numbered_procedure_s_parameter_named_in_the_sql_is_not_one_the_source_defines()
    {
        var run = Run("", Annotation(owner: "PROCEDURE', N'place_order;2"));

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4115", code);
        Assert.Contains("the source doesn't define shop.place_order;2", message, StringComparison.Ordinal);
    }

    // ---- agreement with the config file ------------------------------------------------------------------------

    [Fact]
    public void An_annotation_that_names_the_schema_the_config_file_declares_agrees_and_is_one_declaration()
    {
        var run = Run("", Annotation(), """ "SHOP.Place_Order.@ORDER": { "json": "schemas/order.schema.json" } """);

        Assert.Empty(run.Diagnostics);
        Assert.Single(run.Expected().Parameters);
    }

    [Theory]
    [InlineData("schemas/copy.schema.json")]
    [InlineData("schemas/order.schema.json#/$defs/other")]
    public void An_annotation_that_names_another_schema_than_the_config_file_is_an_error_and_neither_is_taken(string schema)
    {
        var run = Run("", Annotation(schema), Configured);

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4114", code);
        Assert.Contains($"shop.place_order.@order is declared to hold JSON of {schema} here and of schemas/order.schema.json in the config file", message, StringComparison.Ordinal);
    }

    // ---- what can't be declared ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("shop.missing.@order", "there is no procedure or function shop.missing in the project")]
    [InlineData("shop.place_order.@missing", "shop.place_order has no parameter @missing")]
    [InlineData("shop.place_order.@count", "shop.place_order.@count is of the type int, which can't hold JSON text")]
    [InlineData("shop.place_order.@receipt", "shop.place_order.@receipt is an OUTPUT parameter")]
    public void A_declaration_of_a_parameter_that_can_t_hold_json_is_an_error_naming_why(string key, string problem)
    {
        var run = Run("", parameters: $$""" "{{key}}": { "json": "schemas/order.schema.json" } """);

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4117", code);
        Assert.Contains(problem, message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_annotation_on_a_parameter_that_can_t_hold_json_is_the_same_error()
    {
        var run = Run("", Annotation(parameter: "@count"));

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4117", code);
        Assert.Contains("shop.place_order.@count is of the type int", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shop.place_order.order", "names its parameter without its @")]
    [InlineData("shop.@order", "is not a parameter named schema.object.@name")]
    public void A_key_that_names_no_parameter_is_refused_by_the_config_reader(string key, string problem)
    {
        using var dir = new TempDirectory();
        dir.File("schemas/order.schema.json", Order);
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes($$"""{ "parameters": { "{{key}}": { "json": "schemas/order.schema.json" } } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);

        var error = Assert.Single(read.Diagnostics, d => d.IsError);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_whose_top_allows_null_is_refused_for_a_parameter()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/maybe.schema.json", """{ "type": ["object", "null"] }""");
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "parameters": { "shop.p.@order": { "json": "schemas/maybe.schema.json" } } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);

        var error = Assert.Single(read.Diagnostics, d => d.IsError);
        Assert.Equal("HAR3115", error.Code.ToString());
        Assert.Contains("a caller with no value sends NULL", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- check -------------------------------------------------------------------------------------------------

    /// <summary>Generates in a project whose parameter is annotated, then makes <paramref name="change"/> and runs check; its exit and error output.</summary>
    private static (int Exit, string Error) CheckAfter(Action<TempDirectory> change)
    {
        using var dir = new TempDirectory();
        dir.File("db/objects.sql", Objects);
        dir.File("db/annotations.sql", Annotation());
        dir.File("schemas/order.schema.json", Order);
        dir.File("schemas/copy.schema.json", Order);
        dir.File("haruspex.json", """{ "source": "db", "out": "out", "schemas": { "shop": { "include": ["place_order"] } } }""");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, HaruspexCli.Run(["generate"], output, error, dir.Path));

        change(dir);
        error.GetStringBuilder().Clear();
        return (HaruspexCli.Run(["check"], output, error, dir.Path), error.ToString());
    }

    [Fact]
    public void Check_passes_when_nothing_changed()
    {
        var (exit, error) = CheckAfter(_ => { });

        Assert.True(exit == 0, error);
    }

    [Fact]
    public void Check_fails_when_the_declared_schema_file_s_text_changes_though_no_type_does()
    {
        var (exit, error) = CheckAfter(dir => dir.File("schemas/order.schema.json", Order.Replace("\"type\": \"object\"", "\"type\": \"object\", \"description\": \"An order\"", StringComparison.Ordinal)));

        Assert.NotEqual(0, exit);
        Assert.Contains("parameters[\"shop.place_order.@order\"]: changed from schemas/order.schema.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_when_the_annotation_names_another_schema_file_or_is_removed()
    {
        var (renamed, renamedError) = CheckAfter(dir => dir.File("db/annotations.sql", Annotation("schemas/copy.schema.json")));
        var (removed, removedError) = CheckAfter(dir => File.Delete(Path.Combine(dir.Path, "db", "annotations.sql")));

        Assert.NotEqual(0, renamed);
        Assert.Contains("parameters[\"shop.place_order.@order\"]: changed from schemas/order.schema.json", renamedError, StringComparison.Ordinal);
        Assert.Contains("schemas/copy.schema.json", renamedError, StringComparison.Ordinal);
        Assert.NotEqual(0, removed);
        Assert.Contains("parameters[\"shop.place_order.@order\"]", removedError, StringComparison.Ordinal);
    }
}

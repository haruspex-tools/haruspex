using System.Text;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Paths into a column's declared JSON: <c>JSON_VALUE</c>, <c>json_query</c> with a path and <c>OPENJSON</c>, each typed by
/// walking the schema's type along the path (README.md in the inference project, "Paths into JSON"). What SQL Server does at each
/// step is observed in <c>DeclaredColumnObservationTests.Paths.cs</c>; why the walk is shaped so,
/// docs/decisions/0097-a-path-into-json-of-known-shape-is-walked-step-by-step.md.
/// </summary>
public partial class DeclaredColumnTests
{
    private const string Order = """
        { "type": "object", "additionalProperties": false,
          "required": ["mode", "rush", "count", "lines", "tags", "contact", "extra", "name"],
          "properties": {
            "mode": { "enum": ["fast", "slow"] },
            "rush": { "type": "boolean" },
            "note": { "type": ["string", "null"], "maxLength": 100 },
            "name": { "type": "string" },
            "count": { "type": "integer" },
            "address": { "type": "object", "additionalProperties": false, "required": ["city"],
                         "properties": { "city": { "type": "string", "maxLength": 50 }, "floor": { "type": "integer" } } },
            "lines": { "type": "array", "minItems": 1,
                       "items": { "type": "object", "required": ["sku"], "properties": { "sku": { "type": "string", "maxLength": 20 } } } },
            "tags": { "type": "object", "additionalProperties": { "type": "string", "maxLength": 10 } },
            "contact": { "anyOf": [ { "type": "string", "maxLength": 80 },
                                    { "type": "object", "required": ["email"], "properties": { "email": { "type": "string", "maxLength": 80 } } } ] },
            "extra": {},
            "a\"b": { "const": "q" } } }
        """;

    /// <summary>Infers <paramref name="procedures"/>, with <c>shop.orders.settings</c> declared to hold <see cref="Order"/>.</summary>
    private static Outcome<IrDocument> RunPaths(string procedures)
    {
        using var dir = new TempDirectory();
        dir.File("db/tables.sql", Tables);
        dir.File("db/procedures.sql", procedures);
        dir.File("schemas/order.schema.json", Order);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        Assert.Empty(project.Diagnostics);
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "columns": { "shop.orders.settings": { "json": "schemas/order.schema.json" } } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        return project.Infer(project.Procedures, read.Expected());
    }

    /// <summary>The fields of one row read from a declared column that is never NULL, by <paramref name="select"/>.</summary>
    private static IrDocument Select(string select) =>
        RunPaths($"CREATE PROCEDURE shop.p AS SELECT {select} FROM shop.orders AS o WHERE o.settings IS NOT NULL;").Expected();

    // ---- JSON_VALUE ----------------------------------------------------------------------------------------------

    [Fact]
    public void Json_value_of_a_required_key_holding_a_string_of_bounded_length_is_never_null_and_keeps_its_value_set()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.mode') AS mode");

        Assert.False(Field(doc, "mode").Nullable);
        var mode = Assert.IsType<IrScalar>(Field(doc, "mode").Type);
        Assert.Equal("nvarchar(4000)", mode.Sql);
        Assert.Equal(["fast", "slow"], mode.Facets!.Values!.Items.Select(v => v.Text));
    }

    [Fact]
    public void Json_value_of_a_key_not_required_may_find_nothing_under_lax_but_is_never_null_under_strict()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.address.city') AS city, JSON_VALUE(o.settings, 'strict $.address.city') AS strict_city");

        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "city"));
        Assert.False(Field(doc, "strict_city").Nullable);
    }

    [Fact]
    public void A_nullable_value_is_null_under_strict_too()
    {
        var doc = Select("JSON_VALUE(o.settings, 'strict $.note') AS note");

        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "note"));
    }

    [Fact]
    public void Json_value_of_an_object_is_null_under_lax_and_an_error_under_strict_never_a_string_of_it()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.address') AS address, JSON_VALUE(o.settings, '$.tags') AS tags, JSON_VALUE(o.settings, 'strict $.lines') AS lines");

        // tags is required and never null, so only its being an object makes it NULL.
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "tags"));
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "address"));
        Assert.Null(Assert.IsType<IrScalar>(Field(doc, "address").Type).Facets?.Values);
        Assert.False(Field(doc, "lines").Nullable);
    }

    [Fact]
    public void Json_value_of_a_string_whose_length_the_schema_does_not_bound_may_be_too_long_under_lax()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.name') AS name, JSON_VALUE(o.settings, 'strict $.name') AS strict_name");

        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "name"));
        Assert.False(Field(doc, "strict_name").Nullable);
    }

    [Fact]
    public void Json_value_of_a_declared_number_is_its_text_as_written_which_may_be_too_long_under_lax()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.count') AS count, JSON_VALUE(o.settings, 'strict $.count') AS strict_count");

        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "count"));
        Assert.Null(Assert.IsType<IrScalar>(Field(doc, "count").Type).Facets?.Values);
        Assert.False(Field(doc, "strict_count").Nullable);
    }

    [Fact]
    public void Json_value_of_true_or_false_is_the_text_true_or_false()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.rush') AS rush");

        Assert.False(Field(doc, "rush").Nullable);
        Assert.Equal(["false", "true"], Assert.IsType<IrScalar>(Field(doc, "rush").Type).Facets!.Values!.Items.Select(v => v.Text));
    }

    [Fact]
    public void An_index_past_the_fewest_items_may_find_nothing_and_one_within_them_always_finds_an_item()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.lines[0].sku') AS first, JSON_VALUE(o.settings, '$.lines[1].sku') AS second");

        Assert.False(Field(doc, "first").Nullable);
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "second"));
    }

    [Fact]
    public void A_key_of_a_map_may_be_missing_since_its_keys_are_data()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.tags.colour') AS colour");

        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "colour"));
        Assert.Equal("nvarchar(4000)", Assert.IsType<IrScalar>(Field(doc, "colour").Type).Sql);
    }

    [Fact]
    public void A_path_through_a_union_finds_what_each_variant_finds_and_nothing_in_the_others()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.contact') AS contact, JSON_VALUE(o.settings, '$.contact.email') AS email");

        // The string is given; the object is NULL under lax. The email is in the object only.
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "contact"));
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "email"));
    }

    [Fact]
    public void A_path_into_any_json_may_find_anything_or_nothing()
    {
        var doc = Select("JSON_VALUE(o.settings, 'strict $.extra.a.b') AS b");

        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "b"));
    }

    [Fact]
    public void Explain_names_the_step_that_may_find_nothing_and_why()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.address.city') AS city");

        Assert.Equal("`$.address` may be left out, as the schema doesn't require it", Field(doc, "city").NullableBecause.Single().Detail);
        Assert.Equal("0.29", doc.Version);
    }

    [Fact]
    public void A_path_the_schema_can_never_have_is_warned_of_once_and_reads_as_always_null()
    {
        var run = RunPaths("CREATE PROCEDURE shop.p AS SELECT JSON_VALUE(o.settings, '$.colour') AS colour, JSON_VALUE(o.settings, '$.mode.x') AS x FROM shop.orders AS o;");

        Assert.Equal(["HAR2102", "HAR2102"], run.Diagnostics.Select(d => d.Code.ToString()));
        Assert.Contains("`$.colour` is never there: the object has no key `colour`", run.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("`$.mode.x` is never there: `$.mode` is a string", run.Diagnostics[^1].Message, StringComparison.Ordinal);
        Assert.True(Field(run.Expected(), "colour").Nullable);
    }

    [Fact]
    public void A_path_into_an_open_object_s_unlisted_key_is_not_warned_of()
    {
        var run = Run("CREATE PROCEDURE shop.p AS SELECT JSON_VALUE(o.settings, '$.colour') AS colour FROM shop.orders AS o;");

        Assert.DoesNotContain(run.Diagnostics, d => d.Code.ToString() == "HAR2102");
    }

    [Theory]
    [InlineData("LAX $.mode")]
    [InlineData("$.lines[*].sku")]
    [InlineData("$.\"mo\\u0064e\"")]
    public void A_path_not_read_falls_back_to_json_value_s_own_type(string path)
    {
        var doc = Select($"JSON_VALUE(o.settings, '{path}') AS v");

        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "v"));
        Assert.Null(Assert.IsType<IrScalar>(Field(doc, "v").Type).Facets?.Values);
    }

    [Fact]
    public void Quoted_keys_an_escaped_quote_and_blanks_between_steps_are_read_as_the_server_reads_them()
    {
        var doc = Select("JSON_VALUE(o.settings, '$.\"mode\"') AS mode, JSON_VALUE(o.settings, '  strict $ .\"lines\" [ 00 ].sku ') AS sku, JSON_VALUE(o.settings, '$.\"a\\\"b\"') AS ab");

        Assert.False(Field(doc, "mode").Nullable);
        Assert.False(Field(doc, "sku").Nullable);
        Assert.Equal(["q"], Assert.IsType<IrScalar>(Field(doc, "ab").Type).Facets!.Values!.Items.Select(v => v.Text));
    }

    // ---- json_query with a path ------------------------------------------------------------------------------------

    [Fact]
    public void Json_query_at_a_path_is_live_json_of_the_schema_there()
    {
        var doc = RunPaths("CREATE PROCEDURE shop.p AS SELECT json_query(o.settings, '$.lines') AS lines, json_query(o.settings, '$.address') AS address FROM shop.orders AS o WHERE o.settings IS NOT NULL FOR JSON PATH;").Expected();

        var lines = Assert.IsType<IrArray>(Field(doc, "lines").Type);
        Assert.Equal(["sku"], Fields(doc, lines.Items).Select(f => f.Name));
        Assert.False(Field(doc, "lines").Nullable);
        Assert.Equal(["city", "floor"], Fields(doc, Field(doc, "address").Type).Select(f => f.Name));
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "address"));
    }

    [Fact]
    public void Json_query_keeps_a_union_s_objects_and_arrays_and_is_null_under_lax_where_it_finds_a_scalar()
    {
        var doc = RunPaths("CREATE PROCEDURE shop.p AS SELECT json_query(o.settings, '$.contact') AS contact, json_query(o.settings, 'strict $.contact') AS strict_contact FROM shop.orders AS o WHERE o.settings IS NOT NULL FOR JSON PATH;").Expected();

        Assert.Equal(["email"], Fields(doc, Field(doc, "contact").Type).Select(f => f.Name));
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "contact"));
        Assert.False(Field(doc, "strict_contact").Nullable);
    }

    [Fact]
    public void Json_query_of_a_path_into_any_json_is_an_object_or_array_that_may_be_null()
    {
        var doc = RunPaths("CREATE PROCEDURE shop.p AS SELECT json_query(o.settings, '$.extra') AS extra FROM shop.orders AS o WHERE o.settings IS NOT NULL FOR JSON PATH;").Expected();

        Assert.Equal(new IrAnyJson(ObjectOrArray: true), Field(doc, "extra").Type);
        Assert.True(Field(doc, "extra").Nullable);
    }

    [Theory]
    [InlineData("json_query(o.settings, '$.mode')", "never finds an object or array")]
    [InlineData("json_query(o.settings, @path)", "not a literal")]
    [InlineData("json_query(o.settings, '$.lines[*]')", "several values")]
    [InlineData("json_query(o.notes, '$.a')", "not built here or declared")]
    public void Json_query_at_a_path_whose_object_or_array_is_not_known_is_unknown_saying_why(string call, string why)
    {
        var doc = RunPaths($"CREATE PROCEDURE shop.p @path nvarchar(100) AS SELECT {call} AS v FROM shop.orders AS o FOR JSON PATH;").Expected();

        var unknown = Unknown(doc, "results[0][].v");
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, unknown.Reason);
        Assert.Contains(why, unknown.Detail, StringComparison.Ordinal);
    }

    // ---- OPENJSON ------------------------------------------------------------------------------------------------

    [Fact]
    public void Openjson_with_types_each_column_by_its_declared_sql_type_not_by_the_schema()
    {
        var doc = RunPaths("""
            CREATE PROCEDURE shop.p AS
            SELECT j.mode, j.count, j.sku, j.city, j.lines
            FROM shop.orders AS o CROSS APPLY OPENJSON(o.settings) WITH (mode varchar(3) '$.mode', count nvarchar(5) '$.count', sku nvarchar(20) '$.lines[0].sku',
                                                                         city nvarchar(50) '$.address.city', lines nvarchar(max) '$.lines' AS JSON) AS j
            FOR JSON PATH;
            """).Expected();

        Assert.Equal("varchar(3)", Assert.IsType<IrScalar>(Field(doc, "mode").Type).Sql);
        Assert.Equal("nvarchar(5)", Assert.IsType<IrScalar>(Field(doc, "count").Type).Sql);

        // A string cut short is never NULL; a key not required may be missing.
        Assert.False(Field(doc, "mode").Nullable);
        Assert.False(Field(doc, "sku").Nullable);
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "city"));
        Assert.Equal(["sku"], Fields(doc, Assert.IsType<IrArray>(Field(doc, "lines").Type).Items).Select(f => f.Name));
    }

    [Fact]
    public void Openjson_with_into_a_number_type_is_null_only_where_overflow_is_under_the_callers_options()
    {
        var doc = RunPaths("""
            CREATE PROCEDURE shop.p AS
            SELECT j.count, j.rush FROM shop.orders AS o CROSS APPLY OPENJSON(o.settings) WITH (count bigint '$.count', rush bit '$.rush') AS j;
            """).Expected();

        Assert.Equal([IrNullRule.ArithmeticOverflow], Rules(doc, "count"));
        Assert.False(Field(doc, "rush").Nullable);
    }

    [Fact]
    public void Openjson_at_a_row_path_opens_the_object_or_array_there_with_its_keys_and_types()
    {
        var doc = RunPaths("""
            CREATE PROCEDURE shop.p AS
            SELECT j.[key], j.type FROM shop.orders AS o CROSS APPLY OPENJSON(o.settings, '$.address') AS j;
            """).Expected();

        Assert.Equal(["city", "floor"], Assert.IsType<IrScalar>(Field(doc, "key").Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(["1", "2"], Assert.IsType<IrScalar>(Field(doc, "type").Type).Facets!.Values!.Items.Select(v => v.Text));
    }
}

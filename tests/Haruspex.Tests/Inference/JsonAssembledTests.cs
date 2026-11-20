using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Inference;

/// <summary>
/// JSON assembled as text: arrays glued together with <c>+</c>, <c>concat</c>, <c>concat_ws</c> and <c>string_agg</c>, read with
/// <c>json_query</c>. Each element is typed only where the server guarantees its text is one JSON value; every rule rests on an
/// observation in the differential tests' <c>JsonAssembledObservationTests</c>, named where it matters.
/// </summary>
public partial class JsonAssembledTests
{
    private const string Tables = """
        CREATE TABLE dbo.[order] (order_id int NOT NULL PRIMARY KEY, ref nvarchar(20) NOT NULL, channel varchar(10) NULL);
        GO
        CREATE TABLE dbo.line (line_id int NOT NULL PRIMARY KEY, order_id int NOT NULL, qty smallint NULL, price decimal(9,2) NOT NULL,
            fee money NOT NULL, weight float NOT NULL, gift bit NOT NULL, note nvarchar(30) NULL);
        GO
        CREATE TABLE dbo.tag (order_id int NOT NULL, name nvarchar(15) NOT NULL);
        GO
        """;

    private static IrDocument Procedure(string body, SessionOptions? session = null, string more = "") =>
        InferProcedure.Infer(
            "dbo.p",
            session ?? SessionOptions.NotKnown,
            [("tables.sql", Tables), .. more.Length > 0 ? [("more.sql", more)] : System.Array.Empty<(string, string)>(), ("p.sql", $"CREATE PROCEDURE dbo.p @id int AS\n{body}")]);

    /// <summary>The field <paramref name="name"/> of the first result's row, a FOR JSON object.</summary>
    private static IrField Field(IrDocument doc, string name) => Fields(doc, Row(doc)).Single(f => f.Name == name);

    private static IrRef Row(IrDocument doc) => doc.Objects[0].Results![0].Type switch
    {
        IrArray { Items: IrRef r } => r,
        IrRef r => r,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    private static EquatableArray<IrField> Fields(IrDocument doc, IrType type) => doc.Types[Assert.IsType<IrRef>(type).Name].Fields;

    /// <summary>The unknown at the end of <paramref name="field"/>'s path.</summary>
    private static IrUnknown Unknown(IrDocument doc, string field) => Assert.Single(doc.Unknowns, u => u.Path.EndsWith("." + field, StringComparison.Ordinal));

    /// <summary>A FOR JSON object of one key, <c>v</c>, read with json_query from <paramref name="text"/>, for each order.</summary>
    private static string Read(string text) => $"SELECT json_query({text}) AS v FROM dbo.[order] AS o WHERE o.order_id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;";

    private const string LineIds = "(SELECT string_agg(convert(nvarchar(max), l.line_id), N',') FROM dbo.line AS l WHERE l.order_id = o.order_id)";

    private static IrArray Array(IrDocument doc, string name = "v") => Assert.IsType<IrArray>(Field(doc, name).Type);

    // ---- integers aggregated -------------------------------------------------------------------------------------------

    [Fact]
    public void Integers_aggregated_into_brackets_with_coalesce_are_an_array_of_integers_never_null_and_maybe_empty()
    {
        var doc = Procedure("SELECT json_query(N'[' + coalesce(string_agg(convert(nvarchar(max), l.line_id), N','), N'') + N']') AS v FROM dbo.line AS l WHERE l.order_id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;");
        var v = Field(doc, "v");
        var array = Assert.IsType<IrArray>(v.Type);

        Assert.Empty(v.NullableBecause);
        Assert.False(array.ItemsNullable);
        Assert.Null(array.MinItems);
        Assert.Equal("int", Assert.IsType<IrScalar>(array.Items).Sql);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Plus_over_an_aggregate_is_null_over_no_rows_and_empty_brackets_only_if_concat_null_yields_null_may_be_off()
    {
        // Observed: An_aggregate_over_no_rows_is_null_so_plus_is_null_concat_is_empty_and_quotes_in_concat_make_one_empty_string
        // and With_concat_null_yields_null_off_plus_over_a_null_aggregate_is_empty_brackets.
        var unknown = Procedure(Read($"N'[' + {LineIds} + N']'"));
        Assert.Equal([IrNullRule.EmptyAggregate], Field(unknown, "v").NullableBecause.Select(r => r.Rule).Distinct());
        Assert.Null(Array(unknown).MinItems);

        var on = Procedure(Read($"N'[' + {LineIds} + N']'"), new SessionOptions(ConcatNullYieldsNull: true));
        Assert.Equal(1, Array(on).MinItems);
    }

    [Fact]
    public void Concat_counts_a_null_aggregate_as_empty_so_its_array_is_never_null()
    {
        var doc = Procedure(Read($"concat(N'[', {LineIds}, N']')"));

        Assert.Empty(Field(doc, "v").NullableBecause);
        Assert.Null(Array(doc).MinItems);
    }

    [Fact]
    public void Coalesce_of_a_subquery_concatenating_brackets_and_an_empty_array_literal_is_an_array()
    {
        var doc = Procedure(Read("coalesce((SELECT concat(N'[', string_agg(convert(nvarchar(max), l.line_id), N','), N']') FROM dbo.line AS l WHERE l.order_id = o.order_id), N'[]')"));

        Assert.Equal("int", Assert.IsType<IrScalar>(Array(doc).Items).Sql);
        Assert.Empty(doc.Unknowns);
    }

    // ---- strings, escaped and not --------------------------------------------------------------------------------------

    [Fact]
    public void Escaped_strings_each_in_quotes_are_an_array_of_the_strings_with_their_facets()
    {
        var doc = Procedure(Read("N'[' + coalesce((SELECT string_agg(cast(N'\"' + string_escape(t.name, 'json') + N'\"' AS nvarchar(max)), N',') FROM dbo.tag AS t WHERE t.order_id = o.order_id), N'') + N']'"));
        var items = Assert.IsType<IrScalar>(Array(doc).Items);

        Assert.Equal(IrScalarKind.String, items.Kind);
        Assert.Equal("nvarchar(15)", items.Sql);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Escaped_strings_with_the_quotes_in_the_separator_allow_the_one_empty_string_concat_makes_of_no_rows()
    {
        var doc = Procedure(Read("concat(N'[\"', (SELECT string_agg(string_escape(t.name, 'json'), N'\",\"') FROM dbo.tag AS t WHERE t.order_id = o.order_id), N'\"]')"));
        var array = Array(doc);

        // Over no rows it is [""]: one item, which the item type allows.
        Assert.Equal(1, array.MinItems);
        var variants = Assert.IsType<IrUnion>(array.Items).Variants;
        Assert.Contains(variants, t => t is IrScalar { Facets.Values.Items: [{ Text: "" }] });
        Assert.Contains(variants, t => t is IrScalar { Sql: "nvarchar(15)" });
    }

    [Fact]
    public void An_unescaped_value_in_quotes_is_unknown_since_a_quotation_mark_in_it_would_change_the_array()
    {
        // Observed: An_unescaped_value_in_quotes_can_make_more_elements_a_number_or_cut_the_array_short.
        var doc = Procedure(Read("N'[' + (SELECT string_agg(N'\"' + t.name + N'\"', N',') FROM dbo.tag AS t WHERE t.order_id = o.order_id) + N']'"));
        var unknown = Unknown(doc, "v");

        Assert.Equal(IrUnknownReason.JsonShapeUnknown, unknown.Reason);
        Assert.Contains("t.name is written inside quotes without string_escape", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_converted_to_text_in_quotes_is_a_string()
    {
        var doc = Procedure(Read("N'[\"' + cast(o.order_id AS nvarchar(20)) + N'\"]'"));
        var tuple = Assert.IsType<IrTuple>(Field(doc, "v").Type);

        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(Assert.Single(tuple.Items).Type).Kind);
    }

    [Fact]
    public void A_value_whose_every_value_is_a_literal_without_quotes_is_a_string_in_quotes()
    {
        var doc = Procedure("""
            SELECT json_query(N'["' + k.kind + N'"]') AS v
            FROM (SELECT N'retail' AS kind UNION ALL SELECT N'trade') AS k FOR JSON PATH;
            """);
        var item = Assert.IsType<IrScalar>(Assert.Single(Assert.IsType<IrTuple>(Field(doc, "v").Type).Items).Type);

        Assert.Equal(["retail", "trade"], item.Facets!.Values!.Items.Select(v => v.Text).Order(StringComparer.Ordinal));
    }

    // ---- numbers ---------------------------------------------------------------------------------------------------------

    [Theory]
    // Observed: A_number_converted_to_text_is_written_so. An integer, a decimal and money in style 2 or 126 are written as a JSON
    // number of their value; a bit as 0 or 1.
    [InlineData("convert(nvarchar(max), l.qty)", "smallint")]
    [InlineData("convert(nvarchar(max), l.price)", "decimal(9,2)")]
    [InlineData("convert(nvarchar(max), l.fee, 2)", "money")]
    [InlineData("convert(nvarchar(max), l.fee, 126)", "money")]
    [InlineData("cast(l.line_id AS nvarchar)", "int")]
    [InlineData("l.line_id", "int")]
    public void A_number_written_as_its_value_is_a_json_number(string item, string sql)
    {
        var doc = Procedure(Read($"N'[' + (SELECT string_agg({item}, N',') FROM dbo.line AS l WHERE l.order_id = o.order_id) + N']'"));

        Assert.Equal(sql, Assert.IsType<IrScalar>(Array(doc).Items).Sql);
    }

    [Fact]
    public void A_bit_converted_to_text_is_the_number_0_or_1()
    {
        var doc = Procedure(Read("N'[' + (SELECT string_agg(convert(nvarchar(max), l.gift), N',') FROM dbo.line AS l WHERE l.order_id = o.order_id) + N']'"));
        var items = Assert.IsType<IrScalar>(Array(doc).Items);

        Assert.Equal(IrScalarKind.Number, items.Kind);
        Assert.Equal(["0", "1"], items.Facets!.Values!.Items.Select(v => v.Text));
    }

    [Theory]
    // money with no style is rounded to two places, and with style 1 has commas; float and real keep six digits.
    [InlineData("convert(nvarchar(max), l.fee)")]
    [InlineData("convert(nvarchar(max), l.fee, 1)")]
    [InlineData("convert(nvarchar(max), l.weight)")]
    [InlineData("convert(nvarchar(max), l.weight, 3)")]
    public void A_number_whose_text_is_not_its_value_is_unknown(string item)
    {
        var doc = Procedure(Read($"N'[' + (SELECT string_agg({item}, N',') FROM dbo.line AS l WHERE l.order_id = o.order_id) + N']'"));

        Assert.Contains("not a JSON number of its value", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_separator_glues_the_items_together_so_it_is_unknown()
    {
        // Observed: String_agg_with_a_null_separator_glues_its_values_together.
        var doc = Procedure(Read("N'[' + (SELECT string_agg(convert(nvarchar(max), l.line_id), NULL) FROM dbo.line AS l WHERE l.order_id = o.order_id) + N']'"));

        Assert.Contains("separator is not a literal comma", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    // ---- concat_ws ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Concat_ws_of_quoted_literals_is_any_subset_of_them()
    {
        // Observed: Concat_ws_skips_null_values_so_quoted_literals_make_any_subset_and_none_makes_empty_brackets.
        var doc = Procedure(Read("N'[' + concat_ws(N',', CASE WHEN o.channel = 'r' THEN N'\"retail\"' END, CASE WHEN o.channel = 't' THEN N'\"trade\"' END) + N']'"));
        var array = Array(doc);

        Assert.Null(array.MinItems);
        Assert.Equal(2, array.MaxItems);
        Assert.Equal(["retail", "trade"], Assert.IsType<IrScalar>(array.Items).Facets!.Values!.Items.Select(v => v.Text).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Concat_ws_read_through_a_function_into_a_variable_keeps_its_json()
    {
        var doc = Procedure(
            "DECLARE @c nvarchar(max) = dbo.channels(@id);\nSELECT json_query(@c) AS v FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;",
            more: """
                CREATE FUNCTION dbo.channels (@id int) RETURNS nvarchar(100) AS
                BEGIN
                    RETURN N'[' + concat_ws(N',', CASE WHEN @id > 1 THEN N'"retail"' END, CASE WHEN @id > 2 THEN N'"trade"' END) + N']';
                END
                """);

        Assert.Equal(2, Array(doc).MaxItems);
        Assert.Empty(doc.Unknowns);
    }

    // ---- fragments --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Json_fragments_joined_in_brackets_are_a_tuple_of_their_shapes()
    {
        var doc = Procedure(Read("concat(N'[', json_object('id': o.order_id), N',', (SELECT l.line_id FROM dbo.line AS l WHERE l.order_id = o.order_id FOR JSON PATH), N']')"));
        var tuple = Assert.IsType<IrTuple>(Field(doc, "v").Type);

        Assert.Equal(2, tuple.Items.Count);
        Assert.IsType<IrRef>(tuple.Items[0].Type);
        Assert.IsType<IrArray>(tuple.Items[1].Type);
    }

    [Fact]
    public void String_agg_of_each_rows_object_is_an_array_of_objects()
    {
        var doc = Procedure(Read("N'[' + (SELECT string_agg(json_object('id': l.line_id, 'qty': l.qty), N',') FROM dbo.line AS l WHERE l.order_id = o.order_id) + N']'"));

        Assert.Equal(["id", "qty"], Fields(doc, Array(doc).Items).Select(f => f.Name));
    }

    [Fact]
    public void A_for_json_fragment_without_its_wrapper_over_many_rows_is_unknown_and_over_one_row_is_its_object()
    {
        // Observed: A_for_json_fragment_without_its_array_wrapper_holds_every_row_so_in_brackets_it_is_an_array_of_them.
        var many = Procedure(Read("concat(N'[', (SELECT l.line_id FROM dbo.line AS l WHERE l.order_id = o.order_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N']')"));
        Assert.Contains("may not be one JSON value", Unknown(many, "v").Detail, StringComparison.Ordinal);

        var one = Procedure(Read("concat(N'[', (SELECT o.ref AS ref FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N']')"));
        Assert.IsType<IrRef>(Assert.IsType<IrTuple>(Field(one, "v").Type).Items[0].Type);
    }

    // ---- what is never read -----------------------------------------------------------------------------------------------

    [Fact]
    public void Text_that_may_not_be_an_array_is_unknown_where_it_is_held_since_brackets_around_it_could_make_json_of_it()
    {
        // json_query refuses '' and '1,2', but '[' + it + ']' would be [] or [1,2], so a place holding either is not read.
        var doc = Procedure($"""
            DECLARE @v nvarchar(max) = (SELECT coalesce(N'[' + string_agg(convert(nvarchar(max), l.line_id), N',') + N']', N'') FROM dbo.line AS l);
            SELECT json_query(@v) AS v FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """);

        Assert.Contains("one way the text can be is empty", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_cut_short_by_a_conversion_and_then_followed_by_more_is_unknown()
    {
        // Cut at five characters, an escaped name loses its closing quote, and the text after it would be read as the string's.
        var doc = Procedure(Read("concat(N'[', (SELECT string_agg(cast(N'\"' + string_escape(t.name, 'json') + N'\"' AS nvarchar(5)), N',') FROM dbo.tag AS t WHERE t.order_id = o.order_id), N']')"));

        Assert.Contains("which may be cut short", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    // ---- read without json_query --------------------------------------------------------------------------------------------

    [Fact]
    public void Assembled_text_read_without_json_query_stays_a_string()
    {
        // Observed: Json_query_of_text_that_is_not_json_is_an_error_wherever_it_is_and_text_read_without_it_is_a_string.
        var doc = Procedure($"""
            DECLARE @ids nvarchar(max) = (SELECT N'[' + coalesce(string_agg(convert(nvarchar(max), l.line_id), N','), N'') + N']' FROM dbo.line AS l);
            SELECT @ids AS a, N'[' + coalesce({LineIds}, N'') + N']' AS b, json_query(@ids) AS c FROM dbo.[order] AS o FOR JSON PATH;
            """);

        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(Field(doc, "a").Type).Kind);
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(Field(doc, "b").Type).Kind);
        Assert.Equal("int", Assert.IsType<IrScalar>(Array(doc, "c").Items).Sql);
    }
}

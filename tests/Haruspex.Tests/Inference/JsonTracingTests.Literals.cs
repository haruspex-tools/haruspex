using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class JsonTracingTests
{
    [Fact]
    public void A_json_literal_is_its_exact_shape()
    {
        // Observed: A_json_literal_is_read_exactly_and_a_bare_scalar_or_text_that_is_not_json_is_an_error.
        var doc = Infer("""SELECT json_query('[]') AS empty, json_query(N' {"s": "x", "n": 1.50, "b": true, "z": null, "a": [1, "y"]} ') AS obj FOR JSON PATH;""");

        Assert.Empty(Assert.IsType<IrTuple>(Field(doc, "empty").Type).Items);
        var obj = Field(doc, "obj");
        Assert.False(obj.Nullable);
        Assert.Equal(["x"], Assert.IsType<IrScalar>(Sub(doc, obj.Type, "s").Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(["1.5"], Assert.IsType<IrScalar>(Sub(doc, obj.Type, "n").Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(IrScalarKind.Boolean, Assert.IsType<IrScalar>(Sub(doc, obj.Type, "b").Type).Kind);
        Assert.Equal([IrNullRule.NullLiteral], Rules(Sub(doc, obj.Type, "z")));
        Assert.Equal(IrPresence.Always, Sub(doc, obj.Type, "z").Presence);
        Assert.Equal(2, Assert.IsType<IrTuple>(Sub(doc, obj.Type, "a").Type).Items.Count);
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    // Json_query_with_array_wrapper_wraps_what_it_finds_in_an_array_a_scalar_at_a_path_too: an array, never the object
    // json_query gives without the clause, so until it is modelled the call is unknown, with a path or without.
    [InlineData("json_query(json_object('a': 1) WITH ARRAY WRAPPER)")]
    [InlineData("""json_query(N'{"a":1}' WITH ARRAY WRAPPER)""")]
    [InlineData("""json_query(N'{"a":{"b":1}}', '$.a' WITH ARRAY WRAPPER)""")]
    public void Json_query_with_array_wrapper_is_unknown(string call)
    {
        var doc = Infer($"SELECT {call} AS x FOR JSON PATH;");

        Assert.Contains("WITH ARRAY WRAPPER", Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("'1'", "bare JSON scalar")]
    [InlineData("'null'", "bare JSON scalar")]
    [InlineData("''", "not valid JSON")]
    [InlineData("N''", "not valid JSON")]
    [InlineData("'[1]x'", "not valid JSON")]
    [InlineData("'{\"a\":1,\"a\":2}'", "key written twice")]
    [InlineData("'[\"ā\"]'", "not an N'' literal")]
    [InlineData("'[\"\\u0101\"]'", "not an N'' literal")]
    [InlineData("'{\"ā\": 1}'", "not an N'' literal")]
    [InlineData("N'[1e400]'", "Infinity")]
    public void A_literal_that_is_not_a_json_document_is_unknown_saying_why(string literal, string why)
    {
        var doc = Infer($"SELECT json_query({literal}) AS x FOR JSON PATH;");
        Assert.Contains(why, Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_n_literal_keeps_a_string_that_is_not_ascii()
    {
        // Observed: A_literal_without_n_is_converted_to_the_code_page_before_it_is_read_as_json.
        var doc = Infer("""SELECT json_query(N'["ā", "\u0101"]') AS x FOR JSON PATH;""");
        var items = Assert.IsType<IrTuple>(Field(doc, "x").Type).Items;
        Assert.All(items, i => Assert.Equal(["ā"], Assert.IsType<IrScalar>(i.Type).Facets!.Values!.Items.Select(v => v.Text)));
    }

    [Fact]
    public void Coalesce_case_and_isnull_with_an_empty_array_keep_the_arrays_items()
    {
        // Observed: Under_json_query_coalesce_case_and_isnull_with_a_json_literal_give_the_json_of_the_branch_taken.
        var doc = Infer(Lines + """
            SELECT json_query(COALESCE(@lines, '[]')) AS a,
                   json_query(CASE WHEN @flag = 1 THEN (SELECT l.sku, l.qty FROM sales.order_line AS l FOR JSON PATH) ELSE '[]' END) AS b,
                   json_query(ISNULL(@lines, '[]')) AS c,
                   json_query(IIF(@flag = 1, @lines, N'[]')) AS d
            FOR JSON PATH;
            """);

        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            var field = Field(doc, name);
            AssertLines(doc, field.Type);
            Assert.Null(Assert.IsType<IrArray>(field.Type).MinItems);
        }

        Assert.False(Field(doc, "a").Nullable);
        Assert.False(Field(doc, "c").Nullable);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(Field(doc, "b")));
        Assert.Equal([IrNullRule.EmptySubquery], Rules(Field(doc, "d")));
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Isnull_writes_any_json_literal_fallback_as_json()
    {
        var doc = Infer("""SELECT ISNULL((SELECT l.qty FROM sales.order_line AS l FOR JSON PATH), '{"none":true}') AS x FOR JSON PATH;""");
        var x = Assert.IsType<IrUnion>(Field(doc, "x").Type);
        Assert.Equal(2, x.Variants.Count);
        Assert.Empty(doc.Unknowns);
    }
}

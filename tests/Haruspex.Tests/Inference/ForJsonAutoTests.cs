using System.Text;
using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// <c>FOR JSON AUTO</c> and <c>ROOT</c>: each rule <c>Typer.ForJsonAuto.cs</c> follows, held to the observation it rests on
/// (<c>ForJsonObservationTests.Auto.cs</c> in the differential tests).
/// </summary>
/// <remarks>
/// A shape is written on one line: an object's keys in order, <c>?</c> after a key left out when null, <c>~</c> after one always
/// written that may be null, a nested object or array after a colon, and an array's bounds after it (<c>1..</c>, <c>1..1</c>).
/// </remarks>
public class ForJsonAutoTests
{
    private const string Tables = """
        CREATE TABLE dbo.orders (id int NOT NULL PRIMARY KEY, placed_on date NOT NULL, note nvarchar(10) NULL);
        GO
        CREATE TABLE dbo.lines (id int NOT NULL PRIMARY KEY, order_id int NOT NULL, sku varchar(5) NOT NULL);
        GO
        CREATE TABLE dbo.notes (id int NOT NULL PRIMARY KEY, order_id int NOT NULL, body nvarchar(5) NULL);
        GO
        CREATE VIEW dbo.order_view AS SELECT o.id, o.placed_on FROM dbo.orders AS o;
        GO
        CREATE FUNCTION dbo.lines_of (@o int) RETURNS TABLE AS RETURN SELECT l.sku FROM dbo.lines AS l WHERE l.order_id = @o;
        GO
        """;

    private const string Joined = "FROM dbo.orders AS o JOIN dbo.lines AS l ON l.order_id = o.id";

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("*.*", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p @x xml = NULL AS\n{body}"));

    /// <summary>The first result's shape, on one line, after checking nothing is unknown.</summary>
    private static string Shape(string body)
    {
        var doc = Procedure(body);
        AllTyped(doc);
        return Render(doc, Results(doc)[0].Type);
    }

    private static string Render(IrDocument doc, IrType type) => type switch
    {
        IrArray array => $"[{Render(doc, array.Items)}]" + (array.MinItems is null && array.MaxItems is null ? "" : $"{array.MinItems}..{array.MaxItems}"),
        IrRef reference => "{" + string.Join(", ", doc.Types[reference.Name].Fields.Select(f => Field(doc, f))) + "}",
        IrJsonText { Content: { } content } => $"text({Render(doc, content)})",
        _ => "",
    };

    private static string Field(IrDocument doc, IrField field)
    {
        var text = new StringBuilder(field.Name);
        if (field.Presence == IrPresence.OmittedWhenNull)
            text.Append('?');
        else if (field.Nullable)
            text.Append('~');
        var nested = Render(doc, field.Type);
        if (nested.Length > 0)
            text.Append(':').Append(nested);
        return text.ToString();
    }

    // ---- nesting --------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_second_tables_columns_nest_in_an_array_of_at_least_one_object_keyed_by_its_alias() =>
        Assert.Equal("[{id, placed_on, l:[{sku}]1..}]", Shape($"SELECT o.id, o.placed_on, l.sku {Joined} FOR JSON AUTO;"));

    [Theory]
    [InlineData("o.id, n.body, l.sku", "[{id, n:[{body?, l:[{sku}]1..}]1..}]")]
    [InlineData("o.id, l.sku, n.body", "[{id, l:[{sku, n:[{body?}]1..}]1..}]")]
    [InlineData("l.sku, o.id", "[{sku, o:[{id}]1..}]")]
    public void Tables_nest_in_a_chain_in_the_order_their_first_columns_are_selected(string columns, string expected) =>
        Assert.Equal(expected, Shape($"SELECT {columns} {Joined} JOIN dbo.notes AS n ON n.order_id = o.id FOR JSON AUTO;"));

    [Fact]
    public void A_tables_columns_are_gathered_into_its_object_and_the_nested_array_is_its_last_key() =>
        Assert.Equal("[{id, placed_on, l:[{sku, oid}]1..}]", Shape($"SELECT o.id, l.sku, o.placed_on, l.order_id AS oid {Joined} FOR JSON AUTO;"));

    [Fact]
    public void Two_aliases_of_one_table_are_two_levels() =>
        Assert.Equal("[{id, b:[{bid}]1..}]", Shape("SELECT a.id, b.id AS bid FROM dbo.orders AS a JOIN dbo.orders AS b ON b.id = a.id FOR JSON AUTO;"));

    [Fact]
    public void One_table_is_one_object_per_row() =>
        Assert.Equal("[{id, note?}]", Shape("SELECT o.id, o.note FROM dbo.orders AS o FOR JSON AUTO;"));

    [Fact]
    public void A_nested_array_has_exactly_one_object_when_the_query_returns_at_most_one_row() =>
        Assert.Equal("[{id, l:[{sku}]1..1}]", Shape($"SELECT TOP (1) o.id, l.sku {Joined} FOR JSON AUTO;"));

    // ---- rows that don't match ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("FOR JSON AUTO", "[{id, l:[{sku?}]1..}]")]
    [InlineData("FOR JSON AUTO, INCLUDE_NULL_VALUES", "[{id, l:[{sku~}]1..}]")]
    public void An_unmatched_child_is_still_one_object_whose_keys_may_be_left_out_or_null(string clause, string expected) =>
        Assert.Equal(expected, Shape($"SELECT o.id, l.sku FROM dbo.orders AS o LEFT JOIN dbo.lines AS l ON l.order_id = o.id {clause};"));

    [Fact]
    public void An_unmatched_parents_keys_may_be_left_out() =>
        Assert.Equal("[{id?, l:[{sku}]1..}]", Shape("SELECT o.id, l.sku FROM dbo.orders AS o RIGHT JOIN dbo.lines AS l ON l.order_id = o.id FOR JSON AUTO;"));

    [Fact]
    public void An_unmatched_outer_apply_is_one_object_too() =>
        Assert.Equal("[{id, f:[{sku?}]1..}]", Shape("SELECT o.id, f.sku FROM dbo.orders AS o OUTER APPLY dbo.lines_of(o.id) AS f FOR JSON AUTO;"));

    // ---- columns of no table --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1 AS e, o.id, l.sku", "[{e, id, l:[{sku}]1..}]")]
    [InlineData("o.id, 1 AS e, l.sku", "[{id, e, l:[{sku}]1..}]")]
    [InlineData("o.id, l.sku, 1 AS e", "[{id, l:[{sku, e}]1..}]")]
    [InlineData("o.id, l.sku, o.placed_on, 1 AS e", "[{id, placed_on, l:[{sku, e}]1..}]")]
    [InlineData("o.id, upper(l.sku) AS u", "[{id, u}]")]
    [InlineData("upper(l.sku) AS u, o.id", "[{u, id}]")]
    [InlineData("o.id, CAST(l.id AS bigint) AS lid", "[{id, lid}]")]
    [InlineData("o.id, l.sku COLLATE Latin1_General_BIN AS s", "[{id, s}]")]
    [InlineData("o.id, (l.sku) COLLATE Latin1_General_BIN AS s", "[{id, s}]")]
    [InlineData("o.id, CASE WHEN l.id > 0 THEN 1 END AS big", "[{id, big?}]")]
    [InlineData("o.id, (l.sku) AS s", "[{id, l:[{s}]1..}]")]
    [InlineData("o.id, ((l.sku)) AS s", "[{id, l:[{s}]1..}]")]
    [InlineData("o.id, sku", "[{id, l:[{sku}]1..}]")]
    public void A_column_of_no_table_joins_the_deepest_object_opened_before_it(string columns, string expected) =>
        Assert.Equal(expected, Shape($"SELECT {columns} {Joined} FOR JSON AUTO;"));

    [Fact]
    public void A_column_of_the_enclosing_query_is_a_column_of_no_table() =>
        Assert.Equal(
            "[{j?:[{sku, id}]1..}]",
            Shape("SELECT (SELECT l.sku, o.id FROM dbo.lines AS l WHERE l.order_id = o.id FOR JSON AUTO) AS j FROM dbo.orders AS o FOR JSON PATH;"));

    [Fact]
    public void A_star_nests_each_column_under_its_own_table() =>
        Assert.Equal("[{id, l:[{id, order_id, sku}]1..}]", Shape($"SELECT o.id, l.* {Joined} FOR JSON AUTO;"));

    // ---- which sources are tables ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("CROSS APPLY (SELECT l.sku FROM dbo.lines AS l WHERE l.order_id = o.id) AS x", "x.sku", "[{id, x:[{sku}]1..}]")]
    [InlineData("CROSS JOIN (VALUES (5)) AS x (sku)", "x.sku", "[{id, x:[{sku}]1..}]")]
    [InlineData("JOIN dbo.order_view AS x ON x.id = o.id", "x.placed_on", "[{id, x:[{placed_on}]1..}]")]
    [InlineData("CROSS APPLY dbo.lines_of(o.id) AS x", "x.sku", "[{id, x:[{sku}]1..}]")]
    [InlineData("CROSS APPLY OPENJSON(N'[{\"v\":1}]') WITH (v int) AS x", "x.v", "[{id, v}]")]
    [InlineData("CROSS APPLY STRING_SPLIT(N'a', N',') AS x", "x.value", "[{id, value}]")]
    [InlineData("CROSS APPLY GENERATE_SERIES(7, 7) AS x", "x.value", "[{id, value}]")]
    public void Derived_tables_values_views_and_functions_are_tables_and_built_in_rowset_functions_are_not(string source, string column, string expected) =>
        Assert.Equal(expected, Shape($"SELECT o.id, {column} FROM dbo.orders AS o {source} FOR JSON AUTO;"));

    [Fact]
    public void A_cte_a_table_variable_and_a_temporary_table_are_tables() =>
        Assert.Equal(
            "[{id, c:[{sku, @v:[{k, #t:[{n}]1..}]1..}]1..}]",
            Shape("""
                DECLARE @v TABLE (k int NOT NULL);
                CREATE TABLE #t (n int NOT NULL);
                WITH c AS (SELECT l.order_id, l.sku FROM dbo.lines AS l)
                SELECT o.id, c.sku, [@v].k, #t.n FROM dbo.orders AS o JOIN c ON c.order_id = o.id CROSS JOIN @v CROSS JOIN #t FOR JSON AUTO;
                """));

    [Theory]
    [InlineData("SELECT o.id, lines.sku FROM dbo.orders AS o JOIN lines ON lines.order_id = o.id FOR JSON AUTO;", "lines")]
    [InlineData("SELECT o.id, lines.sku FROM dbo.orders AS o JOIN dbo.lines ON lines.order_id = o.id FOR JSON AUTO;", "dbo.lines")]
    [InlineData("SELECT o.id, lines.sku FROM dbo.orders AS o JOIN [DBO].[Lines] ON lines.order_id = o.id FOR JSON AUTO;", "DBO.Lines")]
    [InlineData("SELECT o.id, order_view.placed_on FROM dbo.orders AS o JOIN dbo.order_view ON order_view.id = o.id FOR JSON AUTO;", "dbo.order_view")]
    [InlineData("SELECT o.id, lines_of.sku FROM dbo.orders AS o CROSS APPLY dbo.lines_of(o.id) FOR JSON AUTO;", "dbo.lines_of")]
    [InlineData("WITH c AS (SELECT l.sku FROM dbo.lines AS l) SELECT o.id, c.sku FROM dbo.orders AS o CROSS JOIN c FOR JSON AUTO;", "c")]
    public void A_source_without_an_alias_is_keyed_by_its_name_as_the_from_clause_writes_it(string body, string key)
    {
        var doc = Procedure(body);

        AllTyped(doc);
        Assert.Equal(["id", key], Names(doc, Results(doc)[0].Type));
    }

    // ---- keys -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_dotted_alias_is_one_key() =>
        Assert.Equal("[{a.b, l:[{c.d}]1..}]", Shape($"SELECT o.id AS [a.b], l.sku AS [c.d] {Joined} FOR JSON AUTO;"));

    [Theory]
    [InlineData("o.id, o.id")]
    [InlineData("o.id AS l, l.sku")]
    [InlineData("o.id, l.sku, 1 AS sku")]
    public void A_repeated_key_makes_the_result_unknown(string columns) =>
        AssertUnknown(Procedure($"SELECT {columns} {Joined} FOR JSON AUTO;"), "results[0]", IrUnknownReason.DuplicateColumn);

    [Fact]
    public void Keys_that_differ_only_in_case_are_two_keys() =>
        Assert.Equal("[{id, ID, l:[{sku}]1..}]", Shape($"SELECT o.id, o.id AS ID, l.sku {Joined} FOR JSON AUTO;"));

    [Fact]
    public void Without_array_wrapper_leaves_out_only_the_outermost_array() =>
        Assert.Equal("{id, l:[{sku}]1..}", Shape($"SELECT o.id, l.sku {Joined} FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER;"));

    // ---- what stays unknown ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 1 AS a FOR JSON AUTO;")]
    [InlineData("SELECT x.v FROM OPENJSON(N'[1]') WITH (v int '$') AS x FOR JSON AUTO;")]
    public void A_from_without_a_table_is_refused_so_the_result_is_unknown(string body)
    {
        var unknown = Unknown(Procedure(body), "results[0]");

        Assert.Equal(IrUnknownReason.UnsupportedQuery, unknown.Reason);
        Assert.Contains("13600", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_from_with_a_table_is_enough_though_no_column_reads_it() =>
        Assert.Equal("[{a}]", Shape("SELECT 1 AS a FROM dbo.orders AS o FOR JSON AUTO;"));

    [Fact]
    public void For_json_auto_over_a_set_operation_is_unknown() =>
        AssertUnknown(
            Procedure("SELECT o.id FROM dbo.orders AS o UNION ALL SELECT l.id FROM dbo.lines AS l FOR JSON AUTO;"),
            "results[0]",
            IrUnknownReason.UnsupportedQuery);

    [Fact]
    public void A_column_of_a_source_whose_nesting_was_not_observed_makes_the_result_unknown() =>
        AssertUnknown(
            Procedure("SELECT o.id, k.[RANK] FROM dbo.orders AS o JOIN CONTAINSTABLE(dbo.orders, note, N'x') AS k ON k.[KEY] = o.id FOR JSON AUTO;"),
            "results[0]",
            IrUnknownReason.UnsupportedQuery);

    [Theory]
    [InlineData("SELECT n.c.value('.', 'int') AS v FROM @x.nodes('/a') AS n (c) FOR JSON AUTO;", "[{v?}]")]
    [InlineData("SELECT o.id, n.c.value('.', 'int') AS v FROM dbo.orders AS o CROSS APPLY @x.nodes('/a') AS n (c) FOR JSON AUTO;", "[{id, v?}]")]
    public void An_xml_nodes_source_is_a_table_whose_column_is_never_bare(string body, string expected) =>
        Assert.Equal(expected, Shape(body));

    [Fact]
    public void Openxml_is_a_table() =>
        Assert.Equal("[{id, x:[{v?}]1..}]", Shape("DECLARE @h int = 1; SELECT o.id, x.v FROM dbo.orders AS o CROSS JOIN OPENXML(@h, N'/r/a') WITH (v int) AS x FOR JSON AUTO;"));

    [Fact]
    public void A_pseudo_column_makes_the_result_unknown() =>
        AssertUnknown(Procedure($"SELECT o.id, l.$IDENTITY AS i {Joined} FOR JSON AUTO;"), "results[0]", IrUnknownReason.UnsupportedQuery);

    // ---- subqueries -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("", "[{k, j?:[{id, l:[{sku}]1..}]1..}]")]
    [InlineData(", WITHOUT_ARRAY_WRAPPER", "[{k, j?:text({id, l:[{sku}]1..})}]")]
    public void A_for_json_auto_subquery_nests_as_json_with_its_wrapper_and_as_text_without(string option, string expected) =>
        Assert.Equal(expected, Shape($"SELECT 1 AS k, (SELECT o.id, l.sku {Joined} FOR JSON AUTO{option}) AS j FOR JSON PATH;"));

    // ---- ROOT -----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("FOR JSON PATH, ROOT", "{root:[{id}]1..}")]
    [InlineData("FOR JSON PATH, ROOT('orders')", "{orders:[{id}]1..}")]
    [InlineData("FOR JSON PATH, ROOT(N'a.b')", "{a.b:[{id}]1..}")]
    [InlineData("FOR JSON AUTO, ROOT('orders')", "{orders:[{id}]1..}")]
    public void Root_wraps_an_array_of_at_least_one_object_in_an_object_whose_one_key_is_root_or_the_name_given(string clause, string expected) =>
        Assert.Equal(expected, Shape($"SELECT o.id FROM dbo.orders AS o {clause};"));

    [Fact]
    public void A_root_document_over_at_most_one_row_has_exactly_one_object() =>
        Assert.Equal("{r:[{id}]1..1}", Shape("SELECT o.id FROM dbo.orders AS o WHERE o.id = 1 FOR JSON PATH, ROOT('r');"));

    [Fact]
    public void A_root_document_may_be_absent_unless_the_query_always_returns_one_row()
    {
        var some = Assert.Single(Results(Procedure("SELECT o.id FROM dbo.orders AS o FOR JSON PATH, ROOT('r');")));
        var one = Assert.Single(Results(Procedure("SELECT COUNT(*) AS n FROM dbo.orders AS o FOR JSON PATH, ROOT('r');")));

        Assert.Equal(IrNullRule.NoRows, Assert.Single(some.AbsentBecause).Rule);
        Assert.False(one.MayBeAbsent);
    }

    [Fact]
    public void A_root_subquery_nests_as_json() =>
        Assert.Equal("[{j?:{r:[{id}]1..}}]", Shape("SELECT (SELECT o.id FROM dbo.orders AS o FOR JSON PATH, ROOT('r')) AS j FOR JSON PATH;"));

    [Fact]
    public void Json_query_reads_into_a_root_subquery() =>
        Assert.Equal("[{j?:[{id}]1..}]", Shape("SELECT json_query((SELECT o.id FROM dbo.orders AS o FOR JSON PATH, ROOT('r')), '$.r') AS j FOR JSON PATH;"));
}

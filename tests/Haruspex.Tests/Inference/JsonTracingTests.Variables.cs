using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class JsonTracingTests
{
    [Fact]
    public void A_variable_holding_built_json_is_text_of_its_content_until_json_query_makes_it_live()
    {
        // Observed: Json_built_here_keeps_its_content_through_variables_and_tables_until_json_query.
        var doc = Infer(Lines + "SELECT @lines AS t, json_query(@lines) AS lines FOR JSON PATH;");

        AssertLines(doc, Assert.IsType<IrJsonText>(Field(doc, "t").Type).Content!);
        var lines = Field(doc, "lines");
        AssertLines(doc, lines.Type);
        Assert.Equal(1, Assert.IsType<IrArray>(lines.Type).MinItems);

        // FOR JSON over no rows is NULL, so the key is left out.
        Assert.Equal([IrNullRule.EmptySubquery], Rules(lines));
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_variable_assigned_by_a_select_over_rows_holds_the_json_or_its_value_before()
    {
        // Observed: an assignment by a SELECT that matches no rows does not happen.
        var doc = Infer(Lines + """
            DECLARE @summary nvarchar(max), @kept nvarchar(max) = json_object('total': 0, 'items': json_query('[]'));
            SELECT @summary = json_object('total': o.total, 'items': json_query(@lines)),
                   @kept = json_object('total': o.total, 'items': json_query(@lines))
            FROM sales.[order] AS o WHERE o.order_id = @id;
            SELECT json_query(@summary) AS summary, json_query(@kept) AS kept FOR JSON PATH;
            """);

        var summary = Field(doc, "summary");
        Assert.Equal([IrNullRule.Variable], Rules(summary));
        Assert.Equal(["total", "items"], Fields(doc, summary.Type).Select(f => f.Name));

        // @kept is its first value or the select's: never null, its items an empty tuple or the lines, so an array of lines.
        var kept = Field(doc, "kept");
        Assert.False(kept.Nullable);
        var items = Sub(doc, kept.Type, "items");
        AssertLines(doc, items.Type);
        Assert.Null(Assert.IsType<IrArray>(items.Type).MinItems);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Assignments_on_several_paths_join_their_json()
    {
        var doc = Infer("""
            DECLARE @x nvarchar(max);
            IF @flag = 1 SET @x = json_object('kind': 'a', 'n': 1);
            ELSE SET @x = json_object('kind': 'b', 'n': 2);
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        var x = Field(doc, "x");
        Assert.False(x.Nullable);
        Assert.Equal(["kind", "n"], Fields(doc, x.Type).Select(f => f.Name));
        Assert.All(Fields(doc, x.Type), f => Assert.False(f.Nullable));
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("SET @x = @raw;", "@raw")]
    [InlineData("SET @x = json_modify(@x, '$.n', 2);", "json_modify")]
    [InlineData("SET @x = @x + N'';", "@x")]
    [InlineData("SELECT @x = o.notes FROM sales.[order] AS o WHERE o.order_id = @id;", "o.notes")]
    [InlineData("SET @x = '';", "not valid JSON")]
    public void An_assignment_that_may_not_be_json_built_here_makes_its_content_unknown_naming_it(string assignment, string named)
    {
        var doc = Infer($"""
            DECLARE @x nvarchar(max) = json_object('n': 1);
            IF @flag = 1 {assignment}
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        var unknown = Unknown(doc, "x");
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, unknown.Reason);
        Assert.Contains(named, unknown.Detail, StringComparison.Ordinal);
        Assert.StartsWith("p.sql:3:", unknown.At, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nvarchar(4000)")]
    [InlineData("varchar(max)")]
    public void A_variable_of_another_type_is_not_traced(string type)
    {
        // Observed: a shorter type cuts the text short without an error; varchar changes what its code page lacks.
        var doc = Infer($"DECLARE @x {type} = json_object('n': 1);\nSELECT json_query(@x) AS x FOR JSON PATH;");
        Assert.Contains("not built here", Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_variable_in_a_loop_reaches_a_fixed_point_over_its_json()
    {
        var doc = Infer("""
            DECLARE @x nvarchar(max) = '[]', @n int = 0;
            WHILE @n < 3
            BEGIN
                SET @x = (SELECT l.sku FROM sales.order_line AS l WHERE l.order_id = @n FOR JSON PATH);
                SET @n += 1;
            END
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        var x = Assert.IsType<IrArray>(Field(doc, "x").Type);
        Assert.Null(x.MinItems);
        Assert.Equal(["sku"], Fields(doc, x.Items).Select(f => f.Name));
    }

    [Fact]
    public void Json_that_grows_in_a_loop_is_widened_and_the_loop_settles()
    {
        // Without widening the loop would reach no fixed point, and the procedure's results would be unknown.
        var doc = Infer("""
            DECLARE @x nvarchar(max) = N'[]', @n int = 0;
            DECLARE @t TABLE (j nvarchar(max));
            INSERT INTO @t (j) VALUES (N'[]');
            WHILE @n < 3
            BEGIN
                SET @x = (SELECT json_query(@x) AS c FOR JSON PATH);
                INSERT INTO @t (j) SELECT (SELECT json_query(t.j) AS c FROM @t AS t FOR JSON PATH);
                SET @n += 1;
            END
            SELECT json_query(@x) AS x, @n AS n, (SELECT json_query(t.j) AS j FROM @t AS t FOR JSON PATH) AS t FOR JSON PATH;
            """);

        Assert.Equal(IrScalarKind.Number, Assert.IsType<IrScalar>(Field(doc, "n").Type).Kind);
        Assert.Contains("loop", Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_variable_assigned_coalesce_over_json_holds_its_content_whether_or_not_the_server_folds_it()
    {
        // Observed: Assigned_to_a_variable_coalesce_and_case_over_json_are_the_same_text_whether_or_not_the_server_folds_them.
        // Nested in FOR JSON the folding decides whether it is JSON or escaped text; in a variable it is text either way.
        var doc = Infer("""
            DECLARE @qty nvarchar(max) = COALESCE((SELECT json_arrayagg(l.qty) FROM sales.order_line AS l WHERE l.order_id = @id), json_array());
            SELECT json_query(@qty) AS qty;
            """);

        // In a rowset JSON is a string holding it; the subquery's array, or json_array()'s empty one, so with no least count.
        var items = Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "qty").Type).Content);
        Assert.Null(items.MinItems);
        Assert.Equal(IrScalarKind.Number, Assert.IsType<IrScalar>(items.Items).Kind);
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("SET @x = CASE WHEN @flag = 1 THEN json_object('n': 1) ELSE json_object('n': 2) END;")]
    [InlineData("SELECT @x = IIF(@flag = 1, json_object('n': 1), json_object('n': 2));")]
    [InlineData("SELECT @x = COALESCE(json_query(@x), json_object('n': 2)) WHERE @flag = 1;")]
    public void A_variable_assigned_case_or_coalesce_over_live_json_holds_the_join_of_its_results(string assignment)
    {
        var doc = Infer($"""
            DECLARE @x nvarchar(max) = json_object('n': 0);
            {assignment}
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        Assert.Equal(["n"], Fields(doc, Field(doc, "x").Type).Select(f => f.Name));
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Coalesce_over_json_nested_in_for_json_stays_unknown_even_on_its_way_to_a_variable()
    {
        // Embedded in an outer FOR JSON, how it nests depends on where it is: text in FOR JSON, but JSON in json_object, where
        // its last argument is JSON built here (Coalesce_over_json_built_from_rows_is_text_wherever_it_nests_at_every_level).
        var doc = Infer("""
            DECLARE @x nvarchar(max) = (SELECT COALESCE((SELECT json_arrayagg(l.qty) FROM sales.order_line AS l WHERE l.order_id = @id), json_array()) AS qty FOR JSON PATH);
            SELECT json_query(@x) AS x FOR JSON PATH;
            """);

        Assert.Contains("json_object", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }
}

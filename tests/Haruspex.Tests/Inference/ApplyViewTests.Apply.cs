using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class ApplyViewTests
{
    [Fact]
    public void Cross_apply_keeps_the_right_sides_own_nullability()
    {
        // Cross_apply_keeps_only_outer_rows_with_a_right_row_and_outer_apply_fills_the_others_with_null: CROSS APPLY never
        // null-extends, so a right-side column is null only when its own value can be.
        var doc = Procedure("SELECT t.id, x.note, x.label FROM dbo.t AS t CROSS APPLY (SELECT u.note, u.label FROM dbo.u AS u WHERE u.t_id = t.id) AS x;");

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "note"));
        Assert.Empty(Rules(doc, "label"));
    }

    [Fact]
    public void Outer_apply_makes_every_right_side_column_nullable()
    {
        var doc = Procedure("SELECT t.id, x.note, x.label FROM dbo.t AS t OUTER APPLY (SELECT u.note, u.label FROM dbo.u AS u WHERE u.t_id = t.id) AS x;");

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
        Assert.Equal([IrNullRule.NullableColumn, IrNullRule.OuterApply], Rules(doc, "note"));
        Assert.Equal([IrNullRule.OuterApply], Rules(doc, "label"));
        Assert.Equal("p.sql:2:58", Field(doc, "label").NullableBecause[0].At);
    }

    [Fact]
    public void Outer_apply_of_a_table_makes_its_columns_nullable()
    {
        var doc = Procedure("SELECT t.id, u.label FROM dbo.t AS t OUTER APPLY dbo.u AS u;");

        Typed(doc);
        Assert.Equal([IrNullRule.OuterApply], Rules(doc, "label"));
    }

    [Theory]
    [InlineData("CROSS")]
    [InlineData("OUTER")]
    public void An_ungrouped_aggregate_applied_is_one_row_so_its_columns_keep_their_own_nullability(string apply)
    {
        // An_ungrouped_aggregate_applied_gives_one_row_for_every_outer_row.
        var doc = Procedure($"SELECT t.id, a.n, a.m FROM dbo.t AS t {apply} APPLY (SELECT COUNT_BIG(*) AS n, MAX(u.label) AS m FROM dbo.u AS u WHERE u.t_id = t.id) AS a;");

        Typed(doc);
        Assert.Empty(Rules(doc, "n"));
        Assert.Equal([IrNullRule.EmptyAggregate], Rules(doc, "m"));
    }

    [Fact]
    public void A_grouped_aggregate_applied_may_have_no_row()
    {
        // A_grouped_aggregate_applied_gives_no_row_for_an_outer_row_without_rows.
        var doc = Procedure("SELECT t.id, a.n FROM dbo.t AS t OUTER APPLY (SELECT COUNT_BIG(*) AS n FROM dbo.u AS u WHERE u.t_id = t.id GROUP BY ()) AS a;");

        Assert.Equal([IrNullRule.OuterApply], Rules(doc, "n"));
    }

    [Fact]
    public void An_aggregate_applied_with_having_may_have_no_row()
    {
        var doc = Procedure("SELECT t.id, a.n FROM dbo.t AS t OUTER APPLY (SELECT COUNT_BIG(*) AS n FROM dbo.u AS u WHERE u.t_id = t.id HAVING COUNT_BIG(*) > 1) AS a;");

        Assert.Equal([IrNullRule.OuterApply], Rules(doc, "n"));
    }

    [Theory]
    [InlineData("OUTER", "")]
    [InlineData("CROSS", "")]
    [InlineData("OUTER", " FOR JSON PATH")]
    public void A_nested_payload_built_by_apply_is_typed(string apply, string forJson)
    {
        // A_for_json_subquery_applied_without_from_is_one_row_whose_json_is_null_when_there_are_no_rows: the applied query
        // has no FROM, so it is one row and never null-extended; the payload is null only when the FOR JSON subquery is empty.
        var doc = Procedure($"""
            SELECT t.id, l.lines
            FROM dbo.t AS t
            {apply} APPLY (SELECT JSON_QUERY((SELECT u.id, u.label FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH)) AS lines) AS l{forJson};
            """);

        Typed(doc);
        var lines = Field(doc, "lines");
        Assert.Equal([IrNullRule.EmptySubquery], lines.NullableBecause.Select(r => r.Rule));
        var items = forJson.Length > 0 ? Assert.IsType<IrArray>(lines.Type) : Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(lines.Type).Content);
        Assert.Equal(["id", "label"], Fields(doc, items).Select(f => f.Name));
    }

    [Fact]
    public void Outer_apply_inside_an_outer_joins_optional_side_is_null_for_the_outer_join()
    {
        var doc = Procedure("SELECT t.id, x.label FROM dbo.t AS t LEFT JOIN (dbo.pair AS p OUTER APPLY (SELECT u.label FROM dbo.u AS u WHERE u.t_id = p.a) AS x) ON p.a = t.id;");

        Typed(doc);
        Assert.Equal([IrNullRule.OuterJoin], Rules(doc, "label"));
    }

    [Fact]
    public void A_filter_inside_the_applied_query_does_not_refine_the_outer_columns()
    {
        // As for a correlated subquery: the filter holds for the applied query's rows, not for the outer row read outside it.
        var doc = Procedure("SELECT t.name FROM dbo.t AS t CROSS APPLY (SELECT u.note FROM dbo.u AS u WHERE t.name IS NOT NULL AND u.t_id = t.id) AS x;");

        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "name"));
    }

    [Fact]
    public void An_outer_column_read_in_an_applied_aggregate_is_not_refined_by_its_where()
    {
        // An_outer_column_read_in_an_applied_aggregate_is_not_filtered_by_its_where (refinement's rule for aggregate subqueries).
        var doc = Procedure("SELECT a.tn FROM dbo.t AS t CROSS APPLY (SELECT t.name AS tn, COUNT_BIG(*) AS n FROM dbo.u AS u WHERE t.name IS NOT NULL AND u.t_id = t.id) AS a;");

        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "tn"));
    }

    [Fact]
    public void A_filter_inside_the_applied_query_refines_its_own_columns()
    {
        var doc = Procedure("SELECT x.note FROM dbo.t AS t CROSS APPLY (SELECT u.note FROM dbo.u AS u WHERE u.note IS NOT NULL AND u.t_id = t.id) AS x;");

        Assert.Empty(Rules(doc, "note"));
    }

    [Fact]
    public void A_where_on_the_right_side_proves_the_outer_apply_matched()
    {
        var doc = Procedure("SELECT x.label, x.note FROM dbo.t AS t OUTER APPLY (SELECT u.note, u.label FROM dbo.u AS u WHERE u.t_id = t.id) AS x WHERE x.label = N'a';");

        Assert.Empty(Rules(doc, "label"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "note"));
    }

    [Fact]
    public void The_applied_query_reads_its_left_inputs_joined_sources_and_a_derived_table_inside_it_does_too()
    {
        // An_applied_query_sees_its_left_input_only.
        var doc = Procedure("SELECT z.code, z.label FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id CROSS APPLY (SELECT d.code, u.label FROM (SELECT t.code) AS d) AS z;");

        Typed(doc);
        Assert.Empty(Rules(doc, "code"));
        Assert.Empty(Rules(doc, "label"));
    }

    [Fact]
    public void The_applied_query_does_not_see_a_source_listed_before_its_left_input()
    {
        // The query fails with 4104; nothing is claimed of a column it cannot bind.
        var doc = Procedure("SELECT z.code FROM dbo.t AS t, dbo.u AS u CROSS APPLY (SELECT t.code) AS z;");

        Assert.Equal(IrUnknownReason.UnresolvedColumn, Unknown(doc, "results[0][].code").Reason);
    }

    [Fact]
    public void Apply_of_a_table_valued_function_is_typed_from_its_body()
    {
        // TableFunctionTests has the rules.
        var doc = Infer(
            ("tables.sql", Tables),
            ("f.sql", "CREATE FUNCTION dbo.f(@id int) RETURNS TABLE AS RETURN SELECT u.label FROM dbo.u AS u WHERE u.t_id = @id;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.id, f.label FROM dbo.t AS t CROSS APPLY dbo.f(t.id) AS f;"));

        Typed(doc);
        Assert.Empty(Rules(doc, "label"));
    }

    [Theory]
    [InlineData("OUTER APPLY (SELECT TOP (1) u.label FROM dbo.u AS u WHERE u.t_id = t.id ORDER BY u.id) AS x", IrCardinality.AtMostOne)]
    [InlineData("CROSS APPLY (SELECT TOP (1) u.label FROM dbo.u AS u WHERE u.t_id = t.id ORDER BY u.id) AS x", IrCardinality.AtMostOne)]
    [InlineData("OUTER APPLY (SELECT COUNT_BIG(*) AS label FROM dbo.u AS u WHERE u.t_id = t.id) AS x", IrCardinality.AtMostOne)]
    [InlineData("CROSS APPLY (SELECT u.label FROM dbo.u AS u WHERE u.id = t.id) AS x", IrCardinality.AtMostOne)]
    [InlineData("OUTER APPLY (VALUES (t.code)) AS x (label)", IrCardinality.AtMostOne)]
    [InlineData("CROSS APPLY (SELECT u.label FROM dbo.u AS u WHERE u.t_id = t.id) AS x", IrCardinality.Many)]
    [InlineData("CROSS APPLY (VALUES (t.code), (t.code)) AS x (label)", IrCardinality.Many)]
    [InlineData("OUTER APPLY dbo.u AS x", IrCardinality.Many)]
    public void An_apply_keeps_the_outer_sides_bound_when_its_right_side_is_at_most_one_row(string apply, IrCardinality expected) =>
        Assert.Equal(expected, Rows(Procedure($"SELECT t.id, x.label FROM dbo.t AS t {apply} WHERE t.id = @id;", "@id int")));

    [Fact]
    public void An_apply_of_at_most_one_row_over_many_outer_rows_is_many() =>
        Assert.Equal(IrCardinality.Many, Rows(Procedure("SELECT t.id, x.label FROM dbo.t AS t OUTER APPLY (SELECT TOP (1) u.label FROM dbo.u AS u WHERE u.t_id = t.id) AS x;")));
}

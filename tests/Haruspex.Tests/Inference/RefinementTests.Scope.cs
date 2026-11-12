using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
    [Fact]
    public void A_subquerys_filter_does_not_refine_the_outer_querys_columns()
    {
        var doc = Procedure("SELECT t.name, (SELECT COUNT_BIG(*) FROM dbo.u AS u WHERE u.note = t.name) AS hits FROM dbo.t AS t;");

        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "name"));
    }

    [Fact]
    public void The_outer_querys_filter_refines_a_column_a_subquery_reads_from_it()
    {
        var doc = Procedure("SELECT t.id, (SELECT TOP (1) t.name FROM dbo.u AS u) AS name FROM dbo.t AS t WHERE t.name IS NOT NULL;");

        // The subquery may have no row, which is null; but not for want of a name.
        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "name"));
    }

    [Fact]
    public void Refinement_passes_through_a_cte_a_derived_table_and_a_temporary_table()
    {
        var doc = Procedure("""
            WITH named AS (SELECT t.id, t.name FROM dbo.t AS t WHERE t.name IS NOT NULL)
            SELECT n.name FROM named AS n;
            SELECT d.name FROM (SELECT t.name FROM dbo.t AS t WHERE t.name LIKE N'a%') AS d;
            SELECT t.id, t.name INTO #named FROM dbo.t AS t WHERE t.name > N'';
            SELECT x.name FROM #named AS x;
            """);

        Assert.All(Results(doc), r => Assert.False(Assert.Single(Fields(doc, r.Type)).Nullable));
    }

    [Fact]
    public void A_where_on_a_ctes_column_refines_it()
    {
        var doc = Procedure("""
            WITH c AS (SELECT t.id, t.name FROM dbo.t AS t)
            SELECT c.name FROM c WHERE c.name = N'x';
            """);

        Assert.False(Field(doc, "name").Nullable);
    }

    [Fact]
    public void A_derived_table_is_read_like_a_cte()
    {
        var doc = Procedure("SELECT d.id, d.label FROM (SELECT t.id, t.name AS label FROM dbo.t AS t) AS d;");

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal([new IrNullReason(IrNullRule.NullableColumn, "dbo.t.name")], Field(doc, "label").NullableBecause);
    }

    [Fact]
    public void A_derived_tables_column_list_names_its_columns()
    {
        var doc = Procedure("SELECT d.x FROM (SELECT t.id FROM dbo.t AS t) AS d (x);");

        Assert.False(Field(doc, "x").Nullable);
    }

    [Fact]
    public void A_derived_table_on_the_optional_side_is_null_extended()
    {
        var doc = Procedure("SELECT d.id FROM dbo.u AS u LEFT JOIN (SELECT t.id FROM dbo.t AS t) AS d ON d.id = u.t_id;");

        Assert.Equal([IrNullRule.OuterJoin], Rules(doc, "id"));
    }

    [Fact]
    public void An_aggregate_subquerys_filter_proves_nothing_of_an_outer_column_it_returns()
    {
        // An_aggregate_subquery_returns_its_row_even_when_its_where_rejects_the_outer_column: its one row comes even when WHERE
        // removed every row, so the outer t.n in it may be null.
        var doc = Procedure("""
            SELECT t.id,
                (SELECT t.n AS n, COUNT_BIG(*) AS c FROM dbo.u AS u WHERE u.t_id = t.n FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS stats,
                (SELECT t.n FROM dbo.u AS u WHERE u.t_id = t.n HAVING COUNT(*) >= 0) AS kept,
                (SELECT t.n FROM dbo.u AS u WHERE u.t_id = t.n) AS plain
            FROM dbo.t AS t FOR JSON PATH;
            """);

        var stats = Assert.IsType<IrJsonText>(Field(doc, "stats").Type);
        var n = Assert.Single(doc.Types[Assert.IsType<IrRef>(stats.Content).Name].Fields, f => f.Name == "n");
        Assert.Equal([IrNullRule.NullableColumn], Rules(n));
        Assert.Contains(IrNullRule.NullableColumn, Rules(doc, "kept"));

        // Without an aggregate, a row the subquery returns passed its WHERE.
        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "plain"));
    }

    [Fact]
    public void An_aggregate_subquerys_filter_still_refines_its_own_columns_and_having_the_outer_ones()
    {
        var doc = Procedure("""
            SELECT t.id,
                (SELECT MAX(u.note) FROM dbo.u AS u WHERE u.t_id = t.id AND u.note IS NOT NULL) AS note,
                (SELECT t.n FROM dbo.u AS u WHERE u.t_id = t.id HAVING t.n > 0) AS n
            FROM dbo.t AS t;
            """);

        Assert.Equal([IrNullRule.EmptyAggregate], Rules(doc, "note"));
        Assert.Equal([IrNullRule.EmptySubquery], Rules(doc, "n"));
    }

    [Theory]
    [InlineData("GROUP BY ROLLUP (t.n)")]
    [InlineData("GROUP BY CUBE (t.n)")]
    [InlineData("GROUP BY GROUPING SETS ((t.n), ())")]
    [InlineData("GROUP BY t.n WITH ROLLUP")]
    [InlineData("GROUP BY t.n WITH CUBE")]
    public void Super_aggregate_rows_make_grouped_columns_nullable_and_the_rows_many(string groupBy)
    {
        // Rollup_cube_and_grouping_sets_add_rows_whose_grouped_columns_are_null.
        var doc = Procedure($"SELECT t.n, t.id, COUNT_BIG(*) AS c, MAX(t.id) AS top_id FROM dbo.t AS t WHERE t.n IS NOT NULL AND t.id = @x {groupBy.Replace("t.n", "t.n, t.id", StringComparison.Ordinal)};", "@x int");

        Assert.Equal(IrCardinality.Many, Assert.Single(Results(doc)).Cardinality);
        Assert.Contains(IrNullRule.GroupingSet, Rules(doc, "n"));
        Assert.Contains(IrNullRule.GroupingSet, Rules(doc, "id"));
        Assert.False(Field(doc, "c").Nullable);
        Assert.False(Field(doc, "top_id").Nullable);
    }

    [Fact]
    public void A_plain_group_by_leaves_grouped_columns_as_they_are()
    {
        var doc = Procedure("SELECT t.n, COUNT_BIG(*) AS c FROM dbo.t AS t WHERE t.n IS NOT NULL GROUP BY t.n;");

        Assert.False(Field(doc, "n").Nullable);
    }
}

using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
    [Fact]
    public void Count_big_is_never_null_and_count_only_on_overflow()
    {
        // Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off.
        var doc = Procedure("SELECT COUNT(*) AS n, COUNT(t.name) AS named, COUNT_BIG(*) AS nb, COUNT_BIG(DISTINCT t.name) AS kinds FROM dbo.t AS t;");

        Assert.Equal([IrNullRule.CountOverflow], Rules(doc, "n"));
        Assert.Equal([IrNullRule.CountOverflow], Rules(doc, "named"));
        Assert.False(Field(doc, "nb").Nullable);
        Assert.False(Field(doc, "kinds").Nullable);
        Assert.Equal("int", Assert.IsType<IrScalar>(Field(doc, "n").Type).Sql);
        Assert.Equal("bigint", Assert.IsType<IrScalar>(Field(doc, "nb").Type).Sql);
    }

    [Fact]
    public void Min_and_max_keep_their_arguments_type_and_facets_and_are_null_over_no_rows_without_group_by()
    {
        // An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows (MIN and MAX are NULL there).
        var doc = Procedure("SELECT MAX(t.status) AS high, MIN(t.id) AS low FROM dbo.t AS t;");

        var high = Field(doc, "high");
        Assert.Equal([IrNullRule.NullableColumn, IrNullRule.EmptyAggregate], Rules(high));
        Assert.Equal(["open", "shut"], Assert.IsType<IrScalar>(high.Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal([IrNullRule.EmptyAggregate], Rules(doc, "low"));
    }

    [Fact]
    public void Under_group_by_max_is_null_only_when_its_argument_can_be()
    {
        // An_aggregate_with_group_by_returns_no_row_over_no_rows: a group's MAX is null only when every value in it is.
        var doc = Procedure("SELECT t.n, MAX(t.name) AS high, MAX(t.id) AS top_id FROM dbo.t AS t GROUP BY t.n;");

        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "high"));
        Assert.False(Field(doc, "top_id").Nullable);
    }

    [Fact]
    public void A_query_grouped_by_all_is_unknown()
    {
        // Group_by_all_returns_a_group_for_every_value_where_rejects_with_null_aggregates: its aggregates may be NULL over a column
        // never null, WHERE proves nothing of its grouped columns, and a keyed filter doesn't bound its rows. Until that is
        // modelled, every form that reads such a query is unknown: a result, a subquery, a derived table and an assignment.
        var doc = Procedure("""
            SELECT t.n, MAX(t.id) AS top_id FROM dbo.t AS t WHERE t.n IS NOT NULL GROUP BY ALL t.n;
            SELECT (SELECT COUNT_BIG(*) FROM dbo.t AS t WHERE t.id = 1 GROUP BY ALL t.id) AS c;
            SELECT d.top_id FROM (SELECT MAX(t.id) AS top_id FROM dbo.t AS t WHERE t.id = 1 GROUP BY ALL t.n) AS d;
            DECLARE @top int = 0;
            SELECT @top = MAX(t.id) FROM dbo.t AS t WHERE t.id = 1 GROUP BY ALL t.n;
            SELECT @top AS top_id;
            """);
        Assert.Equal(4, Results(doc).Count);
        for (var result = 0; result < 3; result++)
            Assert.True(Results(doc)[result].Type is IrUnknownType || Fields(doc, result).All(f => f.Type is IrUnknownType), $"result {result}");
        Assert.All(doc.Unknowns, u => Assert.Contains("GROUP BY ALL", u.Detail, StringComparison.Ordinal));

        // A variable keeps its declared type; what it was assigned is unknown, so it may be NULL for that.
        Assert.Contains(IrNullRule.Unknown, Rules(Field(doc, "top_id", 3)));
    }

    [Fact]
    public void A_where_refines_an_aggregates_argument()
    {
        var doc = Procedure("SELECT MAX(t.name) AS high FROM dbo.t AS t WHERE t.name IS NOT NULL GROUP BY t.n;");

        Assert.False(Field(doc, "high").Nullable);
    }

    [Fact]
    public void Having_refines_aggregates_and_grouped_columns()
    {
        // Having_over_no_groups_returns_no_row_and_without_group_by_at_most_one.
        var doc = Procedure("""
            SELECT t.n, MAX(t.name) AS high, MIN(t.name) AS low, COUNT(*) AS c FROM dbo.t AS t
            GROUP BY t.n HAVING MAX(t.name) > N'' AND t.n > 0 AND COUNT(*) > 1;
            """);

        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "high").Nullable);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "low"));
        Assert.False(Field(doc, "c").Nullable);
    }

    [Fact]
    public void Having_proves_an_aggregate_written_alike_and_no_other()
    {
        // An aggregate is matched by its text within its query (AggregateKey): the same text binds the same columns there, so
        // it is the same value. Written in another case, or with its column unqualified, it is the same aggregate to SQL
        // Server but another here, which loses the fact and claims less, never more.
        var doc = Procedure("""
            SELECT t.n, MAX(t.name) AS same, max(t.name) AS cased, MAX(name) AS bare FROM dbo.t AS t
            GROUP BY t.n HAVING MAX(t.name) > N'';
            """);

        Assert.False(Field(doc, "same").Nullable);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "cased"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "bare"));
    }

    [Fact]
    public void A_windowed_aggregate_not_modelled_is_unknown()
    {
        // MIN, MAX, SUM and COUNT over a window are read (FunctionTests), AVG and the statistics too
        // (NumericTests); CHECKSUM_AGG is not, with a window or without.
        var doc = Procedure("SELECT CHECKSUM_AGG(t.id) OVER () AS m FROM dbo.t AS t;");

        Assert.Equal(IrUnknownReason.UnsupportedExpression, Assert.Single(doc.Unknowns).Reason);
    }
}

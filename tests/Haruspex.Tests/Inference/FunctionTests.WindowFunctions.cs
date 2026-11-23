using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void Min_and_max_of_a_bit_are_unknown_with_or_without_a_window()
    {
        // Observed: Min_and_max_of_a_bit_are_refused_with_or_without_a_window (8117).
        var doc = Procedure("SELECT MIN(CAST(n AS bit)) AS a, MAX(CAST(n AS bit)) OVER () AS b, MAX(CAST(n AS bit)) AS c FROM dbo.t GROUP BY n;");
        Unknown(doc, "a");
        Unknown(doc, "b");
        Unknown(doc, "c");
    }

    [Fact]
    public void Ranking_is_bigint_and_never_null()
    {
        // Ranking_is_never_null_and_percentiles_are_null_only_over_all_null_input.
        var doc = Procedure("SELECT ROW_NUMBER() OVER (ORDER BY id) AS a, RANK() OVER (PARTITION BY g ORDER BY v) AS b, DENSE_RANK() OVER (ORDER BY v) AS c, NTILE(4) OVER (ORDER BY id) AS d FROM dbo.t;");
        Assert.Equal("bigint", Typed(doc, "a"));
        Assert.Equal("bigint", Typed(doc, "b"));
        Assert.Equal("bigint", Typed(doc, "c"));
        Assert.Equal("bigint", Typed(doc, "d"));
    }

    [Fact]
    public void An_aggregate_over_a_window_is_null_as_its_values_are_or_when_its_frame_may_be_empty()
    {
        // A_frame_that_may_leave_out_the_current_row_can_be_empty_and_give_null_over_a_never_null_column.
        var doc = Procedure("""
            SELECT COUNT(*) OVER () AS a, COUNT_BIG(v) OVER (PARTITION BY g) AS b, SUM(n) OVER (PARTITION BY g) AS c, SUM(v) OVER () AS d,
                   SUM(n) OVER (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING) AS e, MAX(s) OVER (ORDER BY id) AS f,
                   MIN(n) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING) AS g2, SUM(d) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS h,
                   COUNT(n) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS i, MAX(status) OVER () AS j, SUM(n) OVER (ORDER BY id ROWS 2 PRECEDING) AS k
            FROM dbo.t;
            """);

        // COUNT is an int, which over more than 2,147,483,647 rows would overflow, as for COUNT without a window.
        Assert.Equal("int count-overflow", Typed(doc, "a"));
        Assert.Equal("bigint", Typed(doc, "b"));
        Assert.Equal("int arithmetic-overflow", Typed(doc, "c"));
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("int arithmetic-overflow empty-frame", Typed(doc, "e"));
        Assert.Equal("varchar(10)", Typed(doc, "f"));
        Assert.Equal("int empty-frame", Typed(doc, "g2"));
        Assert.Equal("decimal(38,2)", Typed(doc, "h"));
        Assert.Equal("int count-overflow", Typed(doc, "i"));
        Assert.Equal("int arithmetic-overflow", Typed(doc, "k"));

        // MIN and MAX give one of the column's values, so its CHECK set holds.
        Assert.NotNull(((IrScalar)Field(doc, "j").Type).Facets?.Values);
    }

    [Fact]
    public void Lag_and_lead_are_null_past_the_edge_unless_a_default_never_null_is_given()
    {
        // Lag_and_lead_are_null_past_the_partitions_edge_unless_a_default_is_given_which_is_converted_to_the_columns_type.
        var doc = Procedure("""
            SELECT LAG(n) OVER (ORDER BY id) AS a, LAG(n, 1, 0) OVER (ORDER BY id) AS b, LEAD(s, 1, 'a longer default') OVER (ORDER BY id) AS c,
                   LAG(ti, 1, 1000) OVER (ORDER BY id) AS d, LAG(v, 2, 0) OVER (ORDER BY id) AS e, LAG(n, 1, v) OVER (ORDER BY id) AS f,
                   LAG(n, 1, NULL) OVER (ORDER BY id) AS g2, LAG(n, v) OVER (ORDER BY id) AS h, LAG(n, 0) OVER (ORDER BY id) AS i,
                   LAG(status) OVER (ORDER BY id) AS j, LAG(status, 1, 'none') OVER (ORDER BY id) AS k, LEAD(ti, 1, 1.9) OVER (ORDER BY id) AS l
            FROM dbo.t;
            """);
        Assert.Equal("int window-edge", Typed(doc, "a"));
        Assert.Equal("int", Typed(doc, "b"));
        Assert.Equal("varchar(10)", Typed(doc, "c"));
        Assert.Equal("tinyint arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("int nullable-column", Typed(doc, "e"));
        Assert.Equal("int nullable-column", Typed(doc, "f"));
        Assert.Equal("int window-edge", Typed(doc, "g2"));
        Assert.Equal("int window-edge nullable-column", Typed(doc, "h"));
        Assert.Equal("int", Typed(doc, "i"));
        Assert.Equal("tinyint", Typed(doc, "l"));

        // Without a default, the column's own values (and its CHECK set); with one, which may be outside it, only the type's.
        Assert.Equal("varchar(10) window-edge", Typed(doc, "j"));
        Assert.NotNull(((IrScalar)Field(doc, "j").Type).Facets?.Values);
        Assert.Null(((IrScalar)Field(doc, "k").Type).Facets?.Values);
    }

    [Fact]
    public void A_branch_proves_nothing_of_the_other_rows_lag_lead_first_value_and_last_value_read()
    {
        // Observed: A_branch_proves_nothing_of_the_other_rows_lag_lead_first_value_and_last_value_read. The
        // value argument is read in another row; LAG's offset and default, a percentile and an aggregate over a frame holding the
        // current row are read in this one, and a filter or a variable holds in every row.
        var doc = Procedure("""
            DECLARE @k int = (SELECT MAX(v) FROM dbo.t);
            SELECT CASE WHEN v IS NOT NULL THEN LAG(v, 1, 0) OVER (ORDER BY id) ELSE 0 END AS a,
                   CASE WHEN v IS NOT NULL THEN LEAD(v, 1, 0) OVER (ORDER BY id) ELSE 0 END AS b,
                   CASE WHEN v IS NOT NULL THEN FIRST_VALUE(v) OVER (ORDER BY id) ELSE 0 END AS c,
                   IIF(v IS NULL, 0, LAST_VALUE(v) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)) AS d,
                   CASE WHEN v IS NOT NULL THEN LAG(v, 0) OVER (ORDER BY id) ELSE 0 END AS e,
                   CASE WHEN v IS NOT NULL THEN LAG(0, 1, v) OVER (ORDER BY id) ELSE 0 END AS f,
                   CASE WHEN v IS NOT NULL THEN PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v) OVER () ELSE 0 END AS g2,
                   CASE WHEN v IS NOT NULL THEN MAX(v) OVER (ORDER BY id) ELSE 0 END AS h,
                   CASE WHEN @k IS NOT NULL THEN LAG(@k, 1, 0) OVER (ORDER BY id) ELSE 0 END AS i
            FROM dbo.t;
            """);
        Assert.Equal("int nullable-column", Typed(doc, "a"));
        Assert.Equal("int nullable-column", Typed(doc, "b"));
        Assert.Equal("int nullable-column", Typed(doc, "c"));
        Assert.Equal("int nullable-column", Typed(doc, "d"));
        Assert.Equal("int", Typed(doc, "e"));
        Assert.Equal("int", Typed(doc, "f"));
        Assert.Equal("int", Typed(doc, "g2"));
        Assert.Equal("int", Typed(doc, "h"));
        Assert.Equal("int", Typed(doc, "i"));

        // An aggregate a branch proves is the current group's; the previous group's may be NULL.
        var grouped = Procedure("SELECT g, CASE WHEN MAX(v) IS NOT NULL THEN LAG(MAX(v), 1, 0) OVER (ORDER BY g) ELSE 0 END AS a FROM dbo.t GROUP BY g;");
        Assert.StartsWith("int nullable-column", Typed(grouped, "a"), StringComparison.Ordinal);

        // A filter holds of every row the query keeps, so of every row a window reads.
        var filtered = Procedure("""
            SELECT LAG(v, 1, 0) OVER (ORDER BY id) AS a, FIRST_VALUE(v) OVER (ORDER BY id) AS b,
                   CASE WHEN v > 0 THEN LEAD(v, 1, 0) OVER (ORDER BY id) ELSE 0 END AS c
            FROM dbo.t WHERE v IS NOT NULL;
            """);
        Assert.Equal("int", Typed(filtered, "a"));
        Assert.Equal("int", Typed(filtered, "b"));
        Assert.Equal("int", Typed(filtered, "c"));
    }

    [Fact]
    public void First_and_last_value_and_percentiles()
    {
        // A_frame_that_may_leave_out_the_current_row_can_be_empty_…; Ranking_is_never_null_and_percentiles_are_null_only_over_all_null_input.
        var doc = Procedure("""
            SELECT FIRST_VALUE(n) OVER (ORDER BY id) AS a, LAST_VALUE(s) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS b,
                   FIRST_VALUE(v) OVER (PARTITION BY g ORDER BY id) AS c, LAST_VALUE(n) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS d,
                   PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY n) OVER () AS e, PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v) OVER (PARTITION BY g) AS f,
                   PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY d) OVER () AS h, PERCENTILE_DISC(0.25) WITHIN GROUP (ORDER BY s DESC) OVER () AS i,
                   PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY CAST(n AS bit)) OVER () AS j, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY s) OVER () AS k
            FROM dbo.t;
            """);
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("varchar(10) empty-frame", Typed(doc, "b"));
        Assert.Equal("int nullable-column", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "d"));
        Assert.Equal("float", Typed(doc, "e"));
        Assert.Equal("int nullable-column", Typed(doc, "f"));
        Assert.Equal("float", Typed(doc, "h"));
        Assert.Equal("varchar(10)", Typed(doc, "i"));

        // A bit is a number to PERCENTILE_CONT; a string is an error.
        Assert.Equal("float", Typed(doc, "j"));
        Unknown(doc, "k");
    }

    [Fact]
    public void A_function_over_a_window_named_in_a_window_clause_takes_its_frame()
    {
        // A_window_named_in_a_window_clause_brings_its_frame_to_every_function_over_it: the name's frame may be empty.
        var doc = Procedure("""
            SELECT FIRST_VALUE(n) OVER w AS a, SUM(n) OVER w AS b, ROW_NUMBER() OVER w AS c, SUM(n) OVER W AS d
            FROM dbo.t WINDOW w AS (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING);
            """);
        Assert.Equal("int empty-frame", Typed(doc, "a"));
        Assert.Equal("int arithmetic-overflow empty-frame", Typed(doc, "b"));
        Assert.Equal("bigint", Typed(doc, "c"));
        Assert.Equal("int arithmetic-overflow empty-frame", Typed(doc, "d"));
    }

    [Fact]
    public void A_window_defined_on_another_named_window_takes_the_elements_of_both()
    {
        // A_window_defined_on_another_named_window_takes_the_elements_of_both: the frame may come from the window named, from
        // the one defined on it (written before it or after), or from OVER itself.
        var doc = Procedure("""
            SELECT FIRST_VALUE(n) OVER w2 AS a, FIRST_VALUE(n) OVER w3 AS b, FIRST_VALUE(n) OVER (w1 ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS c,
                   FIRST_VALUE(n) OVER w1 AS d, FIRST_VALUE(n) OVER (w4 ORDER BY id) AS e
            FROM dbo.t
            WINDOW w3 AS (w2), w1 AS (ORDER BY id), w2 AS (w1 ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING), w4 AS (PARTITION BY g);
            """);
        Assert.Equal("int empty-frame", Typed(doc, "a"));
        Assert.Equal("int empty-frame", Typed(doc, "b"));
        Assert.Equal("int empty-frame", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "d"));
        Assert.Equal("int", Typed(doc, "e"));
    }

    [Theory]
    [InlineData("OVER x WINDOW w AS (ORDER BY id)", "doesn't define (5362)")]
    [InlineData("OVER w1 WINDOW w1 AS (w2), w2 AS (W1)", "defined on itself (5365)")]
    [InlineData("OVER (w ORDER BY id) WINDOW w AS (ORDER BY g)", "given again where 'w' gives it (4123)")]
    [InlineData("OVER w2 WINDOW w1 AS (ORDER BY id ROWS BETWEEN 1 PRECEDING AND CURRENT ROW), w2 AS (w1 ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING)", "given again where 'w1' gives it (4123)")]
    public void A_named_window_sql_server_refuses_is_unknown(string window, string why)
    {
        var doc = Procedure($"SELECT SUM(n) {window.Replace(" WINDOW", " AS a FROM dbo.t WINDOW", StringComparison.Ordinal)};");
        Assert.Contains(why, Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_window_function_not_modelled_is_unknown()
    {
        // AVG over a window is read (NumericTests).
        var doc = Procedure("SELECT CUME_DIST() OVER (ORDER BY id) AS a, PERCENT_RANK() OVER (ORDER BY id) AS b FROM dbo.t;");
        Unknown(doc, "a");
        Unknown(doc, "b");
    }
}

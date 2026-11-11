using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
    [Theory]
    [InlineData("t.name IS NOT NULL")]
    [InlineData("t.name = N'a'")]
    [InlineData("N'a' = t.name")]
    [InlineData("t.name <> N'a'")]
    [InlineData("t.name != N'a'")]
    [InlineData("t.name < N'm'")]
    [InlineData("N'm' > t.name")]
    [InlineData("t.name >= N'a' AND t.name <= N'z'")]
    [InlineData("t.name BETWEEN N'a' AND N'z'")]
    [InlineData("t.name IN (N'a', N'b')")]
    [InlineData("t.name LIKE N'a%'")]
    [InlineData("N'abc' LIKE t.name")]
    [InlineData("(t.name = N'a')")]
    [InlineData("t.id > 0 AND (t.name = N'a' AND t.n > 1)")]
    [InlineData("t.name = @x")]
    [InlineData("t.name = t.name")]
    public void A_conjunct_that_is_never_true_for_null_makes_the_column_non_null(string predicate)
    {
        // A_comparison_with_a_null_column_is_unknown_and_keeps_no_row, Like_is_unknown_when_the_value_or_the_pattern_is_null.
        var doc = Procedure($"SELECT t.name, t.n FROM dbo.t AS t WHERE {predicate};", "@x nvarchar(10)");

        Assert.False(Field(doc, "name").Nullable);
    }

    [Fact]
    public void A_conjunct_refines_only_the_columns_it_compares()
    {
        var doc = Procedure("SELECT t.name, t.n, t.status FROM dbo.t AS t WHERE t.name = N'a';");

        Assert.False(Field(doc, "name").Nullable);
        Assert.Equal(["nullable-column"], Rules(doc, "n"));
        Assert.Equal(["nullable-column"], Rules(doc, "status"));
    }

    [Fact]
    public void Both_columns_of_a_comparison_between_columns_are_refined()
    {
        var doc = Procedure("SELECT t.name, t.n, u.t_id FROM dbo.t AS t, dbo.u AS u WHERE u.t_id = t.n;");

        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "t_id").Nullable);
        Assert.True(Field(doc, "name").Nullable);
    }

    [Fact]
    public void Between_refines_its_bounds_too()
    {
        // 5 BETWEEN a AND b is 5 >= a AND 5 <= b, which is never TRUE when a or b is null.
        var doc = Procedure("SELECT t.n, u.t_id FROM dbo.t AS t, dbo.u AS u WHERE 5 BETWEEN t.n AND u.t_id;");

        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "t_id").Nullable);
    }

    [Theory]
    [InlineData("t.name = N'a' OR t.name = N'b'")]
    [InlineData("t.name IN (N'a') OR t.name LIKE N'b%'")]
    [InlineData("(t.name = N'a' AND t.n = 1) OR t.name = N'b'")]
    public void Under_or_every_disjunct_must_prove_it(string predicate) =>
        // Or_keeps_a_null_when_any_disjunct_can_be_true.
        Assert.False(Field(Procedure($"SELECT t.name FROM dbo.t AS t WHERE {predicate};"), "name").Nullable);

    [Theory]
    [InlineData("t.name = N'a' OR t.n = 1")]
    [InlineData("t.name = N'a' OR t.name IS NULL")]
    [InlineData("t.name = N'a' OR 1 = 1")]
    public void Under_or_one_disjunct_that_does_not_prove_it_is_enough_to_keep_it_nullable(string predicate)
    {
        var doc = Procedure($"SELECT t.name FROM dbo.t AS t WHERE {predicate};");

        Assert.Equal(["nullable-column"], Rules(doc, "name"));
    }

    [Theory]
    [InlineData("@x IS NULL OR t.name = @x")]
    [InlineData("t.name = @x OR @x IS NULL")]
    [InlineData("(@x IS NULL OR t.name = @x) AND (@y IS NULL OR t.n = @y)")]
    public void The_catch_all_filter_does_not_narrow_the_column(string predicate)
    {
        // The_catch_all_filter_keeps_every_row_when_its_parameter_is_null: with @x null, every row comes back, null names included.
        var doc = Procedure($"SELECT t.name, t.n FROM dbo.t AS t WHERE {predicate};", "@x nvarchar(10), @y int");

        Assert.Equal(["nullable-column"], Rules(doc, "name"));
        Assert.Equal(["nullable-column"], Rules(doc, "n"));
    }

    [Theory]
    [InlineData("t.name IS NULL")]
    [InlineData("ISNULL(t.name, N'') = N'a'")]
    [InlineData("COALESCE(t.name, N'') = N'a'")]
    [InlineData("CASE WHEN t.name IS NULL THEN 1 ELSE 0 END = 1")]
    [InlineData("t.name IS DISTINCT FROM N'a'")]
    [InlineData("t.name IS NOT DISTINCT FROM @x")]
    [InlineData("t.name NOT IN (SELECT u.note FROM dbo.u AS u)")]
    [InlineData("N'a' IN (t.name, N'a')")]
    [InlineData("EXISTS (SELECT 1 FROM dbo.u AS u WHERE u.note = t.name)")]
    [InlineData("NOT (t.name IS NOT NULL)")]
    public void Forms_that_can_be_true_for_a_null_do_not_refine(string predicate)
    {
        // A_null_inside_isnull_coalesce_or_case_can_make_the_predicate_true, Is_distinct_from_is_never_unknown,
        // In_a_subquery_with_no_rows_is_false_and_not_in_it_is_true_even_for_a_null.
        var doc = Procedure($"SELECT t.name FROM dbo.t AS t WHERE {predicate};", "@x nvarchar(10)");

        Assert.Equal(["nullable-column"], Rules(doc, "name"));
    }

    [Theory]
    [InlineData("NOT (t.name IS NULL)")]
    [InlineData("NOT (t.name = N'a')")]
    [InlineData("NOT (NOT (t.name = N'a'))")]
    [InlineData("t.name NOT IN (N'a', N'b')")]
    [InlineData("t.name NOT BETWEEN N'a' AND N'b'")]
    [InlineData("t.name NOT LIKE N'a%'")]
    [InlineData("t.name IN (SELECT u.note FROM dbo.u AS u)")]
    [InlineData("NOT (t.name = N'a' OR t.name IS NULL)")]
    public void Not_refines_exactly_where_three_valued_logic_says_it_is_never_true_for_null(string predicate) =>
        // Not_of_an_unknown_comparison_is_unknown_and_keeps_no_null: NOT UNKNOWN is UNKNOWN, and NOT (c IS NULL) is FALSE for
        // a null c. IN (subquery) is FALSE or UNKNOWN for a null, never TRUE.
        Assert.False(Field(Procedure($"SELECT t.name FROM dbo.t AS t WHERE {predicate};"), "name").Nullable);

    [Fact]
    public void A_procedure_created_with_ansi_nulls_off_does_not_refine_from_a_comparison_with_a_value()
    {
        // A_procedure_created_with_ansi_nulls_off_keeps_a_null_that_equals_a_null_variable: off, c = @x is TRUE when both are
        // null and c <> 1 is TRUE for a null c; a comparison of two columns is not affected.
        var doc = Infer(("tables.sql", Tables), ("p.sql", """
            SET ANSI_NULLS OFF;
            GO
            CREATE PROCEDURE dbo.p @x nvarchar(10) AS
            SELECT t.name, t.n, u.t_id FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.n WHERE t.name = @x AND t.status <> 'open';
            """));

        Assert.Equal(["nullable-column"], Rules(doc, "name"));
        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "t_id").Nullable);
    }

    [Fact]
    public void A_procedure_created_with_ansi_nulls_on_after_off_refines()
    {
        var doc = Infer(("tables.sql", Tables), ("p.sql", """
            SET ANSI_NULLS OFF;
            GO
            SET ANSI_NULLS ON;
            GO
            CREATE PROCEDURE dbo.p @x nvarchar(10) AS SELECT t.name FROM dbo.t AS t WHERE t.name = @x;
            """));

        Assert.False(Field(doc, "name").Nullable);
    }
}

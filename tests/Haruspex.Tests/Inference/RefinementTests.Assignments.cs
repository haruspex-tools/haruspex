using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
    private const string Tally = """
        CREATE FUNCTION dbo.tally (@above int) RETURNS TABLE AS RETURN
            SELECT COUNT_BIG(*) AS lines, SUM(t.n) AS total FROM dbo.t AS t WHERE t.n > @above;
        GO
        """;

    private static IrDocument WithTally(string body, string parameters = "") =>
        Infer(("tables.sql", Tables), ("tally.sql", Tally), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    [Fact]
    public void A_select_assigning_from_an_aggregate_without_group_by_always_assigns()
    {
        // A_select_assigning_from_an_aggregate_without_group_by_assigns_even_over_no_rows: the one row is there over no rows,
        // so the variable takes the aggregate's value. COUNT_BIG is never null; SUM is null over no rows or only nulls, and may
        // overflow, each its own reason, and none of them that the assignment may not happen.
        var doc = Procedure("""
            DECLARE @lines bigint, @total int;
            SELECT @lines = COUNT_BIG(*), @total = SUM(t.n) FROM dbo.t AS t WHERE t.name = N'x';
            SELECT @lines AS lines, @total AS total;
            """);

        Assert.Equal([], Rules(doc, "lines"));
        Assert.Equal([IrNullRule.NullableColumn, IrNullRule.EmptyAggregate, IrNullRule.ArithmeticOverflow], Rules(doc, "total"));
    }

    [Fact]
    public void A_select_assigning_from_a_query_that_may_return_no_row_may_assign_nothing()
    {
        // HAVING may filter the one row out, and GROUP BY over no rows has no group: the variable keeps what it held.
        var doc = Procedure("""
            DECLARE @having bigint, @grouped bigint;
            SELECT @having = COUNT_BIG(*) FROM dbo.t AS t HAVING COUNT_BIG(*) > 1;
            SELECT @grouped = COUNT_BIG(*) FROM dbo.t AS t GROUP BY t.n;
            SELECT @having AS having_, @grouped AS grouped;
            """);

        Assert.Equal([IrNullRule.Variable], Rules(doc, "having_"));
        Assert.Equal([IrNullRule.Variable], Rules(doc, "grouped"));
    }

    [Fact]
    public void A_select_assigning_from_an_inline_function_whose_body_is_one_row_always_assigns()
    {
        // A_select_assigning_from_an_inline_function_whose_body_is_one_row_assigns_even_over_no_rows.
        var doc = WithTally("""
            DECLARE @lines bigint, @total int, @filtered bigint;
            SELECT @lines = f.lines, @total = f.total FROM dbo.tally(@above) AS f;
            SELECT @filtered = f.lines FROM dbo.tally(@above) AS f WHERE f.lines > 0;
            SELECT @lines AS lines, @total AS total, @filtered AS filtered;
            """, "@above int");

        Assert.False(Field(doc, "lines").Nullable);
        Assert.Equal([IrNullRule.EmptyAggregate, IrNullRule.ArithmeticOverflow], Rules(doc, "total"));
        Assert.Equal([IrNullRule.Variable], Rules(doc, "filtered"));
    }

    [Fact]
    public void A_select_assigning_with_no_from_assigns_unless_a_where_may_filter_its_row_out()
    {
        // A_select_assigning_with_no_from_assigns_unless_a_where_filters_its_row_out.
        var doc = Procedure("""
            DECLARE @a int, @b int;
            SELECT @a = 1;
            SELECT @b = 1 WHERE @x = 1;
            SELECT @a AS a, @b AS b;
            """, "@x int");

        Assert.False(Field(doc, "a").Nullable);
        Assert.Equal([IrNullRule.Variable], Rules(doc, "b"));
    }
}

namespace Haruspex.DifferentialTests;

public partial class RefinementObservationTests
{
    // ---- a query that always returns one row, assigned --------------------------------------------------------------

    /// <summary>A table and an inline function over it, for the assignments below; the function's body is an aggregate.</summary>
    private static readonly string[] Counted =
    [
        "CREATE TABLE dbo.obs_none (id int NOT NULL PRIMARY KEY, c int NULL, s nvarchar(10) NULL);",
        "CREATE FUNCTION dbo.obs_tally (@above int) RETURNS TABLE AS RETURN SELECT COUNT_BIG(*) AS n, SUM(c) AS total FROM dbo.obs_none WHERE c > @above;",
        "CREATE FUNCTION dbo.obs_lit () RETURNS TABLE AS RETURN SELECT 1 AS one;",
    ];

    [Fact]
    public async Task A_select_assigning_from_an_aggregate_without_group_by_assigns_even_over_no_rows()
    {
        // The variables start as -1, so a select that assigned nothing would leave -1. Over no rows the aggregate's one row is
        // still there: COUNT_BIG assigns 0 and SUM assigns NULL, which the variable takes, so the assignment happened and the
        // value's own nullability is the aggregate's.
        Assert.Equal("n,total: 0,null", (await Run(Counted, """
            DECLARE @n bigint = -1, @total int = -1;
            SELECT @n = COUNT_BIG(*), @total = SUM(c) FROM dbo.obs_none WHERE c > 0;
            SELECT @n AS n, @total AS total;
            """)).ToString());

        // With HAVING the one row may be filtered out, and then nothing is assigned; GROUP BY over no rows has no group.
        Assert.Equal("n: -1", (await Run(Counted, """
            DECLARE @n bigint = -1;
            SELECT @n = COUNT_BIG(*) FROM dbo.obs_none HAVING COUNT_BIG(*) > 0;
            SELECT @n AS n;
            """)).ToString());
        Assert.Equal("n: -1", (await Run(Counted, """
            DECLARE @n bigint = -1;
            SELECT @n = COUNT_BIG(*) FROM dbo.obs_none GROUP BY c;
            SELECT @n AS n;
            """)).ToString());
    }

    [Fact]
    public async Task A_select_assigning_from_an_inline_function_whose_body_is_one_row_assigns_even_over_no_rows()
    {
        Assert.Equal("n,total,one: 0,null,1", (await Run(Counted, """
            DECLARE @n bigint = -1, @total int = -1, @one int = -1;
            SELECT @n = f.n, @total = f.total FROM dbo.obs_tally(0) AS f;
            SELECT @one = l.one FROM dbo.obs_lit() AS l;
            SELECT @n AS n, @total AS total, @one AS one;
            """)).ToString());

        // A filter outside the function may still remove its one row.
        Assert.Equal("n: -1", (await Run(Counted, """
            DECLARE @n bigint = -1;
            SELECT @n = f.n FROM dbo.obs_tally(0) AS f WHERE f.n > 0;
            SELECT @n AS n;
            """)).ToString());
    }

    [Fact]
    public async Task A_select_assigning_with_no_from_assigns_unless_a_where_filters_its_row_out() =>
        Assert.Equal("a,b: 1,-1", (await Run([], """
            DECLARE @a int = -1, @b int = -1;
            SELECT @a = 1;
            SELECT @b = 1 WHERE 1 = 0;
            SELECT @a AS a, @b AS b;
            """)).ToString());
}

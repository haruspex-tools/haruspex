using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with predicates, aggregates and single-row queries, observed and kept as tests: the rules
/// refinement in Haruspex.SqlServer.Inference (<c>Refinement.cs</c>, <c>Cardinality.cs</c>) rests on. Which rows a filter
/// keeps when a column is NULL, under three-valued logic; what aggregates give over no rows, grouped and not; how many rows
/// TOP, OFFSET … FETCH and a unique key allow. Each test creates what it needs in a write session, rolled back, alone in
/// the test database while it lasts. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class RefinementObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call) => ObservedCall.Run(server, setup, call);

    /// <summary>One row with every column null, beside rows with values; no row is ever filtered in by accident.</summary>
    private const string Rows = """
        CREATE TABLE dbo.obs_val (id int NOT NULL PRIMARY KEY, c int NULL, s nvarchar(10) NULL);
        INSERT INTO dbo.obs_val VALUES (1, 1, N'abc'), (2, 2, N'xyz'), (3, NULL, NULL);
        """;

    private const string Empty = "CREATE TABLE dbo.obs_none (id int NOT NULL PRIMARY KEY, c int NULL, s nvarchar(10) NULL);";

    /// <summary>The ids of the rows of obs_val that <paramref name="where"/> keeps, with @x set to <paramref name="x"/>.</summary>
    private async Task<string> Kept(string where, string x = "NULL") =>
        (await Run([Rows], $"DECLARE @x int = {x}; SELECT id FROM dbo.obs_val WHERE {where} ORDER BY id;")).ToString();

    // ---- three-valued logic: what a filter keeps when a column is null ---------------------------------------------

    [Fact]
    public async Task A_comparison_with_a_null_column_is_unknown_and_keeps_no_row()
    {
        // Every comparison, in either order, is UNKNOWN when the column is NULL, and WHERE keeps only TRUE: row 3 never comes.
        Assert.Equal("id: 1", await Kept("c = 1"));
        Assert.Equal("id: 2", await Kept("1 <> c"));
        Assert.Equal("id: 1;2", await Kept("c < 5"));
        Assert.Equal("id: 1;2", await Kept("5 > c"));
        Assert.Equal("id: 1;2", await Kept("c >= 0 AND c <= 9"));
        Assert.Equal("id: 1;2", await Kept("c BETWEEN 0 AND 9"));
        Assert.Equal("id: 1;2", await Kept("c IN (1, 2, 3)"));
        Assert.Equal("id: 1;2", await Kept("c IS NOT NULL"));
        Assert.Equal("id: 3", await Kept("c IS NULL"));
    }

    [Fact]
    public async Task Like_is_unknown_when_the_value_or_the_pattern_is_null()
    {
        Assert.Equal("id: 1", await Kept("s LIKE N'a%'"));
        Assert.Equal("id: 1;2", await Kept("s LIKE N'%'"));
        Assert.Equal("id: 1;2", await Kept("N'abc' LIKE s OR N'xyz' LIKE s"));
    }

    [Fact]
    public async Task Not_of_an_unknown_comparison_is_unknown_and_keeps_no_null()
    {
        // NOT UNKNOWN is UNKNOWN, so NOT (c = 1) drops a null c as c = 1 does; NOT (c IS NULL) is c IS NOT NULL. NOT IN a list
        // and NOT BETWEEN drop it too. But NOT IN a subquery that returns no rows is TRUE for a null c (below).
        Assert.Equal("id: 2", await Kept("NOT (c = 1)"));
        Assert.Equal("id: 1;2", await Kept("NOT (c IS NULL)"));
        Assert.Equal("id: 2", await Kept("c NOT IN (1)"));
        Assert.Equal("id: 2", await Kept("c NOT BETWEEN 0 AND 1"));
        Assert.Equal("id: 2", await Kept("s NOT LIKE N'a%'"));
    }

    [Fact]
    public async Task Or_keeps_a_null_when_any_disjunct_can_be_true()
    {
        // c = 1 OR c = 2 is UNKNOWN for a null c; but a disjunct that does not read c may be TRUE whatever c is.
        Assert.Equal("id: 1;2", await Kept("c = 1 OR c = 2"));
        Assert.Equal("id: 1;3", await Kept("c = 1 OR id = 3"));
        Assert.Equal("id: 1;2;3", await Kept("c = 1 OR c IS NULL OR c = 2"));
    }

    [Fact]
    public async Task The_catch_all_filter_keeps_every_row_when_its_parameter_is_null()
    {
        // @x IS NULL OR c = @x: the optional-filter idiom. With @x null the first disjunct is TRUE for every row, so a null c
        // comes back, and the filter must not make c non-null.
        Assert.Equal("id: 1;2;3", await Kept("@x IS NULL OR c = @x"));
        Assert.Equal("id: 2", await Kept("@x IS NULL OR c = @x", x: "2"));
        Assert.Equal("id: 1;2;3", await Kept("c = ISNULL(@x, c) OR @x IS NULL"));

        // c = ISNULL(@x, c) alone drops a null c even with @x null: c = c is UNKNOWN for null.
        Assert.Equal("id: 1;2", await Kept("c = ISNULL(@x, c)"));
        Assert.Equal("id: 1;2", await Kept("c = COALESCE(@x, c)"));
    }

    [Fact]
    public async Task A_null_inside_isnull_coalesce_or_case_can_make_the_predicate_true()
    {
        Assert.Equal("id: 1;3", await Kept("ISNULL(c, 1) = 1"));
        Assert.Equal("id: 1;3", await Kept("COALESCE(c, 1) = 1"));
        Assert.Equal("id: 3", await Kept("CASE WHEN c IS NULL THEN 1 ELSE 0 END = 1"));
    }

    [Fact]
    public async Task In_a_subquery_with_no_rows_is_false_and_not_in_it_is_true_even_for_a_null()
    {
        // So IN (subquery) never keeps a null c, but NOT IN (subquery) keeps it when the subquery is empty.
        Assert.Equal("id: ", await Kept("c IN (SELECT n.c FROM dbo.obs_val AS n WHERE 1 = 0)"));
        Assert.Equal("id: 1;2;3", await Kept("c NOT IN (SELECT n.c FROM dbo.obs_val AS n WHERE 1 = 0)"));
        Assert.Equal("id: 1;2", await Kept("c IN (SELECT n.c FROM dbo.obs_val AS n)"));
    }

    [Fact]
    public async Task Is_distinct_from_is_never_unknown()
    {
        // IS [NOT] DISTINCT FROM compares nulls as values: a null c can pass either.
        Assert.Equal("id: 3", await Kept("c IS NOT DISTINCT FROM @x"));
        Assert.Equal("id: 1;3", await Kept("c IS DISTINCT FROM 2"));
    }

    // ---- joins ----------------------------------------------------------------------------------------------------------

    private const string Joined = """
        CREATE TABLE dbo.obs_head (id int NOT NULL PRIMARY KEY, label nvarchar(10) NOT NULL);
        CREATE TABLE dbo.obs_line (id int NOT NULL PRIMARY KEY, head_id int NOT NULL, note nvarchar(10) NULL);
        INSERT INTO dbo.obs_head VALUES (1, N'one'), (2, N'two');
        INSERT INTO dbo.obs_line VALUES (10, 1, N'x'), (11, 1, NULL);
        """;

    [Fact]
    public async Task A_where_condition_on_the_optional_side_of_an_outer_join_drops_the_rows_that_did_not_match()
    {
        // Head 2 has no line: its line columns are null, and any condition that is unknown for null drops it.
        Assert.Equal("id,line: 1,10;1,11;2,null", (await Run([Joined], "SELECT h.id, l.id AS line FROM dbo.obs_head AS h LEFT JOIN dbo.obs_line AS l ON l.head_id = h.id ORDER BY h.id, l.id;")).ToString());
        Assert.Equal("id,line: 1,10;1,11", (await Run([Joined], "SELECT h.id, l.id AS line FROM dbo.obs_head AS h LEFT JOIN dbo.obs_line AS l ON l.head_id = h.id WHERE l.id > 0 ORDER BY h.id, l.id;")).ToString());

        // A condition on a nullable column of the optional side proves the row matched, but not that its other nullable
        // columns hold values: line 11 matched with a null note.
        Assert.Equal("line,note: 11,null", (await Run([Joined], "SELECT l.id AS line, l.note FROM dbo.obs_head AS h LEFT JOIN dbo.obs_line AS l ON l.head_id = h.id WHERE l.id = 11;")).ToString());
    }

    [Fact]
    public async Task A_condition_in_the_on_of_an_outer_join_never_drops_a_row_of_the_kept_side()
    {
        // The ON decides only which rows match: head 2 still comes, null-extended, and so does head 1 whose lines do not pass.
        Assert.Equal("id,line: 1,null;2,null", (await Run([Joined], "SELECT h.id, l.id AS line FROM dbo.obs_head AS h LEFT JOIN dbo.obs_line AS l ON l.head_id = h.id AND l.note = N'none' ORDER BY h.id;")).ToString());
    }

    [Fact]
    public async Task An_inner_join_after_an_outer_join_drops_its_unmatched_rows()
    {
        // The inner join's ON reads the optional side's column, which is null for head 2, so head 2 goes.
        Assert.Equal("id,line: 1,10;1,11", (await Run([Joined], """
            SELECT h.id, l.id AS line
            FROM dbo.obs_head AS h
            LEFT JOIN dbo.obs_line AS l ON l.head_id = h.id
            INNER JOIN dbo.obs_head AS again ON again.id = l.head_id
            ORDER BY h.id, l.id;
            """)).ToString());
    }

    // ---- aggregates over no rows ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows()
    {
        // COUNT and COUNT_BIG are 0; SUM, AVG, MIN and MAX are NULL.
        Assert.Equal("n,nc,nb,total,mean,low,high: 0,0,0,null,null,null,null", (await Run([Empty], """
            SELECT COUNT(*) AS n, COUNT(c) AS nc, COUNT_BIG(*) AS nb, SUM(c) AS total, AVG(c) AS mean, MIN(c) AS low, MAX(s) AS high
            FROM dbo.obs_none WHERE c > 0;
            """)).ToString());

        // Over rows whose values are all null, COUNT(c) is 0 and the others NULL; COUNT(*) counts the row.
        Assert.Equal("n,nc,total,high: 1,0,null,null", (await Run([Rows], "SELECT COUNT(*) AS n, COUNT(c) AS nc, SUM(c) AS total, MAX(s) AS high FROM dbo.obs_val WHERE id = 3;")).ToString());
    }

    [Fact]
    public async Task Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off()
    {
        // COUNT returns int. Past 2,147,483,647 rows it overflows: an error (8115) by default, but NULL when the caller's
        // session has ANSI_WARNINGS and ARITHABORT off, which a procedure cannot prevent. COUNT_BIG does not overflow.
        // So COUNT can be null, if only there, and COUNT_BIG cannot. The cross join is counted without being built.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_count AS
            WITH d AS (SELECT TOP (216) 1 AS x FROM sys.all_columns)
            SELECT COUNT(*) AS n, COUNT_BIG(*) AS nb FROM d AS a CROSS JOIN d AS b CROSS JOIN d AS c CROSS JOIN d AS e;
            """];

        Assert.Equal("n,nb: null,2176782336", (await Run(setup, "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; EXEC dbo.obs_count; SET ANSI_WARNINGS ON;")).ToString());
        Assert.Equal(" ! 8115", (await Run(setup, "EXEC dbo.obs_count;")).ToString());
    }

    [Fact]
    public async Task An_aggregate_with_group_by_returns_no_row_over_no_rows()
    {
        // No rows means no groups, even for GROUP BY () (the grand total alone).
        Assert.Equal("n: ", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none GROUP BY c;")).ToString());
        Assert.Equal("n: ", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none GROUP BY ();")).ToString());

        // A group's MAX is null only when every value in it is null.
        Assert.Equal("c,high: null,null;1,abc;2,xyz", (await Run([Rows], "SELECT c, MAX(s) AS high FROM dbo.obs_val GROUP BY c ORDER BY c;")).ToString());
    }

    [Fact]
    public async Task Having_over_no_groups_returns_no_row_and_without_group_by_at_most_one()
    {
        Assert.Equal("n: ", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none GROUP BY c HAVING COUNT(*) > 0;")).ToString());

        // Without GROUP BY, HAVING filters the one row: it may keep it or not.
        Assert.Equal("n: ", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none HAVING COUNT(*) > 0;")).ToString());
        Assert.Equal("n: 0", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none HAVING COUNT(*) = 0;")).ToString());

        // HAVING on an aggregate is unknown for its null, as WHERE is for a column's, so the group goes.
        Assert.Equal("c,high: 1,abc;2,xyz", (await Run([Rows], "SELECT c, MAX(s) AS high FROM dbo.obs_val GROUP BY c HAVING MAX(s) > N'' ORDER BY c;")).ToString());
        Assert.Equal("c,n: 1,1;2,1", (await Run([Rows], "SELECT c, COUNT(*) AS n FROM dbo.obs_val GROUP BY c HAVING c > 0 ORDER BY c;")).ToString());
    }

    [Fact]
    public async Task A_scalar_subquery_of_an_aggregate_without_group_by_always_has_a_value_for_count()
    {
        Assert.Equal("n,total: 0,null", (await Run([Empty], "SELECT (SELECT COUNT(*) FROM dbo.obs_none) AS n, (SELECT SUM(c) FROM dbo.obs_none) AS total;")).ToString());

        // With GROUP BY the subquery may return no row, which is NULL.
        Assert.Equal("n: null", (await Run([Empty], "SELECT (SELECT COUNT(*) FROM dbo.obs_none GROUP BY c) AS n;")).ToString());

        // A query without FROM returns one row; with a WHERE that fails, none.
        Assert.Equal("a,b: 1,null", (await Run([], "SELECT (SELECT 1) AS a, (SELECT 1 WHERE 1 = 0) AS b;")).ToString());
    }

    [Fact]
    public async Task For_json_over_an_aggregate_without_group_by_always_returns_its_one_object()
    {
        // Never the no-rows case of FOR JSON: the one row is always there, in the array or alone.
        Assert.Equal("JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{\"n\":0}]", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none FOR JSON PATH;")).ToString());
        Assert.Equal("JSON_F52E2B61-18A1-11d1-B105-00805F49916B: {\"n\":0}", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;")).ToString());
        Assert.Equal("j: [{\"n\":0}]", (await Run([Empty], "SELECT (SELECT COUNT(*) AS n FROM dbo.obs_none FOR JSON PATH) AS j;")).ToString());
    }

    [Fact]
    public async Task Rollup_cube_and_grouping_sets_add_rows_whose_grouped_columns_are_null()
    {
        // The super-aggregate rows hold NULL in the columns they roll up, whatever WHERE proved of them, and they are rows
        // of their own: a filter fixing a key no longer gives at most one row.
        Assert.Equal("c,n: 1,1;2,1;null,2", (await Run([Rows], "SELECT c, COUNT_BIG(*) AS n FROM dbo.obs_val WHERE c IS NOT NULL GROUP BY ROLLUP (c) ORDER BY GROUPING(c), c;")).ToString());
        Assert.Equal("id,n: 1,1;null,1", (await Run([Rows], "SELECT id, COUNT_BIG(*) AS n FROM dbo.obs_val WHERE id = 1 GROUP BY ROLLUP (id) ORDER BY GROUPING(id);")).ToString());
        Assert.Equal("id,n: 1,1;null,1", (await Run([Rows], "SELECT id, COUNT_BIG(*) AS n FROM dbo.obs_val WHERE id = 1 GROUP BY CUBE (id) ORDER BY GROUPING(id);")).ToString());
        Assert.Equal("id,n: 1,1;null,1", (await Run([Rows], "SELECT id, COUNT_BIG(*) AS n FROM dbo.obs_val WHERE id = 1 GROUP BY GROUPING SETS ((id), ()) ORDER BY GROUPING(id);")).ToString());
        Assert.Equal("id,n: 1,1;null,1", (await Run([Rows], "SELECT id, COUNT_BIG(*) AS n FROM dbo.obs_val WHERE id = 1 GROUP BY id WITH ROLLUP ORDER BY GROUPING(id);")).ToString());
    }

    [Fact]
    public async Task An_aggregate_subquery_returns_its_row_even_when_its_where_rejects_the_outer_column()
    {
        // The outer row's c is null, so t.c = o.c keeps no row; but an aggregate without GROUP BY still returns one, in which
        // o.c is null. A filter on an enclosing query's column proves nothing in such a query's select list.
        Assert.Equal("j: {\"n\":0}", (await Run([Rows], """
            SELECT (SELECT o.c AS c, COUNT_BIG(*) AS n FROM dbo.obs_val AS t WHERE t.c = o.c FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j
            FROM dbo.obs_val AS o WHERE o.id = 3;
            """)).ToString());
        Assert.Equal("c,n: null,0", (await Run([Rows], "SELECT (SELECT o.c FROM dbo.obs_val AS t WHERE t.c = o.c HAVING COUNT(*) >= 0) AS c, (SELECT COUNT_BIG(*) FROM dbo.obs_val AS t WHERE t.c = o.c) AS n FROM dbo.obs_val AS o WHERE o.id = 3;")).ToString());
    }

    // ---- how many rows ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Offset_keeps_the_one_row_of_an_aggregate_query_only_when_it_skips_none()
    {
        Assert.Equal("n: 0", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none ORDER BY 1 OFFSET 0 ROWS;")).ToString());
        Assert.Equal("n: 0", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none ORDER BY 1 OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;")).ToString());
        Assert.Equal("n: ", (await Run([Empty], "SELECT COUNT(*) AS n FROM dbo.obs_none ORDER BY 1 OFFSET 1 ROWS;")).ToString());
    }

    [Fact]
    public async Task A_filtered_unique_index_allows_duplicates_outside_its_filter()
    {
        // Unique only among the rows its filter keeps: several NULLs, or several equal values the filter leaves out.
        string[] setup = ["CREATE TABLE dbo.obs_filtered (id int NOT NULL PRIMARY KEY, code int NULL, INDEX ux_code UNIQUE (code) WHERE code > 0);"];
        Assert.Equal("", (await Run(setup, "INSERT INTO dbo.obs_filtered VALUES (1, NULL), (2, NULL), (3, -1), (4, -1);")).ToString());
        Assert.Equal(" ! 2601", (await Run(setup, "INSERT INTO dbo.obs_filtered VALUES (1, 5), (2, 5);")).ToString());
    }

    [Fact]
    public async Task A_disabled_unique_index_allows_duplicates()
    {
        // Disabled, it enforces nothing, so a key it declares no longer fixes one row; rebuilding it then fails on them.
        string[] setup = [
            "CREATE TABLE dbo.obs_disabled (id int NOT NULL PRIMARY KEY, code int NOT NULL CONSTRAINT uq_obs_disabled UNIQUE);",
            "ALTER INDEX uq_obs_disabled ON dbo.obs_disabled DISABLE;",
        ];
        Assert.Equal("n: 2", (await Run(setup, "INSERT INTO dbo.obs_disabled VALUES (1, 5), (2, 5); SELECT COUNT(*) AS n FROM dbo.obs_disabled WHERE code = 5;")).ToString());
    }


    [Fact]
    public async Task Top_1_and_fetch_1_give_at_most_one_row_but_with_ties_gives_more()
    {
        Assert.Equal("id: 1", (await Run([Rows], "SELECT TOP (1) id FROM dbo.obs_val ORDER BY id;")).ToString());
        Assert.Equal("id: ", (await Run([Empty], "SELECT TOP (1) id FROM dbo.obs_none ORDER BY id;")).ToString());
        Assert.Equal("id: 2", (await Run([Rows], "SELECT id FROM dbo.obs_val ORDER BY id OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY;")).ToString());
        Assert.Equal("id: 1;2", (await Run([Rows, "UPDATE dbo.obs_val SET c = 1 WHERE id = 2;"], "SELECT TOP (1) WITH TIES id FROM dbo.obs_val ORDER BY c DESC;")).ToString());
    }

    private const string Keys = """
        CREATE TABLE dbo.obs_key (
            id int NOT NULL CONSTRAINT pk_obs_key PRIMARY KEY,
            code varchar(10) NULL CONSTRAINT uq_obs_key_code UNIQUE,
            a int NULL, b int NULL,
            CONSTRAINT uq_obs_key_ab UNIQUE (a, b));
        """;

    [Fact]
    public async Task A_unique_constraint_allows_one_null_only()
    {
        // SQL Server treats NULLs as equal in a unique constraint: a second NULL is a duplicate key (2627), so a filter
        // `code IS NULL` fixes at most one row too.
        Assert.Equal(" ! 2627", (await Run([Keys], "INSERT INTO dbo.obs_key (id, code, a) VALUES (1, NULL, 1), (2, NULL, 2);")).ToString());
        Assert.Equal(" ! 2627", (await Run([Keys], "INSERT INTO dbo.obs_key (id, code, a, b) VALUES (1, 'x', NULL, 5), (2, 'y', NULL, 5);")).ToString());
        Assert.Equal("", (await Run([Keys], "INSERT INTO dbo.obs_key (id, code, a, b) VALUES (1, 'x', NULL, 5), (2, 'y', NULL, 6);")).ToString());
    }

    [Fact]
    public async Task A_comparison_that_converts_the_key_can_match_several_rows()
    {
        // varchar has lower precedence than int, so code = 5 converts code to int, and '5' and '05' both equal 5: a key
        // compared with a value of another type does not fix one row.
        Assert.Equal("id: 1;2", (await Run([Keys, "INSERT INTO dbo.obs_key (id, code, a) VALUES (1, '5', 1), (2, '05', 2);"], "SELECT id FROM dbo.obs_key WHERE code = 5 ORDER BY id;")).ToString());
        Assert.Equal("id: 1", (await Run([Keys, "INSERT INTO dbo.obs_key (id, code, a) VALUES (1, '5', 1), (2, '05', 2);"], "SELECT id FROM dbo.obs_key WHERE code = '5' ORDER BY id;")).ToString());
    }

    [Fact]
    public async Task A_varchar_key_under_a_sql_collation_compared_with_nvarchar_can_match_several_rows()
    {
        // Under a SQL_ collation varchar compares by the collation's own rules but nvarchar by Unicode's, where more strings
        // are equal: a unique varchar key compared with an nvarchar value can match more than one row.
        string[] setup = [
            "CREATE TABLE dbo.obs_sqlkey (id int NOT NULL PRIMARY KEY, code varchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL UNIQUE);",
            "INSERT INTO dbo.obs_sqlkey VALUES (1, 'ss'), (2, 'ß');",
        ];
        Assert.Equal("id: 1", (await Run(setup, "SELECT id FROM dbo.obs_sqlkey WHERE code = 'ss' ORDER BY id;")).ToString());
        Assert.Equal("id: 1;2", (await Run(setup, "SELECT id FROM dbo.obs_sqlkey WHERE code = N'ss' ORDER BY id;")).ToString());
    }

    // ---- ANSI_NULLS ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_procedure_created_with_ansi_nulls_off_keeps_a_null_that_equals_a_null_variable()
    {
        // The setting is the one in force when the procedure is created, not the caller's. Off, c = @x is TRUE when both are
        // null, so the comparison no longer proves c non-null; a comparison of two columns is not affected.
        string[] setup = [Rows, "SET ANSI_NULLS OFF;", "CREATE PROCEDURE dbo.obs_off @x int AS SELECT id FROM dbo.obs_val WHERE c = @x ORDER BY id;",
            "CREATE PROCEDURE dbo.obs_off_columns AS SELECT a.id FROM dbo.obs_val AS a JOIN dbo.obs_val AS b ON b.c = a.c ORDER BY a.id;", "SET ANSI_NULLS ON;"];
        Assert.Equal("id: 3", (await Run(setup, "EXEC dbo.obs_off NULL;")).ToString());
        Assert.Equal("id: 1;2", (await Run(setup, "EXEC dbo.obs_off_columns;")).ToString());
    }
}

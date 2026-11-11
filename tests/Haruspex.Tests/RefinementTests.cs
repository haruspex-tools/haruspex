using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// Refinement (slice 6c): what predicates, joins and single-row queries prove. A filter that is never TRUE when a column is
/// NULL makes the column non-null in the rows it keeps; an aggregate without GROUP BY returns exactly one row; TOP (1),
/// FETCH 1 and a filter fixing a whole key return at most one. The engine rules each test leans on are observed in the
/// differential tests' <c>RefinementObservationTests</c>, named where they matter.
/// </summary>
public class RefinementTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, n int NULL,
            code varchar(10) NULL CONSTRAINT uq_t_code UNIQUE,
            status varchar(6) NULL CONSTRAINT ck_t_status CHECK (status IN ('open', 'shut')));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NULL, note nvarchar(10) NULL, label nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.pair (a int NOT NULL, b int NOT NULL, v int NULL, w varchar(5) NULL, CONSTRAINT pk_pair PRIMARY KEY (a, b),
            CONSTRAINT uq_pair_vw UNIQUE (v, w));
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);

        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse("*.*")]).Procedures);
    }

    private static IrDocument Procedure(string body, string parameters = "") =>
        Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    private static IReadOnlyList<IrResult> Results(IrDocument doc) =>
        doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));

    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => type switch
    {
        IrArray { Items: var items } => Fields(doc, items),
        IrRef r => doc.Types[r.Name].Fields,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    private static IrField Field(IrDocument doc, string name, int result = 0) => Assert.Single(Fields(doc, Results(doc)[result].Type), f => f.Name == name);

    private static string[] Rules(IrField field) => [.. field.NullableBecause.Select(r => r.Rule)];

    private static string[] Rules(IrDocument doc, string name) => Rules(Field(doc, name));

    private static IrCardinality Rows(string body, string parameters = "") => Assert.Single(Results(Procedure(body, parameters))).Cardinality;

    // ---- WHERE: what a conjunct proves -------------------------------------------------------------------------

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

    // ---- joins ----------------------------------------------------------------------------------------------------

    [Fact]
    public void An_inner_joins_on_refines_both_sides()
    {
        var doc = Procedure("SELECT t.n, u.t_id, u.note FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.n;");

        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "t_id").Nullable);
        Assert.Equal(["nullable-column"], Rules(doc, "note"));
    }

    [Fact]
    public void A_where_condition_on_the_optional_side_proves_its_row_matched()
    {
        // A_where_condition_on_the_optional_side_of_an_outer_join_drops_the_rows_that_did_not_match: the condition removes the
        // null-extended rows, so no column of u is null for want of a match; one that is nullable in u still may be.
        var doc = Procedure("SELECT t.id, u.id AS uid, u.label, u.note, u.t_id FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.id WHERE u.t_id > 0;");

        Assert.False(Field(doc, "uid").Nullable);
        Assert.False(Field(doc, "label").Nullable);
        Assert.False(Field(doc, "t_id").Nullable);
        Assert.Equal(["nullable-column"], Rules(doc, "note"));
    }

    [Fact]
    public void An_outer_joins_on_refines_only_its_optional_side_and_keeps_the_outer_join()
    {
        // A_condition_in_the_on_of_an_outer_join_never_drops_a_row_of_the_kept_side.
        var doc = Procedure("SELECT t.n, u.t_id, u.note FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.n AND t.name = N'a';");

        Assert.Equal(["nullable-column"], Rules(doc, "n"));
        Assert.Equal(["outer-join"], Rules(doc, "t_id"));
        Assert.Equal(["nullable-column", "outer-join"], Rules(doc, "note"));
    }

    [Fact]
    public void An_outer_joins_on_and_a_where_on_another_column_of_its_side_together_make_a_column_non_null()
    {
        var doc = Procedure("SELECT u.t_id FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.id WHERE u.label = N'x';");

        Assert.False(Field(doc, "t_id").Nullable);
    }

    [Fact]
    public void An_inner_join_after_an_outer_join_drops_its_unmatched_rows()
    {
        // An_inner_join_after_an_outer_join_drops_its_unmatched_rows.
        var doc = Procedure("""
            SELECT u.label, u.note FROM dbo.t AS t
            LEFT JOIN dbo.u AS u ON u.t_id = t.id
            JOIN dbo.pair AS p ON p.a = u.id;
            """);

        Assert.False(Field(doc, "label").Nullable);
        Assert.Equal(["nullable-column"], Rules(doc, "note"));
    }

    [Fact]
    public void An_inner_joins_on_inside_the_optional_side_keeps_the_outer_join()
    {
        var doc = Procedure("""
            SELECT u.t_id, p.v FROM dbo.t AS t
            LEFT JOIN (dbo.u AS u JOIN dbo.pair AS p ON p.v = u.t_id) ON u.id = t.id;
            """);

        Assert.Equal(["outer-join"], Rules(doc, "t_id"));
        Assert.Equal(["outer-join"], Rules(doc, "v"));
    }

    [Fact]
    public void A_condition_on_one_source_of_a_nested_optional_side_proves_only_that_sources_row()
    {
        // u matched, but p is on an outer join of its own inside u's side: it may still be null-extended.
        var doc = Procedure("""
            SELECT u.label, p.a FROM dbo.t AS t
            LEFT JOIN (dbo.u AS u LEFT JOIN dbo.pair AS p ON p.a = u.id) ON u.id = t.id
            WHERE u.label = N'x';
            """);

        Assert.False(Field(doc, "label").Nullable);
        Assert.Equal(["outer-join"], Rules(doc, "a"));
    }

    // ---- scope -------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_subquerys_filter_does_not_refine_the_outer_querys_columns()
    {
        var doc = Procedure("SELECT t.name, (SELECT COUNT_BIG(*) FROM dbo.u AS u WHERE u.note = t.name) AS hits FROM dbo.t AS t;");

        Assert.Equal(["nullable-column"], Rules(doc, "name"));
    }

    [Fact]
    public void The_outer_querys_filter_refines_a_column_a_subquery_reads_from_it()
    {
        var doc = Procedure("SELECT t.id, (SELECT TOP (1) t.name FROM dbo.u AS u) AS name FROM dbo.t AS t WHERE t.name IS NOT NULL;");

        // The subquery may have no row, which is null; but not for want of a name.
        Assert.Equal(["empty-subquery"], Rules(doc, "name"));
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
        Assert.Equal([new IrNullReason(IrNullRules.NullableColumn, "dbo.t.name")], Field(doc, "label").NullableBecause);
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

        Assert.Equal(["outer-join"], Rules(doc, "id"));
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
        Assert.Equal(["nullable-column"], Rules(n));
        Assert.Contains("nullable-column", Rules(doc, "kept"));

        // Without an aggregate, a row the subquery returns passed its WHERE.
        Assert.Equal(["empty-subquery"], Rules(doc, "plain"));
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

        Assert.Equal(["empty-aggregate"], Rules(doc, "note"));
        Assert.Equal(["empty-subquery"], Rules(doc, "n"));
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
        Assert.Contains("grouping-set", Rules(doc, "n"));
        Assert.Contains("grouping-set", Rules(doc, "id"));
        Assert.False(Field(doc, "c").Nullable);
        Assert.False(Field(doc, "top_id").Nullable);
    }

    [Fact]
    public void A_plain_group_by_leaves_grouped_columns_as_they_are()
    {
        var doc = Procedure("SELECT t.n, COUNT_BIG(*) AS c FROM dbo.t AS t WHERE t.n IS NOT NULL GROUP BY t.n;");

        Assert.False(Field(doc, "n").Nullable);
    }

    // ---- aggregates and HAVING ---------------------------------------------------------------------------------

    [Fact]
    public void Count_big_is_never_null_and_count_only_on_overflow()
    {
        // Count_over_more_rows_than_int_holds_is_null_under_the_callers_ansi_warnings_and_arithabort_off.
        var doc = Procedure("SELECT COUNT(*) AS n, COUNT(t.name) AS named, COUNT_BIG(*) AS nb, COUNT_BIG(DISTINCT t.name) AS kinds FROM dbo.t AS t;");

        Assert.Equal(["count-overflow"], Rules(doc, "n"));
        Assert.Equal(["count-overflow"], Rules(doc, "named"));
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
        Assert.Equal(["nullable-column", "empty-aggregate"], Rules(high));
        Assert.Equal(["open", "shut"], Assert.IsType<IrScalar>(high.Type).Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(["empty-aggregate"], Rules(doc, "low"));
    }

    [Fact]
    public void Under_group_by_max_is_null_only_when_its_argument_can_be()
    {
        // An_aggregate_with_group_by_returns_no_row_over_no_rows: a group's MAX is null only when every value in it is.
        var doc = Procedure("SELECT t.n, MAX(t.name) AS high, MAX(t.id) AS top_id FROM dbo.t AS t GROUP BY t.n;");

        Assert.Equal(["nullable-column"], Rules(doc, "high"));
        Assert.False(Field(doc, "top_id").Nullable);
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
        Assert.Equal(["nullable-column"], Rules(doc, "low"));
        Assert.False(Field(doc, "c").Nullable);
    }

    [Fact]
    public void A_windowed_aggregate_not_modelled_is_unknown()
    {
        // MIN, MAX, SUM and COUNT over a window are read (slice 6i, FunctionTests), AVG and the statistics since 6j
        // (NumericTests); CHECKSUM_AGG is not, with a window or without.
        var doc = Procedure("SELECT CHECKSUM_AGG(t.id) OVER () AS m FROM dbo.t AS t;");

        Assert.Equal(IrUnknownReasons.UnsupportedExpression, Assert.Single(doc.Unknowns).Reason);
    }

    // ---- scalar subqueries ------------------------------------------------------------------------------------------

    [Fact]
    public void A_scalar_subquery_of_an_aggregate_without_group_by_always_has_its_one_row()
    {
        // A_scalar_subquery_of_an_aggregate_without_group_by_always_has_a_value_for_count.
        var doc = Procedure("""
            SELECT t.id,
                (SELECT COUNT_BIG(*) FROM dbo.u AS u WHERE u.t_id = t.id) AS lines,
                (SELECT COUNT(*) FROM dbo.u AS u WHERE u.t_id = t.id) AS small,
                (SELECT MAX(u.label) FROM dbo.u AS u WHERE u.t_id = t.id) AS last_label,
                (SELECT COUNT(*) FROM dbo.u AS u GROUP BY u.t_id HAVING u.t_id = t.id) AS grouped,
                (SELECT u.label FROM dbo.u AS u WHERE u.id = t.id) AS label
            FROM dbo.t AS t;
            """);

        Assert.False(Field(doc, "lines").Nullable);
        Assert.Equal(["count-overflow"], Rules(doc, "small"));
        Assert.Equal(["empty-aggregate"], Rules(doc, "last_label"));
        Assert.Equal(["count-overflow", "empty-subquery"], Rules(doc, "grouped"));
        Assert.Equal(["empty-subquery"], Rules(doc, "label"));
        Assert.Equal("nvarchar(10)", Assert.IsType<IrScalar>(Field(doc, "label").Type).Sql);
    }

    [Fact]
    public void A_scalar_subquery_without_from_has_its_one_row()
    {
        var doc = Procedure("SELECT (SELECT 1) AS one, (SELECT 1 WHERE 1 = 0) AS none_;");

        Assert.False(Field(doc, "one").Nullable);
        Assert.Equal(["empty-subquery"], Rules(doc, "none_"));
    }

    [Fact]
    public void A_for_json_subquery_over_one_row_is_never_null_and_has_one_item()
    {
        // For_json_over_an_aggregate_without_group_by_always_returns_its_one_object.
        var doc = Procedure("SELECT t.id, (SELECT COUNT_BIG(*) AS n FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH) AS stats FROM dbo.t AS t FOR JSON PATH;");

        var stats = Field(doc, "stats");
        Assert.False(stats.Nullable);
        var array = Assert.IsType<IrArray>(stats.Type);
        Assert.Equal(1, array.MinItems);
        Assert.Equal(1, array.MaxItems);
    }

    [Fact]
    public void A_for_json_subquery_that_returns_anything_has_at_least_one_item()
    {
        // Over no rows FOR JSON gives NULL, not [], so a value that is there has an item; one fixed by key has at most one.
        var doc = Procedure("""
            SELECT t.id,
                (SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH) AS lines,
                (SELECT u.id FROM dbo.u AS u WHERE u.id = t.id FOR JSON PATH) AS one
            FROM dbo.t AS t FOR JSON PATH;
            """);

        var lines = Assert.IsType<IrArray>(Field(doc, "lines").Type);
        Assert.Equal(1, lines.MinItems);
        Assert.Null(lines.MaxItems);
        var one = Assert.IsType<IrArray>(Field(doc, "one").Type);
        Assert.Equal(1, one.MinItems);
        Assert.Equal(1, one.MaxItems);
        Assert.Equal(["empty-subquery"], Rules(doc, "one"));
    }

    // ---- cardinality -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t;", IrCardinality.One)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t WHERE t.n > 1;", IrCardinality.One)]
    [InlineData("SELECT MAX(t.id) AS n, COUNT(*) AS c FROM dbo.t AS t;", IrCardinality.One)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t HAVING COUNT(*) > 1;", IrCardinality.AtMostOne)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t GROUP BY t.n;", IrCardinality.Many)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t GROUP BY ();", IrCardinality.Many)]
    [InlineData("SELECT 1 AS n;", IrCardinality.One)]
    [InlineData("SELECT 1 AS n WHERE @x = 1;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (1) t.id FROM dbo.t AS t ORDER BY t.id;", IrCardinality.AtMostOne)]
    [InlineData("SELECT TOP 1 t.id FROM dbo.t AS t;", IrCardinality.AtMostOne)]
    [InlineData("SELECT TOP (1) WITH TIES t.id FROM dbo.t AS t ORDER BY t.n;", IrCardinality.Many)]
    [InlineData("SELECT TOP (1) PERCENT t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (@x) t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (2) t.id FROM dbo.t AS t;", IrCardinality.Many)]
    [InlineData("SELECT TOP (1) COUNT(*) AS n FROM dbo.t AS t;", IrCardinality.One)]
    [InlineData("SELECT TOP (@x) COUNT(*) AS n FROM dbo.t AS t;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM dbo.t AS t ORDER BY t.id OFFSET 5 ROWS FETCH NEXT 1 ROWS ONLY;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM dbo.t AS t ORDER BY t.id OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY;", IrCardinality.Many)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t ORDER BY 1 OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;", IrCardinality.One)]
    [InlineData("SELECT COUNT(*) AS n FROM dbo.t AS t ORDER BY 1 OFFSET 1 ROWS;", IrCardinality.AtMostOne)]
    public void How_many_rows_a_query_returns(string query, IrCardinality expected) =>
        // An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows, An_aggregate_with_group_by_returns_no_row_over_no_rows,
        // Having_over_no_groups_returns_no_row_and_without_group_by_at_most_one, Top_1_and_fetch_1_give_at_most_one_row_but_with_ties_gives_more.
        Assert.Equal(expected, Rows(query, "@x int"));

    [Theory]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE @x = t.id AND t.n > 0;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = 5;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = @big;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = @code;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = 'ab';", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code IS NULL;", IrCardinality.AtMostOne)]
    [InlineData("SELECT p.v FROM dbo.pair AS p WHERE p.a = 1 AND p.b = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT p.a FROM dbo.pair AS p WHERE p.v IS NULL AND p.w = 'x';", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.note FROM dbo.u AS u JOIN dbo.t AS t ON t.id = u.t_id WHERE u.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.note FROM dbo.u AS u LEFT JOIN dbo.t AS t ON t.id = u.t_id WHERE u.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.note FROM dbo.u AS u, dbo.t AS t WHERE t.id = u.t_id AND u.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.n, COUNT(*) AS c FROM dbo.t AS t WHERE t.id = @x GROUP BY t.n;", IrCardinality.AtMostOne)]
    public void A_filter_fixing_a_whole_key_returns_at_most_one_row(string query, IrCardinality expected) =>
        // A_unique_constraint_allows_one_null_only: so `code IS NULL` fixes at most one row too.
        Assert.Equal(expected, Rows(query, "@x int, @big bigint, @code varchar(10)"));

    [Theory]
    [InlineData("SELECT p.v FROM dbo.pair AS p WHERE p.a = 1;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = @x OR t.id = 2;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id > @x;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id IN (1, 2);")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id = t.n;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id + 0 = @x;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = 5;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = N'ab';")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = @ncode;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.code = 'ab' COLLATE Latin1_General_BIN;")]
    [InlineData("SELECT t.name FROM dbo.t AS t WHERE t.id IS NULL OR t.id = @x;")]
    [InlineData("SELECT u.note FROM dbo.u AS u JOIN dbo.t AS t ON t.n = u.t_id WHERE u.id = @x;")]
    [InlineData("SELECT u.note FROM dbo.u AS u JOIN dbo.t AS t ON t.id = u.t_id;")]
    [InlineData("SELECT t.name FROM dbo.t AS t JOIN dbo.u AS u ON u.id = @x WHERE t.id = u.t_id OR 1 = 1;")]
    [InlineData("SELECT t.name FROM dbo.t AS t LEFT JOIN dbo.u AS u ON t.id = @x;")]
    public void A_filter_that_does_not_fix_a_whole_key_in_the_keys_own_type_returns_many(string query) =>
        // A_comparison_that_converts_the_key_can_match_several_rows, A_varchar_key_under_a_sql_collation_compared_with_nvarchar_can_match_several_rows.
        Assert.Equal(IrCardinality.Many, Rows(query, "@x int, @ncode nvarchar(10)"));

    [Theory]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL, INDEX ix_k_code UNIQUE (code));", IrCardinality.AtMostOne)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL, INDEX ix_k_code UNIQUE (code) WHERE code > 0);", IrCardinality.Many)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL, INDEX ix_k_code (code));", IrCardinality.Many)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL CONSTRAINT uq_k_code UNIQUE);", IrCardinality.AtMostOne)]
    [InlineData("CREATE TABLE dbo.k (id int NOT NULL, code int NOT NULL);", IrCardinality.Many)]
    public void Which_declarations_make_a_key(string table, IrCardinality expected)
    {
        // A filtered unique index is unique only among the rows its filter keeps.
        var doc = Infer(("k.sql", table), ("p.sql", "CREATE PROCEDURE dbo.p @x int AS SELECT k.id FROM dbo.k AS k WHERE k.code = @x;"));

        Assert.Equal(expected, Assert.Single(Results(doc)).Cardinality);
    }

    [Theory]
    [InlineData("ALTER INDEX uq_t_code ON dbo.t DISABLE;", IrCardinality.Many, IrCardinality.AtMostOne)]
    [InlineData("ALTER INDEX ALL ON dbo.t DISABLE;", IrCardinality.Many, IrCardinality.Many)]
    [InlineData("ALTER INDEX ix_other ON dbo.u DISABLE;", IrCardinality.AtMostOne, IrCardinality.AtMostOne)]
    [InlineData("CREATE PROCEDURE dbo.load AS ALTER INDEX uq_t_code ON t DISABLE;", IrCardinality.Many, IrCardinality.AtMostOne)]
    public void A_disabled_index_is_no_key(string disable, IrCardinality byCode, IrCardinality byId)
    {
        // A disabled unique index enforces nothing, so rows written meanwhile may share a key; wherever the statement is.
        var doc = Infer(("tables.sql", Tables), ("disable.sql", disable),
            ("p.sql", "CREATE PROCEDURE dbo.p @code varchar(10), @x int AS SELECT t.name FROM dbo.t AS t WHERE t.code = @code; SELECT t.name FROM dbo.t AS t WHERE t.id = @x;"));

        var results = doc.Objects.Single(o => o.Id == "dbo.p").Results!;
        Assert.Equal(byCode, results[0].Cardinality);
        Assert.Equal(byId, results[1].Cardinality);
    }

    [Fact]
    public void For_json_over_one_row_is_never_absent()
    {
        // For_json_over_an_aggregate_without_group_by_always_returns_its_one_object.
        var result = Assert.Single(Results(Procedure("SELECT COUNT_BIG(*) AS n FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;")));

        Assert.Equal(IrCardinality.One, result.Cardinality);
        Assert.False(result.MayBeAbsent);
    }

    [Fact]
    public void For_json_by_key_may_be_absent_and_returns_at_most_one_object()
    {
        var result = Assert.Single(Results(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;", "@x int")));

        Assert.Equal(IrCardinality.AtMostOne, result.Cardinality);
        Assert.True(result.MayBeAbsent);
    }

    [Fact]
    public void Results_at_one_position_join_their_cardinalities()
    {
        var doc = Procedure("""
            IF @x = 1 SELECT COUNT(*) AS n FROM dbo.t AS t;
            ELSE SELECT t.id AS n FROM dbo.t AS t WHERE t.id = @x;
            IF @x = 2 SELECT COUNT(*) AS n FROM dbo.t AS t; ELSE SELECT COUNT_BIG(*) AS n FROM dbo.t AS t;
            """, "@x int");

        Assert.Equal([IrCardinality.AtMostOne, IrCardinality.One], Results(doc).Select(r => r.Cardinality));
    }

    [Fact]
    public void A_callees_result_keeps_its_cardinality()
    {
        var doc = Infer(("tables.sql", Tables),
            ("q.sql", "CREATE PROCEDURE dbo.q @x int AS SELECT t.name FROM dbo.t AS t WHERE t.id = @x;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS EXEC dbo.q 1;"));

        Assert.All(doc.Objects, o => Assert.Equal(IrCardinality.AtMostOne, Assert.Single(o.Results!).Cardinality));
    }

    // ---- the IR and the Zod ----------------------------------------------------------------------------------------

    [Fact]
    public void The_ir_writes_each_results_cardinality()
    {
        var json = IrJsonWriter.Write(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x;", "@x int"));

        Assert.Contains("\"irVersion\": \"0.15\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cardinality\": \"atMostOne\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_zod_bounds_a_rowset_by_its_cardinality()
    {
        var one = ZodEmitter.Emit(Procedure("SELECT COUNT_BIG(*) AS n FROM dbo.t AS t;"));
        var atMostOne = ZodEmitter.Emit(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x;", "@x int"));
        var mayNotRun = ZodEmitter.Emit(Procedure("IF @x = 1 SELECT COUNT_BIG(*) AS n FROM dbo.t AS t;", "@x int"));
        var json = ZodEmitter.Emit(Procedure("SELECT t.name FROM dbo.t AS t WHERE t.id = @x FOR JSON PATH;", "@x int"));

        Assert.Contains("export const PSchema = z.array(PRowSchema).length(1)\n", one, StringComparison.Ordinal);
        Assert.Contains("export const PSchema = z.array(PRowSchema).max(1)\n", atMostOne, StringComparison.Ordinal);
        Assert.Contains("export const PSchema = z.array(PRowSchema).max(1)\n", mayNotRun, StringComparison.Ordinal);
        Assert.Contains("export const PSchema = z.array(PRowSchema).max(1)\n", json, StringComparison.Ordinal);
    }
}

using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// <c>CROSS APPLY</c>, <c>OUTER APPLY</c>, <c>VALUES</c> lists and views (slice 6e). The engine rules each test leans on are
/// observed in the differential tests' <c>ApplyViewObservationTests</c> (and <c>RefinementObservationTests</c> for the
/// aggregate and cardinality rules reused here), named where they matter.
/// </summary>
public class ApplyViewTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, n int NULL, code varchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, note nvarchar(10) NULL, label nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.pair (a int NOT NULL, b int NOT NULL, v int NULL, CONSTRAINT pk_pair PRIMARY KEY (a, b));
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);

        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse("dbo.p")]).Procedures);
    }

    private static IrDocument Procedure(string body, string parameters = "", string views = "") =>
        views.Length == 0
            ? Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"))
            : Infer(("tables.sql", Tables), ("views.sql", views), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    private static IReadOnlyList<IrResult> Results(IrDocument doc) =>
        doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));

    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => type switch
    {
        IrArray { Items: var items } => Fields(doc, items),
        IrRef r => doc.Types[r.Name].Fields,
        var other => throw new InvalidOperationException($"Unexpected type {other}."),
    };

    private static IrField Field(IrDocument doc, string name) => Assert.Single(Fields(doc, Assert.Single(Results(doc)).Type), f => f.Name == name);

    private static string[] Rules(IrDocument doc, string name) => [.. Field(doc, name).NullableBecause.Select(r => r.Rule)];

    private static IrUnknown Unknown(IrDocument doc, string path) =>
        Assert.Single(doc.Unknowns, u => u.Path == path);

    private static void Typed(IrDocument doc) =>
        Assert.True(doc.Unknowns.Count == 0, "Unknowns: " + string.Join("; ", doc.Unknowns.Select(u => $"{u.Path}: {u.Detail}")));

    private static IrCardinality Rows(IrDocument doc) => Assert.Single(Results(doc)).Cardinality;

    // ---- APPLY: nullability ------------------------------------------------------------------------------------------

    [Fact]
    public void Cross_apply_keeps_the_right_sides_own_nullability()
    {
        // Cross_apply_keeps_only_outer_rows_with_a_right_row_and_outer_apply_fills_the_others_with_null: CROSS APPLY never
        // null-extends, so a right-side column is null only when its own value can be.
        var doc = Procedure("SELECT t.id, x.note, x.label FROM dbo.t AS t CROSS APPLY (SELECT u.note, u.label FROM dbo.u AS u WHERE u.t_id = t.id) AS x;");

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "note"));
        Assert.Empty(Rules(doc, "label"));
    }

    [Fact]
    public void Outer_apply_makes_every_right_side_column_nullable()
    {
        var doc = Procedure("SELECT t.id, x.note, x.label FROM dbo.t AS t OUTER APPLY (SELECT u.note, u.label FROM dbo.u AS u WHERE u.t_id = t.id) AS x;");

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
        Assert.Equal([IrNullRules.NullableColumn, IrNullRules.OuterApply], Rules(doc, "note"));
        Assert.Equal([IrNullRules.OuterApply], Rules(doc, "label"));
        Assert.Equal("p.sql:2:58", Field(doc, "label").NullableBecause[0].At);
    }

    [Fact]
    public void Outer_apply_of_a_table_makes_its_columns_nullable()
    {
        var doc = Procedure("SELECT t.id, u.label FROM dbo.t AS t OUTER APPLY dbo.u AS u;");

        Typed(doc);
        Assert.Equal([IrNullRules.OuterApply], Rules(doc, "label"));
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
        Assert.Equal([IrNullRules.EmptyAggregate], Rules(doc, "m"));
    }

    [Fact]
    public void A_grouped_aggregate_applied_may_have_no_row()
    {
        // A_grouped_aggregate_applied_gives_no_row_for_an_outer_row_without_rows.
        var doc = Procedure("SELECT t.id, a.n FROM dbo.t AS t OUTER APPLY (SELECT COUNT_BIG(*) AS n FROM dbo.u AS u WHERE u.t_id = t.id GROUP BY ()) AS a;");

        Assert.Equal([IrNullRules.OuterApply], Rules(doc, "n"));
    }

    [Fact]
    public void An_aggregate_applied_with_having_may_have_no_row()
    {
        var doc = Procedure("SELECT t.id, a.n FROM dbo.t AS t OUTER APPLY (SELECT COUNT_BIG(*) AS n FROM dbo.u AS u WHERE u.t_id = t.id HAVING COUNT_BIG(*) > 1) AS a;");

        Assert.Equal([IrNullRules.OuterApply], Rules(doc, "n"));
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
        Assert.Equal([IrNullRules.EmptySubquery], lines.NullableBecause.Select(r => r.Rule));
        var items = forJson.Length > 0 ? Assert.IsType<IrArray>(lines.Type) : Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(lines.Type).Content);
        Assert.Equal(["id", "label"], Fields(doc, items).Select(f => f.Name));
    }

    [Fact]
    public void Outer_apply_inside_an_outer_joins_optional_side_is_null_for_the_outer_join()
    {
        var doc = Procedure("SELECT t.id, x.label FROM dbo.t AS t LEFT JOIN (dbo.pair AS p OUTER APPLY (SELECT u.label FROM dbo.u AS u WHERE u.t_id = p.a) AS x) ON p.a = t.id;");

        Typed(doc);
        Assert.Equal([IrNullRules.OuterJoin], Rules(doc, "label"));
    }

    // ---- APPLY: refinement ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_filter_inside_the_applied_query_does_not_refine_the_outer_columns()
    {
        // As for a correlated subquery: the filter holds for the applied query's rows, not for the outer row read outside it.
        var doc = Procedure("SELECT t.name FROM dbo.t AS t CROSS APPLY (SELECT u.note FROM dbo.u AS u WHERE t.name IS NOT NULL AND u.t_id = t.id) AS x;");

        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "name"));
    }

    [Fact]
    public void An_outer_column_read_in_an_applied_aggregate_is_not_refined_by_its_where()
    {
        // An_outer_column_read_in_an_applied_aggregate_is_not_filtered_by_its_where (6c's rule for aggregate subqueries).
        var doc = Procedure("SELECT a.tn FROM dbo.t AS t CROSS APPLY (SELECT t.name AS tn, COUNT_BIG(*) AS n FROM dbo.u AS u WHERE t.name IS NOT NULL AND u.t_id = t.id) AS a;");

        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "tn"));
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
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "note"));
    }

    // ---- APPLY: what the right side sees ---------------------------------------------------------------------------------

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

        Assert.Equal(IrUnknownReasons.UnresolvedColumn, Unknown(doc, "results[0][].code").Reason);
    }

    [Fact]
    public void Apply_of_a_table_valued_function_is_not_modelled_yet()
    {
        var doc = Infer(
            ("tables.sql", Tables),
            ("f.sql", "CREATE FUNCTION dbo.f(@id int) RETURNS TABLE AS RETURN SELECT u.label FROM dbo.u AS u WHERE u.t_id = @id;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.id, f.label FROM dbo.t AS t CROSS APPLY dbo.f(t.id) AS f;"));

        Assert.Equal(IrUnknownReasons.UnsupportedSource, Unknown(doc, "results[0][].label").Reason);
    }

    // ---- APPLY: cardinality ----------------------------------------------------------------------------------------------

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

    // ---- VALUES ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_values_list_of_one_row_names_its_values()
    {
        var doc = Procedure("SELECT t.id, v.c, v.nm FROM dbo.t AS t CROSS APPLY (VALUES (t.code, t.name)) AS v (c, nm);");

        Typed(doc);
        Assert.Empty(Rules(doc, "c"));
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "nm"));
    }

    [Fact]
    public void Outer_apply_of_a_values_list_never_null_extends()
    {
        // A VALUES list always has its rows.
        var doc = Procedure("SELECT t.id, v.c FROM dbo.t AS t OUTER APPLY (VALUES (t.code), (t.code)) AS v (c);");

        Assert.Empty(Rules(doc, "c"));
    }

    [Fact]
    public void A_values_column_whose_rows_have_the_same_type_has_it_and_is_null_when_any_is()
    {
        var doc = Procedure("SELECT v.c FROM dbo.t AS t CROSS APPLY (VALUES (t.n), (t.id)) AS v (c);");

        Typed(doc);
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "c"));
        Assert.Equal((IrScalarKind.Number, "int"), (Assert.IsType<IrScalar>(Field(doc, "c").Type).Kind, Assert.IsType<IrScalar>(Field(doc, "c").Type).Sql));
    }

    [Fact]
    public void A_values_column_whose_rows_differ_in_type_is_unknown()
    {
        // A_values_column_takes_the_type_of_highest_precedence_among_its_rows: the values are converted, which is not modelled.
        var doc = Procedure("SELECT v.c FROM dbo.t AS t CROSS APPLY (VALUES (t.name), (t.code)) AS v (c);");

        Assert.Equal(IrUnknownReasons.UnsupportedExpression, Unknown(doc, "results[0][].c").Reason);
    }

    [Theory]
    [InlineData("('width', t.n), ('height', t.id)", "varchar", 6)]
    [InlineData("('a', t.n), ('', t.id)", "varchar", 1)]
    [InlineData("('', t.n)", "varchar", 1)]
    [InlineData("('ab', t.n), (N'xyz', t.id)", "nvarchar", 3)]
    public void A_values_column_of_string_literals_is_a_non_null_string_of_the_longest(string rows, string sql, int length)
    {
        // A_values_column_of_string_literals_is_a_string_of_the_longest: the unpivot idiom's label column.
        var doc = Procedure($"SELECT v.label, v.size FROM dbo.t AS t CROSS APPLY (VALUES {rows}) AS v (label, size);");

        Assert.Empty(Rules(doc, "label"));
        var scalar = Assert.IsType<IrScalar>(Field(doc, "label").Type);
        Assert.Equal($"{sql}({length})", scalar.Sql);
        Assert.Equal(length, scalar.Facets?.Length?.Value);
    }

    [Theory]
    [InlineData("('caf\u00e9'), ('tea')")]
    [InlineData("(t.name), (N'zz')")]
    public void A_values_column_of_other_strings_is_unknown(string rows) =>
        Assert.Equal(IrUnknownReasons.UnsupportedExpression, Unknown(Procedure($"SELECT v.label FROM dbo.t AS t CROSS APPLY (VALUES {rows}) AS v (label);"), "results[0][].label").Reason);

    [Fact]
    public void A_values_list_in_from_is_modelled_too()
    {
        var doc = Procedure("SELECT s.id FROM (VALUES (1), (2)) AS s (id);");

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
    }

    // ---- views ------------------------------------------------------------------------------------------------------------

    private const string View = """
        CREATE VIEW dbo.v AS SELECT t.id, t.name, t.code FROM dbo.t AS t;
        GO
        """;

    [Theory]
    [InlineData("dbo.v AS x")]
    [InlineData("v AS x")]
    [InlineData("dbo.v AS x WITH (NOEXPAND)")]
    public void A_views_columns_are_typed_from_its_definition(string reference)
    {
        var doc = Procedure($"SELECT x.id, x.name FROM {reference};", views: View);

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "name"));
        Assert.Equal("dbo.t.name", Field(doc, "name").NullableBecause[0].At);
    }

    [Fact]
    public void A_views_column_list_names_its_columns()
    {
        var doc = Procedure("SELECT x.a, x.b FROM dbo.w AS x;", views: "CREATE VIEW dbo.w (a, b) WITH SCHEMABINDING AS SELECT t.id, t.name FROM dbo.t AS t;");

        Typed(doc);
        Assert.Empty(Rules(doc, "a"));
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "b"));
    }

    [Fact]
    public void A_view_over_a_view_is_typed()
    {
        var doc = Procedure("SELECT x.code FROM dbo.w AS x;", views: View + "\nCREATE VIEW dbo.w AS SELECT v.code FROM dbo.v AS v WHERE v.name IS NOT NULL;");

        Typed(doc);
        Assert.Empty(Rules(doc, "code"));
    }

    [Fact]
    public void A_views_where_refines_its_columns()
    {
        var doc = Procedure("SELECT x.name FROM dbo.w AS x;", views: "CREATE VIEW dbo.w AS SELECT t.name FROM dbo.t AS t WHERE t.name IS NOT NULL;");

        Assert.Empty(Rules(doc, "name"));
    }

    [Fact]
    public void A_view_created_with_ansi_nulls_off_keeps_it()
    {
        // A_view_keeps_ansi_nulls_as_it_was_when_it_was_created: t.name = N'a' does not prove t.name non-null there.
        var doc = Procedure("SELECT x.name FROM dbo.w AS x;", views: "SET ANSI_NULLS OFF;\nGO\nCREATE VIEW dbo.w AS SELECT t.name FROM dbo.t AS t WHERE t.name = N'a';\nGO\nSET ANSI_NULLS ON;\nGO\n");

        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "name"));
    }

    [Fact]
    public void Joins_and_apply_inside_a_view_keep_their_rules()
    {
        var doc = Procedure(
            "SELECT x.id, x.label, x.top_note FROM dbo.w AS x;",
            views: """
                CREATE VIEW dbo.w AS
                SELECT t.id, u.label, n.top_note
                FROM dbo.t AS t
                LEFT JOIN dbo.u AS u ON u.t_id = t.id
                OUTER APPLY (SELECT TOP (1) z.label AS top_note FROM dbo.u AS z WHERE z.t_id = t.id ORDER BY z.id) AS n;
                """);

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
        Assert.Equal([IrNullRules.OuterJoin], Rules(doc, "label"));
        Assert.Equal([IrNullRules.OuterApply], Rules(doc, "top_note"));
    }

    [Fact]
    public void A_view_on_the_optional_side_of_an_outer_join_is_nullable()
    {
        var doc = Procedure("SELECT u.id, x.code FROM dbo.u AS u LEFT JOIN dbo.v AS x ON x.id = u.t_id;", views: View);

        Assert.Equal([IrNullRules.OuterJoin], Rules(doc, "code"));
    }

    [Fact]
    public void Select_star_in_a_view_is_its_tables_columns_in_order()
    {
        // A_view_binds_select_star_when_it_is_created: haruspex assumes the view was bound against the tables as the project
        // defines them.
        var doc = Procedure(
            "SELECT x.id, x.name, x.n, x.code, x.label FROM dbo.w AS x;",
            views: "CREATE VIEW dbo.w AS SELECT t.*, u.label FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;");

        Typed(doc);
        Assert.Equal(["id", "name", "n", "code", "label"], Fields(doc, Assert.Single(Results(doc)).Type).Select(f => f.Name));
    }

    [Fact]
    public void Select_star_in_a_view_expands_every_source_in_order()
    {
        // Ten columns: t's four, d's four and k's two, in that order.
        var proc = Procedure(
            "SELECT x.a, x.e, x.j FROM dbo.w AS x;",
            views: "CREATE VIEW dbo.w (a, b, c, d, e, f, g, h, i, j) AS SELECT * FROM dbo.t AS t JOIN (SELECT u.id, u.t_id, u.note, u.label FROM dbo.u AS u) AS d ON d.t_id = t.id CROSS APPLY (VALUES (1, 2)) AS k (k1, k2);");
        Typed(proc);
        Assert.Empty(Rules(proc, "e"));
        Assert.Empty(Rules(proc, "j"));
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.h (id int NOT NULL, since datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL, upto datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL, PERIOD FOR SYSTEM_TIME (since, upto));")]
    [InlineData("CREATE TABLE dbo.h (id int NOT NULL) AS NODE;")]
    public void Select_star_over_a_table_whose_star_differs_from_its_columns_is_unknown(string table)
    {
        var doc = Infer(
            ("tables.sql", Tables + "\n" + table),
            ("views.sql", "CREATE VIEW dbo.w AS SELECT * FROM dbo.h AS h;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT x.id FROM dbo.w AS x;"));

        Assert.Equal(IrUnknownReasons.UnsupportedQuery, Unknown(doc, "results[0][].id").Reason);
    }

    [Theory]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.id FROM dbo.t AS t UNION ALL SELECT u.id FROM dbo.u AS u;", IrUnknownReasons.UnsupportedQuery)]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.id FROM dbo.t AS t;\nGO\nALTER VIEW dbo.w AS SELECT u.id FROM dbo.u AS u;", IrUnknownReasons.UnresolvedTable)]
    [InlineData("CREATE VIEW dbo.w AS SELECT y.id FROM dbo.y AS y;\nGO\nCREATE VIEW dbo.y AS SELECT w.id FROM dbo.w AS w;", IrUnknownReasons.UnresolvedTable)]
    public void A_view_that_cannot_be_read_is_unknown(string views, string reason) =>
        Assert.Equal(reason, Unknown(Procedure("SELECT x.id FROM dbo.w AS x;", views: views), "results[0][].id").Reason);

    [Fact]
    public void A_view_outside_the_project_stays_unknown() =>
        Assert.Equal(IrUnknownReasons.UnresolvedTable, Unknown(Procedure("SELECT x.id FROM dbo.nowhere AS x;"), "results[0][].id").Reason);

    // ---- views: keys ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.id, t.name FROM dbo.t AS t WHERE t.n > 0;", "x.id = @id", IrCardinality.AtMostOne)]
    [InlineData("CREATE VIEW dbo.w (k, name) AS SELECT t.id, t.name FROM dbo.t AS t;", "x.k = @id", IrCardinality.AtMostOne)]
    [InlineData("CREATE VIEW dbo.w AS SELECT * FROM dbo.t AS t;", "x.id = @id", IrCardinality.AtMostOne)]
    [InlineData("CREATE VIEW dbo.w AS SELECT p.a, p.b AS id FROM dbo.pair AS p;", "x.a = @id AND x.id = 1", IrCardinality.AtMostOne)]
    [InlineData("CREATE VIEW dbo.w AS SELECT p.a, p.v AS id FROM dbo.pair AS p;", "x.a = @id", IrCardinality.Many)]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.id, u.label AS name FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;", "x.id = @id", IrCardinality.Many)]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.id, t.name FROM dbo.t AS t GROUP BY ROLLUP (t.id, t.name);", "x.id = @id", IrCardinality.Many)]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.n AS id, t.name FROM dbo.t AS t;", "x.id = @id", IrCardinality.Many)]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.id, t.name FROM dbo.t AS t;", "x.id = @id OR x.id = 2", IrCardinality.Many)]
    public void A_view_over_one_table_that_selects_a_whole_key_has_that_key(string view, string where, IrCardinality expected)
    {
        // A_view_over_one_table_returns_each_of_its_rows_at_most_once.
        var doc = Procedure($"SELECT x.id FROM dbo.w AS x WHERE {where};", "@id int", view);

        Assert.Equal(expected, Rows(doc));
    }

    private const string Temporal = """
        CREATE TABLE dbo.h (
            id int NOT NULL CONSTRAINT pk_h PRIMARY KEY, name nvarchar(10) NOT NULL,
            vf datetime2 GENERATED ALWAYS AS ROW START NOT NULL, vt datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
            PERIOD FOR SYSTEM_TIME (vf, vt))
        WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.h_history));
        GO
        CREATE VIEW dbo.h_all AS SELECT h.id, h.name FROM dbo.h FOR SYSTEM_TIME ALL AS h;
        GO
        CREATE VIEW dbo.h_now AS SELECT h.id, h.name FROM dbo.h AS h;
        GO
        """;

    [Theory]
    [InlineData("dbo.h FOR SYSTEM_TIME ALL AS x", IrCardinality.Many)]
    [InlineData("dbo.h FOR SYSTEM_TIME BETWEEN '2020-01-01' AND '2021-01-01' AS x", IrCardinality.Many)]
    [InlineData("dbo.h FOR SYSTEM_TIME FROM '2020-01-01' TO '2021-01-01' AS x", IrCardinality.Many)]
    [InlineData("dbo.h FOR SYSTEM_TIME CONTAINED IN ('2020-01-01', '2021-01-01') AS x", IrCardinality.Many)]
    [InlineData("dbo.h FOR SYSTEM_TIME AS OF '2020-01-01' AS x", IrCardinality.AtMostOne)]
    [InlineData("dbo.h AS x", IrCardinality.AtMostOne)]
    [InlineData("dbo.h_all AS x", IrCardinality.Many)]
    [InlineData("dbo.h_now FOR SYSTEM_TIME ALL AS x", IrCardinality.Many)]
    [InlineData("dbo.h_now FOR SYSTEM_TIME AS OF '2020-01-01' AS x", IrCardinality.AtMostOne)]
    [InlineData("dbo.h_now AS x", IrCardinality.AtMostOne)]
    public void A_key_does_not_hold_over_history(string from, IrCardinality expected)
    {
        // For_system_time_other_than_as_of_repeats_a_key_through_a_table_or_a_view.
        var doc = Infer(("tables.sql", Tables + "\n" + Temporal), ("p.sql", $"CREATE PROCEDURE dbo.p @id int AS SELECT x.name FROM {from} WHERE x.id = @id;"));

        Assert.Equal(expected, Rows(doc));
    }

    [Fact]
    public void A_keyed_view_fixes_a_join_as_a_table_does() =>
        Assert.Equal(
            IrCardinality.AtMostOne,
            Rows(Procedure("SELECT u.label, x.name FROM dbo.u AS u JOIN dbo.v AS x ON x.id = u.t_id WHERE u.id = @id;", "@id int", View)));
}

using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class ApplyViewTests
{
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
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "name"));
        Assert.Equal("dbo.t.name", Field(doc, "name").NullableBecause[0].At);
    }

    [Fact]
    public void A_views_column_list_names_its_columns()
    {
        var doc = Procedure("SELECT x.a, x.b FROM dbo.w AS x;", views: "CREATE VIEW dbo.w (a, b) WITH SCHEMABINDING AS SELECT t.id, t.name FROM dbo.t AS t;");

        Typed(doc);
        Assert.Empty(Rules(doc, "a"));
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "b"));
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

        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "name"));
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
        Assert.Equal([IrNullRule.UnmatchedRow], Rules(doc, "label"));
        Assert.Equal([IrNullRule.UnmatchedLateralRow], Rules(doc, "top_note"));
    }

    [Fact]
    public void A_view_on_the_optional_side_of_an_outer_join_is_nullable()
    {
        var doc = Procedure("SELECT u.id, x.code FROM dbo.u AS u LEFT JOIN dbo.v AS x ON x.id = u.t_id;", views: View);

        Assert.Equal([IrNullRule.UnmatchedRow], Rules(doc, "code"));
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

    [Fact]
    public void Select_star_in_a_view_over_a_graph_table_is_unknown()
    {
        // A_graph_tables_star_adds_pseudo_columns_named_by_the_server. A hidden column is left out (SelectStarTests).
        var doc = Infer(
            ("tables.sql", Tables + "\nCREATE TABLE dbo.h (id int NOT NULL) AS NODE;"),
            ("views.sql", "CREATE VIEW dbo.w AS SELECT * FROM dbo.h AS h;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT x.id FROM dbo.w AS x;"));

        Assert.Equal(IrUnknownReason.UnsupportedQuery, Unknown(doc, "results[0][].id").Reason);
    }

    [Theory]
    [InlineData("CREATE VIEW dbo.w AS SELECT t.id FROM dbo.t AS t;\nGO\nALTER VIEW dbo.w AS SELECT u.id FROM dbo.u AS u;", IrUnknownReason.UnresolvedTable)]
    [InlineData("CREATE VIEW dbo.w AS SELECT y.id FROM dbo.y AS y;\nGO\nCREATE VIEW dbo.y AS SELECT w.id FROM dbo.w AS w;", IrUnknownReason.UnresolvedTable)]
    public void A_view_that_cannot_be_read_is_unknown(string views, IrUnknownReason reason) =>
        Assert.Equal(reason, Unknown(Procedure("SELECT x.id FROM dbo.w AS x;", views: views), "results[0][].id").Reason);

    [Fact]
    public void A_view_outside_the_project_stays_unknown() =>
        Assert.Equal(IrUnknownReason.UnresolvedTable, Unknown(Procedure("SELECT x.id FROM dbo.nowhere AS x;"), "results[0][].id").Reason);

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
        CREATE VIEW dbo.h_one AS SELECT h.id, h.name FROM dbo.h AS h WHERE h.id = 1;
        GO
        CREATE VIEW dbo.h_count AS SELECT COUNT_BIG(*) AS id, MAX(h.name) AS name FROM dbo.h AS h;
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
    [InlineData("(SELECT 1 AS id) AS k CROSS APPLY (SELECT o.name FROM dbo.h_one AS o) AS x", IrCardinality.AtMostOne)]
    [InlineData("(SELECT 1 AS id) AS k CROSS APPLY (SELECT o.name FROM dbo.h_one FOR SYSTEM_TIME ALL AS o) AS x", IrCardinality.Many)]
    [InlineData("(SELECT 1 AS id) AS k CROSS APPLY (SELECT c.name FROM dbo.h_count FOR SYSTEM_TIME ALL AS c) AS x", IrCardinality.AtMostOne)]
    public void A_key_does_not_hold_over_history(string from, IrCardinality expected)
    {
        // For_system_time_other_than_as_of_repeats_a_key_through_a_table_or_a_view.
        // A view of at most one row because its WHERE fixes a key repeats that row once per version under history; one of
        // exactly one row (an ungrouped aggregate) stays one. Where the filter names k, the outer source is fixed already.
        var where = from.StartsWith("(SELECT", StringComparison.Ordinal) ? "k.id = @id" : "x.id = @id";
        var doc = Infer(("tables.sql", Tables + "\n" + Temporal), ("p.sql", $"CREATE PROCEDURE dbo.p @id int AS SELECT x.name FROM {from} WHERE {where};"));

        Assert.Equal(expected, Rows(doc));
    }

    [Fact]
    public void A_keyed_view_fixes_a_join_as_a_table_does() =>
        Assert.Equal(
            IrCardinality.AtMostOne,
            Rows(Procedure("SELECT u.label, x.name FROM dbo.u AS u JOIN dbo.v AS x ON x.id = u.t_id WHERE u.id = @id;", "@id int", View)));
}

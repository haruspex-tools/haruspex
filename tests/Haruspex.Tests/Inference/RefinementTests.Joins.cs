using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
    [Fact]
    public void An_inner_joins_on_refines_both_sides()
    {
        var doc = Procedure("SELECT t.n, u.t_id, u.note FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.n;");

        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "t_id").Nullable);
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "note"));
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
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "note"));
    }

    [Fact]
    public void An_outer_joins_on_refines_only_its_optional_side_and_keeps_the_outer_join()
    {
        // A_condition_in_the_on_of_an_outer_join_never_drops_a_row_of_the_kept_side.
        var doc = Procedure("SELECT t.n, u.t_id, u.note FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.n AND t.name = N'a';");

        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "n"));
        Assert.Equal([IrNullRule.OuterJoin], Rules(doc, "t_id"));
        Assert.Equal([IrNullRule.NullableColumn, IrNullRule.OuterJoin], Rules(doc, "note"));
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
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "note"));
    }

    [Fact]
    public void An_inner_joins_on_inside_the_optional_side_keeps_the_outer_join()
    {
        var doc = Procedure("""
            SELECT u.t_id, p.v FROM dbo.t AS t
            LEFT JOIN (dbo.u AS u JOIN dbo.pair AS p ON p.v = u.t_id) ON u.id = t.id;
            """);

        Assert.Equal([IrNullRule.OuterJoin], Rules(doc, "t_id"));
        Assert.Equal([IrNullRule.OuterJoin], Rules(doc, "v"));
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
        Assert.Equal([IrNullRule.OuterJoin], Rules(doc, "a"));
    }
}

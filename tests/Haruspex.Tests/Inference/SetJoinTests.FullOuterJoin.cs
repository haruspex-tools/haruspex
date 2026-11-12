using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class SetJoinTests
{
    [Fact]
    public void A_full_join_makes_both_sides_nullable()
    {
        // A_full_outer_join_null_extends_both_sides_where_nothing_matches.
        var doc = Procedure("SELECT t.id AS t_id, t.x, u.id AS u_id FROM dbo.t AS t FULL OUTER JOIN dbo.u AS u ON u.t_id = t.id;");
        AllTyped(doc);
        Assert.Equal("int unmatched-row", Typed(doc, "t_id"));
        Assert.Equal("int nullable-column unmatched-row", Typed(doc, "x"));
        Assert.Equal("int unmatched-row", Typed(doc, "u_id"));
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }

    [Fact]
    public void A_full_joins_on_proves_nothing_of_either_side()
    {
        // A_full_outer_joins_on_proves_nothing_of_either_side.
        var doc = Procedure("SELECT t.x, u.note FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.t_id = t.id AND t.x IS NOT NULL AND u.note IS NOT NULL;");
        Assert.Equal("int nullable-column unmatched-row", Typed(doc, "x"));
        Assert.Equal("varchar(20) nullable-column unmatched-row", Typed(doc, "note"));
    }

    [Fact]
    public void A_where_on_one_side_of_a_full_join_proves_that_sides_row()
    {
        // A_where_on_one_side_of_a_full_join_drops_that_sides_unmatched_rows_only.
        var doc = Procedure("SELECT t.id AS t_id, t.x, u.id AS u_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.t_id = t.id WHERE t.id > 0;");
        Assert.Equal("int", Typed(doc, "t_id"));
        Assert.Equal("int nullable-column", Typed(doc, "x"));
        Assert.Equal("int unmatched-row", Typed(doc, "u_id"));

        // An inner join after it on the other side proves that one.
        doc = Procedure("SELECT t.id AS t_id, u.id AS u_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.t_id = t.id JOIN dbo.u AS w ON w.id = u.id;");
        Assert.Equal("int unmatched-row", Typed(doc, "t_id"));
        Assert.Equal("int", Typed(doc, "u_id"));
    }

    [Theory]
    [InlineData("SELECT a.id, b.id AS b_id FROM (SELECT t.id FROM dbo.t AS t WHERE t.id = 1) AS a FULL JOIN (SELECT u.id FROM dbo.u AS u WHERE u.id = 4) AS b ON b.id = a.id;")]
    [InlineData("SELECT t.id, u.id AS b_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.id = t.id WHERE u.id IS NULL AND t.id = 1;")]
    [InlineData("SELECT t.id, u.id AS b_id FROM dbo.t AS t FULL JOIN dbo.u AS u ON u.id = t.id WHERE t.id = 1;")]
    public void A_full_join_is_never_bounded_by_its_sides(string query) =>
        // A_full_join_of_two_queries_of_at_most_one_row_can_return_two.
        Assert.Equal(IrCardinality.Many, Rows(Procedure(query)));

    [Fact]
    public void Right_join_stays_unmodelled()
    {
        var doc = Procedure("SELECT t.id FROM dbo.t AS t RIGHT JOIN dbo.u AS u ON u.t_id = t.id;");
        Assert.IsType<IrUnknownType>(Field(doc, "id").Type);
    }
}

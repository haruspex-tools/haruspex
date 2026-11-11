using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class SetJoinTests
{
    [Fact]
    public void A_cross_join_reads_both_sides_as_they_are()
    {
        var doc = Procedure("SELECT t.id, t.x, u.note FROM dbo.t AS t CROSS JOIN dbo.u AS u;");
        AllTyped(doc);
        Assert.Equal("int", Typed(doc, "id"));
        Assert.Equal("int nullable-column", Typed(doc, "x"));
        Assert.Equal("varchar(20) nullable-column", Typed(doc, "note"));
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }

    [Theory]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT COUNT(*) AS m FROM dbo.u) AS b;", IrCardinality.One)]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT COUNT(*) AS m FROM dbo.u) AS b WHERE a.n > 0;", IrCardinality.AtMostOne)]
    [InlineData("SELECT a.n, b.id FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN dbo.u AS b WHERE b.id = 1;", IrCardinality.AtMostOne)]
    [InlineData("SELECT a.n, b.id FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN dbo.u AS b;", IrCardinality.Many)]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT TOP (1) u.id AS m FROM dbo.u AS u) AS b;", IrCardinality.AtMostOne)]
    [InlineData("SELECT a.n, b.m FROM (SELECT COUNT(*) AS n FROM dbo.t) AS a CROSS JOIN (SELECT COUNT(*) AS m FROM dbo.u) AS b, (SELECT 1 AS k) AS c;", IrCardinality.One)]
    public void A_cross_join_is_one_row_only_when_both_sides_are(string query, IrCardinality expected) =>
        // A_cross_join_of_two_one_row_queries_is_one_row_and_with_an_empty_side_none.
        Assert.Equal(expected, Rows(Procedure(query)));
}

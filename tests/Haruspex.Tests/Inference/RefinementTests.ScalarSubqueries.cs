using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class RefinementTests
{
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
}

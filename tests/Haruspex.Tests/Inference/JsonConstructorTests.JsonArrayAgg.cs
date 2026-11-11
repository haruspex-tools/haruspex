using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class JsonConstructorTests
{
    [Fact]
    public void Json_arrayagg_without_group_by_is_null_over_no_rows()
    {
        var doc = Procedure("SELECT json_arrayagg(t.id ORDER BY t.id) AS v FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;");
        var v = Field(doc, "v");

        Assert.Equal(["empty-aggregate"], Rules(v.NullableBecause));
        var array = Assert.IsType<IrArray>(v.Type);
        Assert.False(array.ItemsNullable);
        // A non-null result means a row, and a value never null is never dropped.
        Assert.Equal(1, array.MinItems);
        Assert.Null(array.MaxItems);
    }

    [Fact]
    public void Under_group_by_every_group_has_a_row_so_it_is_never_null()
    {
        var doc = Procedure("SELECT u.t_id AS id, json_arrayagg(u.qty) AS v FROM dbo.u AS u GROUP BY u.t_id FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Empty(v.NullableBecause);
        // qty may be null, and ABSENT ON NULL drops it, so the array can be empty.
        Assert.Null(Assert.IsType<IrArray>(v.Type).MinItems);
    }

    [Fact]
    public void Json_arrayagg_null_on_null_keeps_null_items()
    {
        var doc = Procedure("SELECT json_arrayagg(u.qty NULL ON NULL) AS v FROM dbo.u AS u FOR JSON PATH;");

        var array = Assert.IsType<IrArray>(Field(doc, "v").Type);
        Assert.Equal(["nullable-column"], Rules(array.ItemsNullableBecause));
        Assert.Equal(1, array.MinItems);
    }

    [Fact]
    public void Json_arrayagg_of_objects_is_an_array_of_objects()
    {
        var doc = Procedure("SELECT json_arrayagg(json_object('id': u.id, 'qty': u.qty ABSENT ON NULL)) AS v FROM dbo.u AS u FOR JSON PATH;");

        var array = Assert.IsType<IrArray>(Field(doc, "v").Type);
        Assert.Equal(IrPresence.OmittedWhenNull, Fields(doc, array.Items).Single(f => f.Name == "qty").Presence);
    }

    [Fact]
    public void An_aggregate_subquery_is_live_json_and_null_over_no_rows()
    {
        var doc = Procedure("""
            SELECT t.id, (SELECT json_arrayagg(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id) AS lines
            FROM dbo.t AS t FOR JSON PATH;
            """);
        var lines = Field(doc, "lines");

        Assert.IsType<IrArray>(lines.Type);
        Assert.Equal(IrPresence.OmittedWhenNull, lines.Presence);
        Assert.Contains("empty-aggregate", Rules(lines.NullableBecause));

        // An aggregate without GROUP BY always returns its one row, so the subquery is never empty.
        Assert.DoesNotContain("empty-subquery", Rules(lines.NullableBecause));
    }

    [Fact]
    public void A_subquery_that_is_not_json_has_its_columns_type_and_is_null_when_it_has_no_row()
    {
        // A scalar subquery is its one column's value, or null when it returns no row.
        var doc = Procedure("SELECT t.id, (SELECT u.qty FROM dbo.u AS u WHERE u.id = t.id) AS q FROM dbo.t AS t FOR JSON PATH;");

        Assert.Empty(doc.Unknowns);
        Assert.Contains("empty-subquery", Rules(Field(doc, "q").NullableBecause));
    }
}

using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class JsonConstructorTests
{
    [Fact]
    public void Json_array_of_values_never_null_is_a_tuple()
    {
        var doc = Procedure("SELECT json_array(t.id, t.label) AS v FROM dbo.t AS t FOR JSON PATH;");

        var tuple = Assert.IsType<IrTuple>(Field(doc, "v").Type);
        Assert.Equal([IrScalarKind.Number, IrScalarKind.String], tuple.Items.Select(i => Assert.IsType<IrScalar>(i.Type).Kind));
        Assert.All(tuple.Items, i => Assert.False(i.Nullable));
    }

    [Fact]
    public void An_empty_json_array_is_an_empty_tuple() =>
        Assert.Empty(Assert.IsType<IrTuple>(Field(Procedure("SELECT json_array() AS v FROM dbo.t AS t FOR JSON PATH;"), "v").Type).Items);

    [Fact]
    public void Json_array_drops_nulls_by_default_so_a_nullable_element_makes_an_array_of_the_union()
    {
        var doc = Procedure("SELECT json_array(t.id, t.name, t.code) AS v FROM dbo.t AS t FOR JSON PATH;");

        var array = Assert.IsType<IrArray>(Field(doc, "v").Type);
        var union = Assert.IsType<IrUnion>(array.Items);
        Assert.Equal([IrScalarKind.Number, IrScalarKind.String], union.Variants.Select(i => Assert.IsType<IrScalar>(i).Kind));
        Assert.False(array.ItemsNullable);
        Assert.Equal(2, array.MinItems);
        Assert.Equal(3, array.MaxItems);
    }

    [Fact]
    public void Elements_of_one_type_make_an_array_of_that_type()
    {
        var doc = Procedure("SELECT json_array(t.name, t.name) AS v FROM dbo.t AS t FOR JSON PATH;");

        var array = Assert.IsType<IrArray>(Field(doc, "v").Type);
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(array.Items).Kind);
        Assert.Null(array.MinItems);
        Assert.Equal(2, array.MaxItems);
    }

    [Fact]
    public void Json_array_null_on_null_keeps_every_position_as_a_tuple_of_possibly_null_elements()
    {
        var doc = Procedure("SELECT json_array(t.id, t.name NULL ON NULL) AS v FROM dbo.t AS t FOR JSON PATH;");

        var tuple = Assert.IsType<IrTuple>(Field(doc, "v").Type);
        Assert.False(tuple.Items[0].Nullable);
        Assert.Equal([IrNullRule.NullableColumn], Rules(tuple.Items[1].NullableBecause));
    }

    [Fact]
    public void An_unknown_element_is_reported_at_its_position()
    {
        var doc = Procedure("SELECT json_array(t.id, checksum(t.label) NULL ON NULL) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReason.UnsupportedExpression, Unknown(doc, "results[0][].v[1]").Reason);
    }
}

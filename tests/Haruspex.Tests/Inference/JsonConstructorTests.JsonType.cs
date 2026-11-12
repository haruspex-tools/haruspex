using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class JsonConstructorTests
{
    [Fact]
    public void A_json_column_is_live_json_of_unknown_shape_with_the_column_s_nullability()
    {
        var doc = Procedure("SELECT j.id, j.doc, j.body, json_object('d': j.doc) AS o FROM dbo.j AS j FOR JSON PATH;");

        var docField = Field(doc, "doc");
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Assert.IsType<IrUnknownType>(docField.Type).Reason);
        Assert.Equal([IrNullRule.NullableColumn], Rules(docField.NullableBecause));
        Assert.Equal(IrPresence.OmittedWhenNull, docField.Presence);
        var body = Field(doc, "body");
        Assert.IsType<IrUnknownType>(body.Type);
        Assert.Empty(body.NullableBecause);
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Unknown(doc, "results[0][].o.d").Reason);
    }

    [Fact]
    public void A_json_column_in_a_rowset_is_json_text()
    {
        var doc = Procedure("SELECT j.doc, j.body FROM dbo.j AS j;");

        Assert.IsType<IrJsonText>(Field(doc, "doc").Type);
        Assert.True(Field(doc, "doc").Nullable);
        Assert.IsType<IrJsonText>(Field(doc, "body").Type);
        Assert.False(Field(doc, "body").Nullable);
    }

    [Fact]
    public void A_json_parameter_is_json_text()
    {
        var doc = Procedure("SELECT t.id FROM dbo.t AS t;", "@filter json");

        Assert.IsType<IrJsonText>(Assert.Single(doc.Objects[0].Parameters).Type);
    }
}

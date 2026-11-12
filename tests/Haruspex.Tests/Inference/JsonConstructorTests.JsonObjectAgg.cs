using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class JsonConstructorTests
{
    [Fact]
    public void Json_objectagg_is_a_record_whose_null_values_are_kept_by_default()
    {
        var doc = Procedure("SELECT json_objectagg(t.label: t.name) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Equal([IrNullRule.EmptyAggregate], Rules(v.NullableBecause));
        var record = Assert.IsType<IrRecord>(v.Type);
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(record.Values).Kind);
        Assert.Equal([IrNullRule.NullableColumn], Rules(record.ValuesNullableBecause));
    }

    [Fact]
    public void Json_objectagg_absent_on_null_leaves_null_values_out()
    {
        var doc = Procedure("SELECT json_objectagg(t.label: t.name ABSENT ON NULL) AS v FROM dbo.t AS t GROUP BY t.code FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Empty(v.NullableBecause);
        Assert.False(Assert.IsType<IrRecord>(v.Type).ValuesNullable);
    }

    [Fact]
    public void Its_key_may_be_any_expression_since_every_key_is_a_string()
    {
        var doc = Procedure("SELECT json_objectagg(upper(t.label): t.id) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.IsType<IrRecord>(Field(doc, "v").Type);
        Assert.Empty(doc.Unknowns);
    }
}

using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class SetJoinTests
{
    [Fact]
    public void Openjson_without_with_is_key_value_and_type()
    {
        // Openjson_without_with_gives_key_value_and_type.
        var doc = Procedure("SELECT j.[key], j.value, j.type FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) AS j;");
        AllTyped(doc);
        Assert.Equal("nvarchar(4000)", Typed(doc, "key"));
        Assert.Equal("nvarchar(max) json-value", Typed(doc, "value"));
        Assert.Equal("tinyint", Typed(doc, "type"));
        Assert.Equal("exact:0|1|2|3|4|5", Values(doc, "type"));
    }

    [Fact]
    public void Openjson_with_columns_are_of_their_declared_types_and_may_be_null_even_under_strict()
    {
        // Openjson_with_columns_are_of_their_declared_types_and_null_for_a_missing_path_or_a_json_null.
        var doc = Procedure("""
            DECLARE @doc nvarchar(max) = N'[]';
            SELECT j.a, j.s, j.d FROM OPENJSON(@doc) WITH (a int '$.a', s varchar(3) 'strict $.s', d date) AS j;
            """);
        AllTyped(doc);
        Assert.Equal("int json-value", Typed(doc, "a"));
        Assert.Equal("varchar(3) json-value", Typed(doc, "s"));
        Assert.Equal("date json-value", Typed(doc, "d"));
    }

    [Fact]
    public void Openjson_as_json_nests_as_json_of_unknown_shape()
    {
        // Openjson_as_json_is_nvarchar_max_holding_an_object_or_array_that_nests_as_json.
        var doc = Procedure("SELECT j.items FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) WITH (items nvarchar(max) '$.items' AS JSON) AS j;");
        Assert.IsType<IrJsonText>(Field(doc, "items").Type);
        Assert.Equal([IrNullRule.JsonValue], Rules(doc, "items"));

        doc = Procedure("SELECT j.items FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) WITH (items nvarchar(max) '$.items' AS JSON) AS j FOR JSON PATH;");
        Assert.IsType<IrUnknownType>(Field(doc, "items").Type);
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Assert.Single(doc.Unknowns).Reason);
    }

    [Fact]
    public void Openjson_under_outer_apply_is_null_extended()
    {
        var doc = Procedure("SELECT t.id, j.a FROM dbo.t AS t OUTER APPLY OPENJSON(t.doc) WITH (a int) AS j;");
        Assert.Equal("int json-value outer-apply", Typed(doc, "a"));
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }
}

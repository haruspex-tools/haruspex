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
        Assert.Equal("nvarchar(max) json-null-or-absent", Typed(doc, "value"));
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
        Assert.Equal("int json-null-or-absent", Typed(doc, "a"));
        Assert.Equal("varchar(3) json-null-or-absent", Typed(doc, "s"));
        Assert.Equal("date json-null-or-absent", Typed(doc, "d"));
    }

    [Fact]
    public void Openjson_as_json_nests_as_json_of_unknown_shape()
    {
        // Openjson_as_json_is_nvarchar_max_holding_an_object_or_array_that_nests_as_json.
        var doc = Procedure("SELECT j.items FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) WITH (items nvarchar(max) '$.items' AS JSON) AS j;");
        Assert.IsType<IrJsonText>(Field(doc, "items").Type);
        Assert.Equal([IrNullRule.JsonNullOrAbsent], Rules(doc, "items"));

        doc = Procedure("SELECT j.items FROM dbo.t AS t CROSS APPLY OPENJSON(t.doc) WITH (items nvarchar(max) '$.items' AS JSON) AS j FOR JSON PATH;");
        Assert.IsType<IrUnknownType>(Field(doc, "items").Type);
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Assert.Single(doc.Unknowns).Reason);
    }

    [Fact]
    public void Openjson_under_outer_apply_is_null_extended()
    {
        var doc = Procedure("SELECT t.id, j.a FROM dbo.t AS t OUTER APPLY OPENJSON(t.doc) WITH (a int) AS j;");
        Assert.Equal("int json-null-or-absent unmatched-lateral-row", Typed(doc, "a"));
        Assert.Equal(IrCardinality.Many, Rows(doc));
    }

    [Fact]
    public void An_openjson_without_an_alias_on_an_outer_joins_optional_side_is_proved_by_the_on()
    {
        // As an aliased one is: matched, value is not a JSON null, so only the null-extended row is null (observed:
        // An_openjson_without_an_alias_on_an_outer_joins_optional_side_is_filtered_by_the_on_as_one_with_an_alias_is).
        var bare = Procedure("SELECT t.id, value AS v FROM dbo.t AS t LEFT JOIN OPENJSON(N'[1, null]') ON value IS NOT NULL AND t.id = 1;");
        var named = Procedure("SELECT t.id, j.value AS v FROM dbo.t AS t LEFT JOIN OPENJSON(N'[1, null]') AS j ON j.value IS NOT NULL AND t.id = 1;");

        Assert.Equal("nvarchar(max) unmatched-row", Typed(bare, "v"));
        Assert.Equal(Typed(named, "v"), Typed(bare, "v"));
    }
}

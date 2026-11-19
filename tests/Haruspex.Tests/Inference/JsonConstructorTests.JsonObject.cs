using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class JsonConstructorTests
{
    [Fact]
    public void Json_object_with_literal_keys_is_an_object_whose_null_keys_are_kept_by_default()
    {
        var doc = Procedure("SELECT json_object('id': t.id, 'name': t.name) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Empty(v.NullableBecause);
        Assert.Equal(IrPresence.Always, v.Presence);
        var id = Sub(doc, v, "id");
        Assert.Equal(IrPresence.Always, id.Presence);
        Assert.False(id.Nullable);
        var name = Sub(doc, v, "name");
        Assert.Equal(IrPresence.Always, name.Presence);
        Assert.Equal([IrNullRule.NullableColumn], Rules(name.NullableBecause));
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("NULL ON NULL", IrPresence.Always)]
    [InlineData("ABSENT ON NULL", IrPresence.OmittedWhenNull)]
    public void Its_null_option_decides_whether_a_null_key_is_kept(string option, IrPresence presence)
    {
        var doc = Procedure($"SELECT json_object('id': t.id, 'name': t.name {option}) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Equal(presence, Sub(doc, v, "name").Presence);
        Assert.Equal(IrPresence.Always, Sub(doc, v, "id").Presence);
    }

    [Fact]
    public void Its_values_keep_their_facets_when_they_pass_through_unchanged()
    {
        var doc = Procedure("SELECT json_object('name': t.name, 'id': t.id) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        var name = Assert.IsType<IrScalar>(Sub(doc, v, "name").Type);
        Assert.Equal(10, name.Facets!.Length!.Value);
        var id = Assert.IsType<IrScalar>(Sub(doc, v, "id").Type);
        Assert.Equal(IrFormat.Int32, id.Format);
    }

    [Theory]
    [InlineData("@k")]
    [InlineData("t.name")]
    [InlineData("N'a' + N'b'")]
    public void A_key_that_is_not_a_literal_is_unknown(string key)
    {
        var doc = Procedure($"SELECT json_object({key}: t.id) AS v FROM dbo.t AS t FOR JSON PATH;", "@k nvarchar(10)");

        Assert.Equal(IrUnknownReason.UnsupportedExpression, Unknown(doc, "results[0][].v").Reason);
        Assert.Contains("key", Unknown(doc, "results[0][].v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_key_is_unknown()
    {
        // SQL Server writes both ({"a":1,"a":2}), which JSON.parse reads as the last: no fixed shape says that.
        var doc = Procedure("SELECT json_object('a': t.id, N'a': t.code) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReason.DuplicateColumn, Unknown(doc, "results[0][].v").Reason);
    }

    [Fact]
    public void Keys_that_differ_only_in_case_are_two_keys()
    {
        var doc = Procedure("SELECT json_object('a': t.id, 'A': t.code) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(["a", "A"], Fields(doc, Field(doc, "v").Type).Select(f => f.Name));
    }

    [Fact]
    public void An_empty_json_object_has_no_fields()
    {
        var doc = Procedure("SELECT json_object() AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Empty(Fields(doc, Field(doc, "v").Type));
    }

    [Fact]
    public void Nested_constructors_json_query_and_for_json_are_live_and_other_strings_are_text()
    {
        var doc = Procedure("""
            SELECT json_object(
                       'obj': json_object('id': t.id),
                       'arr': json_array(t.id),
                       'q': json_query(json_object('id': t.id)),
                       'sub': (SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH),
                       'text': t.label) AS v
            FROM dbo.t AS t FOR JSON PATH;
            """);
        var v = Field(doc, "v");

        Assert.IsType<IrRef>(Sub(doc, v, "obj").Type);
        Assert.IsType<IrTuple>(Sub(doc, v, "arr").Type);
        Assert.IsType<IrRef>(Sub(doc, v, "q").Type);
        var sub = Sub(doc, v, "sub");
        Assert.IsType<IrArray>(sub.Type);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(sub.NullableBecause));
        Assert.Equal(IrPresence.Always, sub.Presence);
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(Sub(doc, v, "text").Type).Kind);
    }

    [Fact]
    public void A_for_json_subquery_without_its_array_wrapper_is_text_in_a_constructor_and_in_for_json()
    {
        // Observed: (SELECT … FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) nests as an escaped string, {"a":"{\"x\":1}"}.
        var doc = Procedure("""
            SELECT json_object('one': (SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS o,
                   json_array((SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS a,
                   (SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS f
            FROM dbo.t AS t FOR JSON PATH;
            """);

        var one = Sub(doc, Field(doc, "o"), "one");
        Assert.IsType<IrJsonText>(one.Type);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(one.NullableBecause));
        Assert.IsType<IrJsonText>(Assert.IsType<IrArray>(Field(doc, "a").Type).Items);
        var f = Field(doc, "f");
        Assert.IsType<IrJsonText>(f.Type);
        Assert.Equal(IrPresence.OmittedWhenNull, f.Presence);
    }

    [Fact]
    public void Json_query_over_it_makes_it_a_live_object()
    {
        var doc = Procedure("""
            SELECT json_object('one': json_query((SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER))) AS o,
                   json_query((SELECT u.id FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)) AS f
            FROM dbo.t AS t FOR JSON PATH;
            """);

        var one = Sub(doc, Field(doc, "o"), "one");
        Assert.Equal("id", Assert.Single(Fields(doc, one.Type)).Name);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(one.NullableBecause));
        Assert.Equal("id", Assert.Single(Fields(doc, Field(doc, "f").Type)).Name);
    }

    [Fact]
    public void Constructors_nest_several_levels_deep()
    {
        var doc = Procedure("SELECT json_array(json_object('a': json_array(json_object('b': json_array(t.id, json_object('c': t.name)))))) AS v FROM dbo.t AS t FOR JSON PATH;");

        var outer = Assert.IsType<IrTuple>(Field(doc, "v").Type);
        var a = Fields(doc, Assert.Single(outer.Items).Type).Single();
        var b = Fields(doc, Assert.Single(Assert.IsType<IrTuple>(a.Type).Items).Type).Single();
        var inner = Assert.IsType<IrTuple>(b.Type);
        Assert.Equal(2, inner.Items.Count);
        var c = Fields(doc, inner.Items[1].Type).Single();
        Assert.True(c.Nullable);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Returning_json_changes_nothing_about_the_shape()
    {
        var plain = Procedure("SELECT json_object('id': t.id) AS v FROM dbo.t AS t FOR JSON PATH;");
        var returning = Procedure("SELECT json_object('id': t.id RETURNING json) AS v FROM dbo.t AS t FOR JSON PATH;");

        // The two scripts differ, so their files' hashes do; everything the document claims is the same.
        Assert.Equal(IrJsonWriter.Write(plain), IrJsonWriter.Write(returning with { Source = plain.Source }));
        Assert.Empty(returning.Unknowns);
    }

    [Fact]
    public void A_constructor_in_a_rowset_is_json_text()
    {
        var doc = Procedure("SELECT json_object('id': t.id) AS a, json_array(t.id) AS b FROM dbo.t AS t;");

        Assert.IsType<IrJsonText>(Field(doc, "a").Type);
        Assert.IsType<IrJsonText>(Field(doc, "b").Type);
        Assert.False(Field(doc, "a").Nullable);
    }

    [Fact]
    public void A_constructor_under_a_dotted_alias_nests_inside_the_path()
    {
        var doc = Procedure("SELECT t.id, json_object('n': t.code) AS [p.x] FROM dbo.t AS t FOR JSON PATH;");

        var x = Sub(doc, Field(doc, "p"), "x");
        Assert.Equal("n", Assert.Single(Fields(doc, x.Type)).Name);
    }
}

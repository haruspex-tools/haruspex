using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// The JSON constructors (<c>json_object</c>, <c>json_array</c>, <c>json_arrayagg</c>, <c>json_objectagg</c>) and the
/// native <c>json</c> type. Every rule here was observed on SQL Server 2025 first (JsonConstructorObservationTests in
/// the differential tests), and each refusal is a construct whose shape the SQL alone does not settle.
/// </summary>
public class JsonConstructorTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL, label nvarchar(20) NOT NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty smallint NULL);
        GO
        CREATE TABLE dbo.j (id int NOT NULL PRIMARY KEY, doc json NULL, body json NOT NULL);
        GO
        """;

    private static IrDocument Procedure(string body, string parameters = "")
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        dir.File("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}");
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse("*.*")]).Procedures);
    }

    /// <summary>The field <paramref name="name"/> of the first result's row.</summary>
    private static IrField Field(IrDocument doc, string name) => Fields(doc, Row(doc)).Single(f => f.Name == name);

    private static IrRef Row(IrDocument doc) => doc.Objects[0].Results![0].Type switch
    {
        IrArray { Items: IrRef r } => r,
        IrRef r => r,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => doc.Types[Assert.IsType<IrRef>(type).Name].Fields;

    private static IrField Sub(IrDocument doc, IrField field, string name) => Fields(doc, field.Type).Single(f => f.Name == name);

    private static IrUnknown Unknown(IrDocument doc, string path) => Assert.Single(doc.Unknowns, u => u.Path == path);

    private static IEnumerable<string> Rules(IEnumerable<IrNullReason> reasons) => reasons.Select(r => r.Rule);

    // ---- json_object ---------------------------------------------------------------------------------

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
        Assert.Equal(["nullable-column"], Rules(name.NullableBecause));
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
        Assert.Equal(IrFormats.Int32, id.Format);
    }

    [Theory]
    [InlineData("@k")]
    [InlineData("t.name")]
    [InlineData("N'a' + N'b'")]
    public void A_key_that_is_not_a_literal_is_unknown(string key)
    {
        var doc = Procedure($"SELECT json_object({key}: t.id) AS v FROM dbo.t AS t FOR JSON PATH;", "@k nvarchar(10)");

        Assert.Equal(IrUnknownReasons.UnsupportedExpression, Unknown(doc, "results[0][].v").Reason);
        Assert.Contains("key", Unknown(doc, "results[0][].v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_key_is_unknown()
    {
        // SQL Server writes both ({"a":1,"a":2}), which JSON.parse reads as the last: no fixed shape says that.
        var doc = Procedure("SELECT json_object('a': t.id, N'a': t.code) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReasons.DuplicateColumn, Unknown(doc, "results[0][].v").Reason);
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
        Assert.Equal(["empty-subquery"], Rules(sub.NullableBecause));
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
        Assert.Equal(["empty-subquery"], Rules(one.NullableBecause));
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
        Assert.Equal(["empty-subquery"], Rules(one.NullableBecause));
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

        Assert.Equal(IrJsonWriter.Write(plain), IrJsonWriter.Write(returning));
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

    // ---- json_array ----------------------------------------------------------------------------------

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
        Assert.Equal(["nullable-column"], Rules(tuple.Items[1].NullableBecause));
    }

    [Fact]
    public void An_unknown_element_is_reported_at_its_position()
    {
        var doc = Procedure("SELECT json_array(t.id, reverse(t.label) NULL ON NULL) AS v FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(IrUnknownReasons.UnsupportedExpression, Unknown(doc, "results[0][].v[1]").Reason);
    }

    // ---- json_arrayagg -------------------------------------------------------------------------------

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

        // An aggregate without GROUP BY always returns its one row, so the subquery is never empty (slice 6c).
        Assert.DoesNotContain("empty-subquery", Rules(lines.NullableBecause));
    }

    [Fact]
    public void A_subquery_that_is_not_json_has_its_columns_type_and_is_null_when_it_has_no_row()
    {
        // Slice 6c: a scalar subquery is its one column's value, or null when it returns no row.
        var doc = Procedure("SELECT t.id, (SELECT u.qty FROM dbo.u AS u WHERE u.id = t.id) AS q FROM dbo.t AS t FOR JSON PATH;");

        Assert.Empty(doc.Unknowns);
        Assert.Contains("empty-subquery", Rules(Field(doc, "q").NullableBecause));
    }

    // ---- json_objectagg ------------------------------------------------------------------------------

    [Fact]
    public void Json_objectagg_is_a_record_whose_null_values_are_kept_by_default()
    {
        var doc = Procedure("SELECT json_objectagg(t.label: t.name) AS v FROM dbo.t AS t FOR JSON PATH;");
        var v = Field(doc, "v");

        Assert.Equal(["empty-aggregate"], Rules(v.NullableBecause));
        var record = Assert.IsType<IrRecord>(v.Type);
        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(record.Values).Kind);
        Assert.Equal(["nullable-column"], Rules(record.ValuesNullableBecause));
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

    // ---- the json type -------------------------------------------------------------------------------

    [Fact]
    public void A_json_column_is_live_json_of_unknown_shape_with_the_column_s_nullability()
    {
        var doc = Procedure("SELECT j.id, j.doc, j.body, json_object('d': j.doc) AS o FROM dbo.j AS j FOR JSON PATH;");

        var docField = Field(doc, "doc");
        Assert.Equal(IrUnknownReasons.JsonShapeUnknown, Assert.IsType<IrUnknownType>(docField.Type).Reason);
        Assert.Equal(["nullable-column"], Rules(docField.NullableBecause));
        Assert.Equal(IrPresence.OmittedWhenNull, docField.Presence);
        var body = Field(doc, "body");
        Assert.IsType<IrUnknownType>(body.Type);
        Assert.Empty(body.NullableBecause);
        Assert.Equal(IrUnknownReasons.JsonShapeUnknown, Unknown(doc, "results[0][].o.d").Reason);
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

    // ---- Zod -----------------------------------------------------------------------------------------

    [Fact]
    public void The_zod_states_tuples_records_and_array_lengths()
    {
        var ts = ZodEmitter.Emit(Procedure("""
            SELECT json_array(t.id, t.label) AS pair,
                   json_array(t.id, t.name) AS mixed,
                   json_array(t.id, t.name NULL ON NULL) AS slots,
                   (SELECT json_objectagg(u.id: u.qty) FROM dbo.u AS u WHERE u.t_id = t.id) AS qtys
            FROM dbo.t AS t FOR JSON PATH;
            """));

        Assert.Contains("  pair: z.tuple([z.int32(), z.string().max(20)]),\n", ts, StringComparison.Ordinal);
        Assert.Contains("  mixed: z.array(z.union([z.int32(), z.string().max(10)])).min(1).max(2),\n", ts, StringComparison.Ordinal);
        Assert.Contains("  slots: z.tuple([z.int32(), z.string().max(10).nullable()]),\n", ts, StringComparison.Ordinal);
        Assert.Contains("  qtys: z.record(z.string(), z.int().min(-32768).max(32767).nullable()).optional(),\n", ts, StringComparison.Ordinal);
    }
}

using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// JSON objects assembled as text: <c>'{"id":' + … + ',"status":"' + string_escape(…, 'json') + '"}'</c>, its <c>concat</c> and
/// <c>concat_ws</c> forms, and objects and arrays inside each other across pieces. Each rule rests on an observation in the
/// differential tests' <c>JsonAssembledObservationTests</c>, named where it matters.
/// </summary>
public partial class JsonAssembledTests
{
    private const string Id = "convert(nvarchar(max), o.order_id)";

    private static IrField Key(IrDocument doc, IrType type, string name) => Fields(doc, type).Single(f => f.Name == name);

    // ---- keys and values -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_object_of_literal_keys_and_known_values_is_an_object_of_those_keys()
    {
        var doc = Procedure(Read($"N'{{\"id\":' + {Id} + N',\"ref\":\"' + string_escape(o.ref, 'json') + N'\",\"gift\":true}}'"));
        var v = Field(doc, "v");

        Assert.Equal(["id", "ref", "gift"], Fields(doc, v.Type).Select(f => f.Name));
        Assert.All(Fields(doc, v.Type), f => Assert.Equal(IrPresence.Always, f.Presence));
        Assert.All(Fields(doc, v.Type), f => Assert.Empty(f.NullableBecause));
        Assert.Equal("int", Assert.IsType<IrScalar>(Key(doc, v.Type, "id").Type).Sql);
        Assert.Equal("nvarchar(20)", Assert.IsType<IrScalar>(Key(doc, v.Type, "ref").Type).Sql);
        Assert.Equal(IrScalarKind.Boolean, Assert.IsType<IrScalar>(Key(doc, v.Type, "gift").Type).Kind);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Concat_of_an_object_is_the_same_object_and_a_value_concat_writes_as_nothing_describes_no_call()
    {
        // Observed: A_null_value_leaves_an_object_null_under_plus_and_not_json_under_concat. concat writes {"id":} when the value
        // is NULL, which json_query refuses (13609), so that way describes no call that succeeds.
        var doc = Procedure(Read("concat(N'{\"id\":', nullif(o.order_id, 0), N',\"ref\":\"', string_escape(o.ref, 'json'), N'\"}')"));
        var v = Field(doc, "v");

        Assert.Equal(["id", "ref"], Fields(doc, v.Type).Select(f => f.Name));
        Assert.Empty(Key(doc, v.Type, "id").NullableBecause);
        Assert.Empty(v.NullableBecause);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void Plus_over_a_value_that_may_be_null_makes_the_whole_object_null_not_its_key()
    {
        var doc = Procedure(Read("N'{\"id\":' + convert(nvarchar(max), nullif(o.order_id, 0)) + N'}'"));
        var v = Field(doc, "v");

        Assert.NotEmpty(v.NullableBecause);
        Assert.Empty(Key(doc, v.Type, "id").NullableBecause);
    }

    [Fact]
    public void A_value_written_as_null_by_coalesce_is_a_key_present_and_null()
    {
        var doc = Procedure(Read("N'{\"id\":' + coalesce(convert(nvarchar(max), nullif(o.order_id, 0)), N'null') + N'}'"));
        var id = Key(doc, Field(doc, "v").Type, "id");

        Assert.Equal(IrPresence.Always, id.Presence);
        Assert.Equal([IrNullRule.NullLiteral], id.NullableBecause.Select(r => r.Rule));
        // A JSON null written in a literal has no type of its own; it is read as a string, as JsonLiterals reads one.
        Assert.Contains(Assert.IsType<IrUnion>(id.Type).Variants, t => t is IrScalar { Sql: "int" });
    }

    [Fact]
    public void A_key_written_with_escapes_is_the_name_they_make()
    {
        var doc = Procedure(Read($"N'{{\"a\\\"b\":' + {Id} + N',\"c\\\\d\":1,\"\\u0065\":2}}'"));

        Assert.Equal(["a\"b", "c\\d", "e"], Fields(doc, Field(doc, "v").Type).Select(f => f.Name));
    }

    [Fact]
    public void An_unescaped_value_in_an_objects_quotes_is_unknown()
    {
        var doc = Procedure(Read($"N'{{\"id\":' + {Id} + N',\"ref\":\"' + o.ref + N'\"}}'"));

        Assert.Contains("without string_escape", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    // ---- duplicate keys --------------------------------------------------------------------------------------------------

    [Theory]
    // Observed: A_key_written_twice_is_kept_twice_and_readers_differ_on_which_wins. JSON.parse keeps the last, JSON_VALUE the
    // first, so neither write's type is the key's.
    [InlineData("N'{\"id\":1,\"id\":' + convert(nvarchar(max), o.order_id) + N'}'")]
    [InlineData("N'{' + concat_ws(N',', N'\"id\":1', CASE WHEN o.channel = 'r' THEN N'\"id\":\"r\"' END) + N'}'")]
    [InlineData("N'{\"id\":1,' + concat_ws(N',', N'\"x\":2', N'\"id\":3') + N'}'")]
    public void A_key_that_may_be_written_twice_is_unknown(string text)
    {
        var doc = Procedure(Read(text));

        Assert.Contains("written twice", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_written_on_different_ways_through_the_text_is_not_written_twice()
    {
        var doc = Procedure(Read("CASE WHEN o.channel = 'r' THEN N'{\"id\":' + convert(nvarchar(max), o.order_id) + N'}' ELSE N'{\"id\":\"' + string_escape(o.ref, 'json') + N'\"}' END"));

        Assert.Empty(doc.Unknowns);
        Assert.IsType<IrUnion>(Key(doc, Field(doc, "v").Type, "id").Type);
    }

    // ---- keys built from data ------------------------------------------------------------------------------------------

    [Fact]
    public void Keys_built_from_escaped_data_are_an_object_whose_keys_are_data()
    {
        // Observed: Keys_built_from_data_may_repeat_and_are_kept_as_written. A key may repeat, but every value is an int, so
        // whichever a reader keeps is one.
        var doc = Procedure(Read("N'{' + coalesce((SELECT string_agg(N'\"' + string_escape(t.name, 'json') + N'\":' + convert(nvarchar(max), t.order_id), N',') FROM dbo.tag AS t WHERE t.order_id = o.order_id), N'') + N'}'"));
        var record = Assert.IsType<IrRecord>(Field(doc, "v").Type);

        Assert.Equal("int", Assert.IsType<IrScalar>(record.Values).Sql);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_key_built_from_data_beside_literal_keys_is_unknown()
    {
        var doc = Procedure(Read($"N'{{\"id\":' + {Id} + N',\"' + string_escape(o.ref, 'json') + N'\":1}}'"));

        Assert.Contains("key", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_built_from_unescaped_data_is_unknown()
    {
        var doc = Procedure(Read("N'{\"' + o.ref + N'\":1}'"));

        Assert.Contains("without string_escape", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    // ---- keys that may be absent -----------------------------------------------------------------------------------------

    [Fact]
    public void Concat_ws_of_members_leaves_out_a_member_that_may_be_null()
    {
        // Observed: Concat_ws_of_members_leaves_out_those_that_are_null_and_a_stray_comma_is_not_json.
        var doc = Procedure(Read($"N'{{' + concat_ws(N',', N'\"id\":' + {Id}, CASE WHEN o.channel = 'r' THEN N'\"retail\":true' END) + N'}}'"));
        var v = Field(doc, "v");

        Assert.Equal(IrPresence.Always, Key(doc, v.Type, "id").Presence);
        var retail = Key(doc, v.Type, "retail");
        Assert.Equal(IrPresence.OmittedWhenNull, retail.Presence);
        Assert.Equal([IrNullRule.NoBranchMatched], retail.NullableBecause.Select(r => r.Rule));
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    // A member that may be empty text is not left out by a NULL, which the IR could name; a member that may be null as JSON and
    // may be left out can't be said by one presence.
    [InlineData("N'{' + concat_ws(N',', N'\"id\":1', CASE WHEN o.channel = 'r' THEN N'\"retail\":true' ELSE N'' END) + N'}'")]
    [InlineData("N'{' + concat_ws(N',', N'\"id\":1', CASE WHEN o.channel = 'r' THEN N'\"retail\":null' END) + N'}'")]
    [InlineData("N'{' + concat_ws(N',', N'\"id\":1', CASE WHEN o.channel = 'r' THEN N'\"retail\":1' WHEN o.channel = 't' THEN N'\"trade\":1' END) + N'}'")]
    public void A_member_whose_absence_the_ir_cannot_say_is_unknown(string text)
    {
        var doc = Procedure(Read(text));

        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Unknown(doc, "v").Reason);
    }

    [Theory]
    // Observed: Concat_ws_of_members_leaves_out_those_that_are_null_and_a_stray_comma_is_not_json. Each may leave a comma beside a
    // brace, which is not JSON on that way, and JSON on the others.
    [InlineData("N'{\"id\":1,' + concat_ws(N',', CASE WHEN o.channel = 'r' THEN N'\"retail\":true' END, CASE WHEN o.channel = 't' THEN N'\"trade\":true' END) + N'}'")]
    [InlineData("N'{' + concat_ws(N',', CASE WHEN o.channel = 'r' THEN N'\"retail\":true' END, CASE WHEN o.channel = 't' THEN N'\"trade\":true' END) + N',\"id\":1}'")]
    public void Concat_ws_that_may_write_nothing_beside_a_comma_is_unknown(string text)
    {
        var doc = Procedure(Read(text));

        Assert.Contains("may write nothing", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    // ---- nesting across pieces -----------------------------------------------------------------------------------------

    [Fact]
    public void An_array_opened_in_an_objects_literal_and_closed_in_another_is_the_objects_array()
    {
        // concat counts a NULL aggregate as empty, so the array is [] then; the brackets balance on every way.
        var doc = Procedure(Read($"concat(N'{{\"id\":', o.order_id, N',\"lines\":[', {LineIds}, N']}}')"));
        var v = Field(doc, "v");
        var lines = Assert.IsType<IrArray>(Key(doc, v.Type, "lines").Type);

        Assert.Equal("int", Assert.IsType<IrScalar>(lines.Items).Sql);
        Assert.Null(lines.MinItems);
        Assert.Empty(v.NullableBecause);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void An_object_inside_an_object_across_pieces_is_read()
    {
        var doc = Procedure(Read($"N'{{\"order\":{{\"id\":' + {Id} + N'}},\"n\":1}}'"));
        var order = Key(doc, Field(doc, "v").Type, "order");

        Assert.Equal(["id"], Fields(doc, order.Type).Select(f => f.Name));
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void String_agg_of_objects_built_as_text_is_an_array_of_objects()
    {
        var doc = Procedure(Read("N'[' + coalesce((SELECT string_agg(N'{\"id\":' + convert(nvarchar(max), l.line_id) + N',\"note\":\"' + string_escape(l.note, 'json') + N'\"}', N',') FROM dbo.line AS l WHERE l.order_id = o.order_id), N'') + N']'"));
        var array = Array(doc);

        Assert.Equal(["id", "note"], Fields(doc, array.Items).Select(f => f.Name));
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    // Observed: Brackets_that_do_not_balance_are_not_json_and_text_after_the_brace_is_ignored. Unbalanced on one way (a NULL
    // middle piece under concat leaves {"o":}} ) describes no call; text after the closing brace json_query ignores, so the read
    // succeeds with less than the text says.
    [InlineData("N'{\"id\":' + convert(nvarchar(max), o.order_id) + N'}x'", "text follows")]
    [InlineData("N'{\"id\":' + convert(nvarchar(max), o.order_id) + N'},{\"id\":2}'", "text follows")]
    public void Text_after_the_closing_brace_is_unknown(string text, string why)
    {
        var doc = Procedure(Read(text));

        Assert.Contains(why, Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Brackets_that_balance_on_no_way_are_never_json_and_describe_no_call()
    {
        // {"lines":[1} is refused (13609) whatever the value, so no shape is claimed from the text.
        var doc = Procedure(Read($"N'{{\"lines\":[' + {Id} + N'}}'"));

        Assert.IsType<IrUnknownType>(Field(doc, "v").Type);
        Assert.DoesNotContain("JSON assembled as text", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_written_straight_after_a_number_in_an_object_is_unknown()
    {
        var doc = Procedure(Read("concat(N'{\"id\":', o.order_id, nullif(o.order_id, 0), N'}')"));

        Assert.Contains("continue", Unknown(doc, "v").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_held_in_a_variable_and_read_later_is_typed()
    {
        var doc = Procedure("""
            DECLARE @v nvarchar(max) = N'{"id":' + convert(nvarchar(max), @id) + N'}';
            SELECT json_query(@v) AS v FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """);

        Assert.Equal(["id"], Fields(doc, Field(doc, "v").Type).Select(f => f.Name));
    }
}

using Haruspex.Emit.Zod.Lowering;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>Each IR type node lowered to its Zod expression, compared as a model rather than as text.</summary>
public class TypeLoweringTests
{
    private static readonly IrScalar Text10 = new(IrScalarKind.String, null, "nvarchar(10)", new IrFacets(Length: new IrLength(10, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Type, "nvarchar(10)"))));
    private static readonly IrNullReason Because = new(IrNullRule.NullableColumn, "s.t.c");
    private static readonly ZodExpression String10 = Z("string").Then("max", new Number("10"));

    private static readonly TypeLowering Lowering = new(new Dictionary<string, IrObjectType>
    {
        ["Small"] = new([new IrField("a", Text10, IrPresence.Always, [])]),
        ["Large"] = new([new IrField("a", Text10, IrPresence.Always, []), new IrField("b", Text10, IrPresence.OmittedWhenNull, [Because])]),
    }, ZodModule.SchemaOf);

    [Fact]
    public void A_reference_is_the_named_schema() => Assert.Equal(new Name("OrderSchema"), Lowering.Type(new IrRef("Order"), IrEncoding.Json));

    [Fact]
    public void An_import_is_the_imported_name_as_it_is() => Assert.Equal(new Name("SettingsSchema"), Lowering.Type(new IrImport("SettingsSchema", "./s"), IrEncoding.Json));

    [Fact]
    public void An_array_carries_its_items_nullability_and_its_length()
    {
        var array = new IrArray(Text10) { ItemsNullableBecause = [Because], MinItems = 1, MaxItems = 3 };

        Assert.Equal(Z("array", String10.Then("nullable")).Then("min", new Number("1")).Then("max", new Number("3")), Lowering.Type(array, IrEncoding.Json));
    }

    [Fact]
    public void A_tuple_lists_each_element_with_its_own_nullability()
    {
        var tuple = new IrTuple([new IrElement(Text10, []), new IrElement(Text10, [Because])]);

        Assert.Equal(Z("tuple", new ArrayLiteral([String10, String10.Then("nullable")])), Lowering.Type(tuple, IrEncoding.Json));
    }

    [Fact]
    public void A_record_has_string_keys_and_one_value_type() =>
        Assert.Equal(Z("record", Z("string"), String10.Then("nullable")), Lowering.Type(new IrRecord(Text10, [Because]), IrEncoding.Json));

    [Fact]
    public void Json_text_of_known_content_is_read_as_json_inside_even_in_a_rowset()
    {
        var text = new IrJsonText(new IrScalar(IrScalarKind.Date, null, "date"));

        Assert.Equal(new Noted(new JsonText(new Call(new Name("z.iso"), "date", [])), new ZodNote("A string holding JSON, checked against the schema inside.")), Lowering.Type(text, IrEncoding.Rowset));
    }

    [Fact]
    public void Json_text_of_unknown_content_is_a_string_with_a_note() =>
        Assert.Equal(new Noted(Z("string"), new ZodNote("A string holding JSON.")), Lowering.Type(new IrJsonText(), IrEncoding.Json));

    [Fact]
    public void An_unknown_is_z_unknown_with_its_reason_as_a_note() =>
        Assert.Equal(new Noted(Z("unknown"), new ZodNote("Not known (unsupported-expression); the IR's unknowns say why.")), Lowering.Type(new IrUnknownType(IrUnknownReason.UnsupportedExpression), IrEncoding.Json));

    [Theory]
    [InlineData(IrEncoding.Json, "z.iso.date()")]
    [InlineData(IrEncoding.Rowset, "z.date()")]
    public void A_date_follows_its_encoding(IrEncoding encoding, string expected) =>
        Assert.Equal(expected, Haruspex.Emit.Zod.Printing.ZodPrinter.Expression(Lowering.Type(new IrScalar(IrScalarKind.Date, null, "date"), encoding)));

    [Fact]
    public void A_local_datetime_in_json_passes_its_option_as_an_object() =>
        Assert.Equal(
            new Call(new Name("z.iso"), "datetime", [new ObjectLiteral([new ZodProperty("local", new ZodExpression.Boolean(true))])]),
            Lowering.Type(new IrScalar(IrScalarKind.Datetime, IrFormat.Local, "datetime2"), IrEncoding.Json));

    [Fact]
    public void A_union_puts_the_variant_of_more_keys_first_and_each_refuses_the_others_keys()
    {
        var union = new IrUnion([new IrRef("Small"), new IrArray(new IrRef("Large")), Text10]);
        var never = new ObjectLiteral([new ZodProperty("b", Z("never").Then("optional"))]);

        Assert.Equal(
            new Union([Z("array", new Name("LargeSchema")), new Name("SmallSchema").Then("extend", never), String10]),
            Lowering.Type(union, IrEncoding.Json));
    }

    [Fact]
    public void A_union_leaves_out_a_variant_another_accepts_every_value_of()
    {
        var bigint = new IrScalar(IrScalarKind.Number, IrFormat.Int64, "bigint");
        var tinyint = new IrScalar(IrScalarKind.Number, IrFormat.Uint8, "tinyint");
        var spare = new IrScalar(IrScalarKind.String, null, "varchar(5)", new IrFacets(Values: new IrValueSet([new IrValue("SPARE", true)], IrValueMatch.Exact, [])));
        var text60 = new IrScalar(IrScalarKind.String, null, "nvarchar(60)", new IrFacets(Length: new IrLength(60, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Type, "nvarchar(60)"))));

        Assert.Equal(Z("string").Then("max", new Number("60")), Lowering.Type(new IrUnion([Text10, text60]), IrEncoding.Json));
        Assert.Equal(String10, Lowering.Type(new IrUnion([Text10, spare]), IrEncoding.Json));
        Assert.Equal(Lowering.Type(bigint, IrEncoding.Json), Lowering.Type(new IrUnion([bigint, bigint]), IrEncoding.Json));
        Assert.Equal(Lowering.Type(bigint, IrEncoding.Json), Lowering.Type(new IrUnion([tinyint, bigint]), IrEncoding.Json));
    }

    [Fact]
    public void A_union_keeps_variants_neither_of_which_covers_the_other()
    {
        var number = new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int");

        Assert.Equal(new Union([String10, Z("int32")]), Lowering.Type(new IrUnion([Text10, number]), IrEncoding.Json));
    }

    [Fact]
    public void An_int_states_only_the_bounds_its_own_range_does_not()
    {
        var check = new IrFactSource(IrFactSourceKind.Check, "dbo.ck");
        var positive = new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int", new IrFacets(Min: new IrBound("0", false, check), Max: new IrBound("2147483647", true, new IrFactSource(IrFactSourceKind.Type, "int"))));

        Assert.Equal(Z("int32").Then("gt", new Number("0")), Lowering.Type(positive, IrEncoding.Json));
    }

    [Fact]
    public void Padded_values_are_lowered_as_they_are_and_left_to_the_printer_to_escape()
    {
        var padded = new IrScalar(IrScalarKind.String, null, "char(4)", new IrFacets(Values: new IrValueSet([new IrValue("a/b ", true), new IrValue("a/b", true), new IrValue("x", true)], IrValueMatch.Padded, [])));

        Assert.Equal(Z("string").Then("regex", new PaddedValues(["a/b", "x"])), Lowering.Type(padded, IrEncoding.Json));
    }

    [Fact]
    public void A_field_left_out_when_null_is_optional_and_one_kept_as_null_is_nullable()
    {
        Assert.Equal(String10.Then("optional"), Lowering.Field(new IrField("v", Text10, IrPresence.OmittedWhenNull, [Because]), IrEncoding.Json));
        Assert.Equal(String10.Then("nullable"), Lowering.Field(new IrField("v", Text10, IrPresence.Always, [Because]), IrEncoding.Json));
        Assert.Equal(String10, Lowering.Field(new IrField("v", Text10, IrPresence.Always, []), IrEncoding.Json));
    }

    [Fact]
    public void An_unknown_field_is_not_made_nullable_since_unknown_accepts_null() =>
        Assert.Equal(
            new Noted(Z("unknown"), new ZodNote("Not known (unsupported-expression); the IR's unknowns say why.")),
            Lowering.Field(new IrField("v", new IrUnknownType(IrUnknownReason.UnsupportedExpression), IrPresence.Always, [Because]), IrEncoding.Json));
}

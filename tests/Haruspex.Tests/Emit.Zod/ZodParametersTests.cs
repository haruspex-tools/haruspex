using System.Text.RegularExpressions;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// A procedure's parameters as a strict schema of what may be sent, on hand-built IR: each value checked against its declared
/// type's facts, since SQL Server cuts short or rounds one outside them without an error (decision 0054), and a date and time
/// as the ISO 8601 string the server converts exactly (decision 0055).
/// </summary>
public class ZodParametersTests
{
    private static readonly IrFactSource Type = new(IrFactSourceKind.Type, "t");

    private static IrDocument Procedure(params IrParameter[] parameters) => new(
        new IrSource("tsql"),
        [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [.. parameters], [])],
        new Dictionary<string, IrObjectType>(),
        []);

    private static IrParameter Parameter(IrType type, bool hasDefault = false, bool output = false, string name = "@v") =>
        new(name, type, hasDefault, output);

    private static string Emitted(params IrParameter[] parameters) => ZodEmitter.Emit(Procedure(parameters)).Expected();

    /// <summary>The schema of <c>v</c>, the one parameter, without the <c>.nullable()</c> every parameter has.</summary>
    private static string Schema(IrType type)
    {
        var match = Regex.Match(Emitted(Parameter(type)), @"\n  v: (.*)\.nullable\(\),\n");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static IrScalar Text(int length, IrLengthUnit unit = IrLengthUnit.Utf16, bool isFixed = false, IrByteEncoding encoding = IrByteEncoding.Unknown) =>
        new(IrScalarKind.String, null, "nvarchar", new IrFacets(Length: new IrLength(length, isFixed, unit, encoding, Type)));

    [Fact]
    public void The_parameters_are_a_strict_object_named_for_the_procedure()
    {
        var ts = Emitted(Parameter(new IrScalar(IrScalarKind.Boolean, null, "bit"), name: "@is_open"));

        Assert.Contains("\nexport const PParamsSchema = z.strictObject({\n  is_open: z.boolean().nullable(),\n})\n", ts, StringComparison.Ordinal);
        Assert.Contains("\nexport type PParams = z.infer<typeof PParamsSchema>\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_procedure_without_parameters_has_no_parameters_schema() =>
        Assert.DoesNotContain("Params", Emitted(), StringComparison.Ordinal);

    [Fact]
    public void Every_parameter_accepts_null_and_one_with_a_default_may_be_left_out()
    {
        // An interpreted procedure can't declare a parameter NOT NULL, so null is a value it may be sent.
        var ts = Emitted(Parameter(new IrScalar(IrScalarKind.Boolean, null, "bit"), hasDefault: true));

        Assert.Contains("\n  v: z.boolean().nullable().optional(),\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_parameter_may_be_left_out_and_says_so()
    {
        var ts = Emitted(Parameter(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"), output: true));

        Assert.Matches(@"\n  /\*\* An OUTPUT parameter[^\n]*\*/\n  v: z\.int32\(\)\.nullable\(\)\.optional\(\),\n", ts);
    }

    [Theory]
    [InlineData(false)]
    // A shorter value is padded to a fixed length, so it is a maximum here, not the length every value has.
    [InlineData(true)]
    public void A_string_is_at_most_its_length_in_utf16_code_units(bool isFixed) =>

        // Not .max(20), which Zod counts in code points.
        Assert.Equal(
            "z.string().refine((value) => value.length <= 20, { error: \"More than 20 UTF-16 code units, which nvarchar would cut short\" })",
            Schema(Text(20, IrLengthUnit.Utf16, isFixed)));

    [Theory]
    [InlineData(IrByteEncoding.SingleByte, "code page")]
    [InlineData(IrByteEncoding.Utf8, "UTF-8")]
    [InlineData(IrByteEncoding.Unknown, "isn't declared")]
    public void A_length_in_bytes_is_checked_in_characters_and_says_what_more_the_server_may_change(IrByteEncoding encoding, string says)
    {
        var ts = Emitted(Parameter(Text(20, IrLengthUnit.Bytes, encoding: encoding)));

        Assert.Matches($@"/\*\* [^\n]*{Regex.Escape(says)}[^\n]*\*/\n  v: z\.string\(\)\.refine\(\(value\) => value\.length <= 20, [^\n]*\)\.nullable\(\),", ts);
    }

    [Fact]
    public void A_string_of_no_length_is_any_string() =>
        Assert.Equal("z.string()", Schema(new IrScalar(IrScalarKind.String, null, "nvarchar(max)")));

    [Fact]
    public void A_decimal_is_held_to_its_range_and_places() =>
        Assert.Equal(
            "z.number().min(-999.99).max(999.99).multipleOf(1e-2)",
            Schema(new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "decimal(5,2)", new IrFacets(
                Digits: new IrDigits(5, 2, Type), Min: new IrBound("-999.99", true, Type), Max: new IrBound("999.99", true, Type)))));

    [Theory]
    [InlineData(IrFormat.Int32, "z.int32()")]
    [InlineData(IrFormat.Uint8, "z.int().min(0).max(255)")]
    [InlineData(IrFormat.Float, "z.number()")]
    public void A_number_is_held_to_its_format(IrFormat format, string schema) =>
        Assert.Equal(schema, Schema(new IrScalar(IrScalarKind.Number, format, "t")));

    [Fact]
    public void A_floating_point_number_says_it_is_sent_as_the_nearest_value_its_type_holds() =>
        Assert.Matches(@"/\*\* real is binary floating point[^\n]*nearest[^\n]*\*/\n  v: z\.number\(\)\.nullable\(\),", Emitted(Parameter(new IrScalar(IrScalarKind.Number, IrFormat.Float, "real"))));

    [Fact]
    public void A_bigint_is_a_safe_integer_or_a_bigint_in_its_range() =>

        // A number past 2^53 is already rounded before the driver sees it, so a wider value is a JavaScript bigint.
        Assert.Equal(
            "z.union([z.int(), z.bigint().min(-9223372036854775808n).max(9223372036854775807n)])",
            Schema(new IrScalar(IrScalarKind.Number, IrFormat.Int64, "bigint")));

    [Theory]
    [InlineData("date", IrScalarKind.Date, null, "z.iso.date()")]
    [InlineData("time(0)", IrScalarKind.Time, null, @"z.iso.time().regex(/^\d{2}:\d{2}:\d{2}$/)")]
    [InlineData("time(3)", IrScalarKind.Time, null, @"z.iso.time().regex(/^\d{2}:\d{2}:\d{2}(\.\d{1,3})?$/)")]
    [InlineData("datetime2(7)", IrScalarKind.Datetime, IrFormat.Local, @"z.iso.datetime({ local: true }).regex(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?$/)")]
    [InlineData("datetimeoffset(0)", IrScalarKind.Datetime, IrFormat.Offset, @"z.iso.datetime({ offset: true }).regex(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(Z|[+-]\d{2}:\d{2})$/)")]
    [InlineData("datetime", IrScalarKind.Datetime, IrFormat.Local, @"z.iso.datetime({ local: true }).regex(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d(\d[037]?)?)?$/).refine((value) => value >= ""1753-01-01"", { error: ""Before 1753-01-01, the earliest datetime"" })")]
    [InlineData("smalldatetime", IrScalarKind.Datetime, IrFormat.Local, @"z.iso.datetime({ local: true }).regex(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00$/).refine((value) => value >= ""1900-01-01"" && value <= ""2079-06-06T23:59:00"", { error: ""Outside 1900-01-01 to 2079-06-06T23:59:00, the range of smalldatetime"" })")]
    public void A_date_and_time_is_an_iso_string_of_the_form_and_range_its_type_holds_exactly(string sql, IrScalarKind kind, IrFormat? format, string schema) =>
        Assert.Equal(schema, Schema(new IrScalar(kind, format, sql)));

    [Fact]
    public void A_date_and_time_of_a_type_the_emitter_does_not_know_says_its_fraction_may_be_rounded()
    {
        var ts = Emitted(Parameter(new IrScalar(IrScalarKind.Datetime, IrFormat.Local, null)));

        Assert.Matches(@"/\*\* [^\n]*rounded[^\n]*\*/\n  v: z\.iso\.datetime\(\{ local: true \}\)\.nullable\(\),", ts);
    }

    [Fact]
    public void A_json_parameter_is_text_whose_shape_is_not_typed_yet()
    {
        var ts = Emitted(Parameter(new IrJsonText(null)));

        Assert.Matches(@"/\*\* [^\n]*JSON[^\n]*\*/\n  v: z\.string\(\)\.nullable\(\),", ts);
    }

    [Fact]
    public void An_unknown_parameter_is_unknown_with_its_reason()
    {
        var ts = Emitted(Parameter(new IrUnknownType(IrUnknownReason.UnsupportedSqlType)));

        Assert.Matches(@"/\*\* Not known \(unsupported-sql-type\)[^\n]*\*/\n  v: z\.unknown\(\),", ts);
    }

    [Fact]
    public void The_parameters_schema_comes_before_the_results_schema() =>
        Assert.Matches(@"PParamsSchema = [\s\S]*PSchema = ", Emitted(Parameter(new IrScalar(IrScalarKind.Boolean, null, "bit"))));

    [Fact]
    public void A_name_the_parameters_schema_would_take_is_refused()
    {
        var document = Procedure(Parameter(new IrScalar(IrScalarKind.Boolean, null, "bit"))) with
        {
            Types = new Dictionary<string, IrObjectType> { ["PParams"] = new([]) },
        };

        var refusal = Assert.Single(ZodEmitter.Emit(document).Diagnostics);
        Assert.Equal(ZodCodes.ParametersNameTaken, refusal.Code);
    }
}

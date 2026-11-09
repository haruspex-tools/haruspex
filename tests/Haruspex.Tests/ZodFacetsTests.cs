using System.Text.RegularExpressions;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// The Zod emitter's use of every fact the IR carries ("claim everything the database guarantees"), on hand-built
/// IR. Each mapping that holds only under a condition (a single-byte collation, a JSON number, a safe integer)
/// has a case on each side of it.
/// </summary>
public class ZodFacetsTests
{
    private static readonly IrFactSource Type = new(IrFactSources.Type, "t");
    private static readonly IrFactSource Check = new(IrFactSources.Check, "dbo.ck");

    private static string Emit(IrEncoding encoding, IrScalar scalar)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(encoding, new IrArray(new IrRef("PRow")), [])])],
            new Dictionary<string, IrObjectType> { ["PRow"] = new([new IrField("v", scalar, IrPresence.Always, [])]) },
            []);
        var ts = ZodEmitter.Emit(doc);
        var line = Regex.Match(ts, @"\n  v: (?<code>.*),\n");
        Assert.True(line.Success, ts);
        return line.Groups["code"].Value;
    }

    private static string Json(IrScalar scalar) => Emit(IrEncoding.Json, scalar);

    private static string Rows(IrScalar scalar) => Emit(IrEncoding.Rowset, scalar);

    private static IrScalar Text(string sql, IrFacets facets) => new(IrScalarKind.String, null, sql, facets);

    private static IrScalar Number(string format, string sql, IrFacets facets) => new(IrScalarKind.Number, format, sql, facets);

    private static IrBound Incl(string value, IrFactSource? from = null) => new(value, true, from ?? Type);

    private static IrBound Excl(string value) => new(value, false, Check);

    private static IrValueSet Numbers(params string[] values) => new([.. values.Select(v => new IrValue(v, false))], IrValueMatch.Exact, [Check]);

    private static IrValueSet Strings(IrValueMatch match, params string[] values) => new([.. values.Select(v => new IrValue(v, true))], match, [Check]);

    // ---- strings ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("nvarchar(40)", 40, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, "z.string().max(40)")]
    [InlineData("nchar(5)", 5, true, IrLengthUnit.Utf16, IrByteEncoding.Unknown, "z.string().length(5)")]
    // In a single-byte collation a byte is a character, so varchar and char are exact.
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, "z.string().max(5) /* varchar(5) counts bytes, one per character in its single-byte collation: exact */")]
    [InlineData("char(5)", 5, true, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, "z.string().length(5) /* char(5) pads to 5 bytes, one per character in its single-byte collation */")]
    // In UTF-8 the byte count only bounds the length, and a padded value can be shorter than n.
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.Utf8, "z.string().max(5) /* varchar(5) counts bytes, and UTF-8 takes up to four per character: an upper bound */")]
    [InlineData("char(5)", 5, true, IrLengthUnit.Bytes, IrByteEncoding.Utf8, "z.string().max(5) /* char(5) pads to 5 bytes, and UTF-8 takes up to four per character, so the length is at most 5 */")]
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.DoubleByte, "z.string().max(5) /* varchar(5) counts bytes, and its double-byte collation takes up to two per character: an upper bound */")]
    // With no collation declared, only what holds in every encoding.
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.Unknown, "z.string().max(5) /* varchar(5) counts bytes: exact in a single-byte collation, an upper bound otherwise; no collation is declared */")]
    [InlineData("char(5)", 5, true, IrLengthUnit.Bytes, IrByteEncoding.Unknown, "z.string().max(5) /* char(5) pads to 5 bytes: 5 characters in a single-byte collation, at most 5 otherwise; no collation is declared */")]
    public void A_length_is_exact_where_the_target_counts_the_same_units(string sql, int n, bool isFixed, IrLengthUnit unit, IrByteEncoding encoding, string zod)
    {
        Assert.Equal(zod, Json(Text(sql, new IrFacets(Length: new IrLength(n, isFixed, unit, encoding, Type)))));
        Assert.Equal(zod, Rows(Text(sql, new IrFacets(Length: new IrLength(n, isFixed, unit, encoding, Type)))));
    }

    [Fact]
    public void A_minimum_length_comes_before_the_maximum() =>
        Assert.Equal("z.string().min(1).max(60)", Json(Text("nvarchar(60)", new IrFacets(Length: new IrLength(60, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Type), MinLength: new IrMinLength(1, Check)))));

    [Fact]
    public void An_exact_set_of_strings_is_an_enum() =>
        Assert.Equal("z.enum([\"a\", \"b \\\"quoted\\\"\"])", Json(Text("nvarchar(10)", new IrFacets(Values: Strings(IrValueMatch.Exact, "a", "b \"quoted\"")))));

    [Fact]
    public void A_set_of_strings_under_a_collation_is_a_comment_never_an_enum()
    {
        var zod = Json(Text("varchar(10)", new IrFacets(Values: Strings(IrValueMatch.Collation, "open", "held"))));

        Assert.StartsWith("z.string() /* one of \"open\", \"held\" as the database compares strings", zod, StringComparison.Ordinal);
        Assert.DoesNotContain("z.enum", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_of_strings_exact_but_for_trailing_spaces_is_a_regex_with_its_items_escaped() =>
        Assert.Equal(
            "z.string().regex(/^(?:open|a\\u002eb|\\u002f\\u002a\\u005c|)\\u0020*$/).max(10)",
            Json(Text("varchar(10)", new IrFacets(
                Length: new IrLength(10, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Type),
                Values: Strings(IrValueMatch.Padded, "open", "a.b", "/*\\", "")))));

    [Fact]
    public void A_value_in_a_comment_cannot_close_it() =>
        Assert.DoesNotContain("*/ x", Json(Text("varchar(10)", new IrFacets(Values: Strings(IrValueMatch.Collation, "*/ x")))), StringComparison.Ordinal);

    // ---- integers -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(IrFormats.Int32, "-2147483648", "2147483647", "z.int32()")]
    [InlineData(IrFormats.Uint8, "0", "255", "z.int().min(0).max(255)")]
    [InlineData(IrFormats.Int16, "-32768", "32767", "z.int().min(-32768).max(32767)")]
    [InlineData(IrFormats.Int32, "1", "5", "z.int().min(1).max(5)")]
    public void An_integer_has_its_range(string format, string min, string max, string zod)
    {
        var scalar = Number(format, "int", new IrFacets(Min: Incl(min), Max: Incl(max)));

        Assert.Equal(zod, Json(scalar));
        Assert.Equal(zod, Rows(scalar));
    }

    [Fact]
    public void An_exclusive_bound_on_a_safe_integer_stays_exclusive() =>
        Assert.Equal("z.int().gt(0).lt(100)", Json(Number(IrFormats.Int32, "int", new IrFacets(Min: Excl("0"), Max: Excl("100")))));

    [Fact]
    public void An_exact_set_of_integers_is_a_literal() =>
        Assert.Equal("z.literal([1, 2, 4])", Json(Number(IrFormats.Int16, "smallint", new IrFacets(Min: Incl("-32768"), Max: Incl("32767"), Values: Numbers("1", "2", "4")))));

    [Fact]
    public void An_empty_set_admits_no_value() =>
        Assert.Equal("z.never()", Json(Number(IrFormats.Int32, "int", new IrFacets(Values: Numbers()))));

    [Fact]
    public void A_bigint_in_json_keeps_its_bare_number_and_only_a_checks_range_inclusively()
    {
        var typeRange = new IrFacets(Min: Incl("-9223372036854775808"), Max: Incl("9223372036854775807"));
        Assert.Equal("z.number() /* int64: exact only up to 2^53 once parsed */", Json(Number(IrFormats.Int64, "bigint", typeRange)));

        // Exclusive bounds become inclusive, because two bigints past 2^53 can parse to the same double.
        var checkRange = new IrFacets(Min: Excl("0"), Max: new IrBound("9007199254740993", false, Check));
        Assert.Equal("z.number().min(1).max(9007199254740992) /* int64: exact only up to 2^53 once parsed */", Json(Number(IrFormats.Int64, "bigint", checkRange)));
    }

    [Fact]
    public void A_bigint_in_a_row_is_a_string_whose_set_is_strings()
    {
        Assert.Equal("z.string()", Rows(Number(IrFormats.Int64, "bigint", new IrFacets(Min: Incl("-9223372036854775808"), Max: Incl("9223372036854775807")))));
        Assert.Equal("z.enum([\"1\", \"20\"])", Rows(Number(IrFormats.Int64, "bigint", new IrFacets(Values: Numbers("1", "20")))));
        Assert.Equal("z.string() /* a bigint arrives as a string, so its CHECK range is not checked */", Rows(Number(IrFormats.Int64, "bigint", new IrFacets(Min: Incl("0", Check)))));
    }

    // ---- decimals -----------------------------------------------------------------------------------

    private static IrFacets Decimal(int precision, int scale, IrBound min, IrBound max, IrValueSet? values = null) =>
        new(Digits: new IrDigits(precision, scale, Type), Min: min, Max: max, Values: values);

    [Theory]
    [InlineData(19, 2, "-99999999999999999.99", "99999999999999999.99", "z.number().min(-99999999999999999.99).max(99999999999999999.99).multipleOf(1e-2)")]
    [InlineData(5, 5, "-0.99999", "0.99999", "z.number().min(-0.99999).max(0.99999).multipleOf(1e-5)")]
    [InlineData(19, 4, "-922337203685477.5808", "922337203685477.5807", "z.number().min(-922337203685477.5808).max(922337203685477.5807).multipleOf(1e-4)")]
    // An integer decimal within 2^53 is an integer; a wider one can't be z.int(), which wants safe integers.
    [InlineData(10, 0, "-9999999999", "9999999999", "z.int().min(-9999999999).max(9999999999)")]
    [InlineData(38, 0, "-99999999999999999999999999999999999999", "99999999999999999999999999999999999999", "z.number().min(-99999999999999999999999999999999999999).max(99999999999999999999999999999999999999).multipleOf(1)")]
    public void A_decimal_in_json_has_its_range_and_its_step(int precision, int scale, string min, string max, string zod) =>
        Assert.Equal(zod, Json(Number(IrFormats.Decimal, "decimal", Decimal(precision, scale, Incl(min), Incl(max)))));

    [Theory]
    [InlineData(9, 4, "0", "1", "z.number().min(0.0001).max(0.9999).multipleOf(1e-4)")]
    [InlineData(5, 2, "-100", "100", "z.number().min(-99.99).max(99.99).multipleOf(1e-2)")]
    [InlineData(5, 0, "-1", "10", "z.int().min(0).max(9)")]
    public void An_exclusive_bound_on_a_decimal_becomes_the_nearest_inclusive_one(int precision, int scale, string min, string max, string zod) =>
        Assert.Equal(zod, Json(Number(IrFormats.Decimal, "decimal", Decimal(precision, scale, Excl(min), Excl(max)))));

    [Fact]
    public void A_set_of_decimals_is_a_literal() =>
        Assert.Equal("z.literal([1.5, 2.25])", Json(Number(IrFormats.Decimal, "decimal(5,2)", Decimal(5, 2, Incl("-999.99"), Incl("999.99"), Numbers("1.5", "2.25")))));

    [Fact]
    public void A_narrow_decimal_in_a_row_keeps_its_bounds() =>
        Assert.Equal("z.number().min(-214748.3648).max(214748.3647).multipleOf(1e-4)", Rows(Number(IrFormats.Decimal, "smallmoney", Decimal(10, 4, Incl("-214748.3648"), Incl("214748.3647")))));

    [Fact]
    public void A_wide_decimal_in_a_row_keeps_its_step_but_not_its_bounds_or_values()
    {
        Assert.Equal(
            "z.number().multipleOf(1e-2) /* the mssql driver may round decimal(19,2) past its bounds, so they are not checked in a row */",
            Rows(Number(IrFormats.Decimal, "decimal(19,2)", Decimal(19, 2, Incl("-99999999999999999.99"), Incl("99999999999999999.99"), Numbers("1.5")))));
    }

    [Fact]
    public void A_float_takes_only_inclusive_bounds() =>
        Assert.Equal("z.number().min(0).max(1)", Json(Number(IrFormats.Float, "float", new IrFacets(Min: Excl("0"), Max: Incl("1")))));

    [Fact]
    public void A_number_without_facets_is_unchanged() =>
        Assert.Equal("z.number()", Json(new IrScalar(IrScalarKind.Number, IrFormats.Decimal, "decimal(19,2)")));
}

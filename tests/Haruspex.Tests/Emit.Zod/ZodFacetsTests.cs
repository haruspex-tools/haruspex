using System.Text.RegularExpressions;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// The Zod emitter's use of every fact the IR carries ("claim everything the database guarantees"), on hand-built
/// IR. Each mapping that holds only under a condition (a single-byte collation, a JSON number, a safe integer)
/// has a case on each side of it.
/// </summary>
public class ZodFacetsTests
{
    private static readonly IrFactSource Type = new(IrFactSourceKind.Type, "t");
    private static readonly IrFactSource Check = new(IrFactSourceKind.Check, "dbo.ck");

    private static string Emit(IrEncoding encoding, IrScalar scalar)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(encoding, new IrArray(new IrRef("PRow")), [])])],
            new Dictionary<string, IrObjectType> { ["PRow"] = new([new IrField("v", scalar, IrPresence.Always, [])]) },
            []);
        var ts = ZodEmitter.Emit(doc).Expected();
        var line = Regex.Match(ts, @"\n(?:  /\*\* (?<note>.*) \*/\n|  /\*\*\n(?:   \* (?<note>.*)\n)+   \*/\n)?  v: (?<code>.*),\n");
        Assert.True(line.Success, ts);

        // The code, then its notes after "//", as one line a case can compare.
        var notes = line.Groups["note"].Captures.Select(c => c.Value).ToList();
        return notes.Count == 0 ? line.Groups["code"].Value : line.Groups["code"].Value + " // " + string.Join(" ", notes);
    }

    private static string Json(IrScalar scalar) => Emit(IrEncoding.Json, scalar);

    private static string Rows(IrScalar scalar) => Emit(IrEncoding.Rowset, scalar);

    private static IrScalar Text(string? sql, IrFacets facets) => new(IrScalarKind.String, null, sql, facets);

    private static IrScalar Number(IrFormat format, string sql, IrFacets facets) => new(IrScalarKind.Number, format, sql, facets);

    private static IrBound Inclusive(string value, IrFactSource? from = null) => new(value, true, from ?? Type);

    private static IrBound Exclusive(string value) => new(value, false, Check);

    private static IrValueSet Numbers(params string[] values) => new([.. values.Select(v => new IrValue(v, false))], IrValueMatch.Exact, [Check]);

    private static IrValueSet Strings(IrValueMatch match, params string[] values) => new([.. values.Select(v => new IrValue(v, true))], match, [Check]);

    // ---- strings ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("nvarchar(40)", 40, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, "z.string().max(40)")]
    // Zod counts code points, and a pair of UTF-16 code units is one; a value converted under an _SC collation is cut before a pair
    // that doesn't fit, one code unit short and unpadded. So nchar(5) holds 2 to 5 code points, and nchar(1) may be empty (observed:
    // A_string_s_length_is_counted_by_the_server_in_code_units_or_bytes_and_by_zod_in_code_points).
    [InlineData("nchar(5)", 5, true, IrLengthUnit.Utf16, IrByteEncoding.Unknown, "z.string().min(2).max(5) // nchar(5) is 5 UTF-16 code units, or 4 UTF-16 code units where an _SC collation cuts off a pair, which is 2 to 5 code points.")]
    [InlineData("nchar(4)", 4, true, IrLengthUnit.Utf16, IrByteEncoding.Unknown, "z.string().min(2).max(4) // nchar(4) is 4 UTF-16 code units, or 3 UTF-16 code units where an _SC collation cuts off a pair, which is 2 to 4 code points.")]
    [InlineData("nchar(1)", 1, true, IrLengthUnit.Utf16, IrByteEncoding.Unknown, "z.string().max(1) // nchar(1) is 1 UTF-16 code unit, or 0 UTF-16 code units where an _SC collation cuts off a pair, which is 0 to 1 code points.")]
    // In a single-byte collation a byte is a character, so varchar and char are exact, and need no note.
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, "z.string().max(5)")]
    [InlineData("char(5)", 5, true, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, "z.string().length(5)")]
    // In UTF-8 the byte count only bounds the length, and a padded value can be as short as a quarter of n.
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.Utf8, "z.string().max(5) // varchar(5) counts bytes and UTF-8 takes up to four a character, so the length is at most 5.")]
    [InlineData("char(5)", 5, true, IrLengthUnit.Bytes, IrByteEncoding.Utf8, "z.string().min(2).max(5) // char(5) pads to 5 bytes and UTF-8 takes up to four a character, so the length is 2 to 5 code points.")]
    [InlineData("char(1)", 1, true, IrLengthUnit.Bytes, IrByteEncoding.Utf8, "z.string().length(1)")]
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.DoubleByte, "z.string().max(5) // varchar(5) counts bytes and its double-byte collation takes up to two a character, so the length is at most 5.")]
    [InlineData("char(5)", 5, true, IrLengthUnit.Bytes, IrByteEncoding.DoubleByte, "z.string().min(3).max(5) // char(5) pads to 5 bytes and its double-byte collation takes up to two a character, so the length is 3 to 5 code points.")]
    // With no collation declared, only what holds in every encoding.
    [InlineData("varchar(5)", 5, false, IrLengthUnit.Bytes, IrByteEncoding.Unknown, "z.string().max(5) // varchar(5) counts bytes and its collation isn't declared, so the length is at most 5.")]
    [InlineData("char(5)", 5, true, IrLengthUnit.Bytes, IrByteEncoding.Unknown, "z.string().min(2).max(5) // char(5) pads to 5 bytes and its collation isn't declared, so the length is 2 to 5 code points.")]
    // A length a declaration gives in code points is Zod's own unit.
    [InlineData(null, 5, true, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, "z.string().length(5)")]
    public void A_length_is_exact_only_where_every_character_is_one_code_point_of_what_it_counts(string? sql, int n, bool isFixed, IrLengthUnit unit, IrByteEncoding encoding, string zod)
    {
        Assert.Equal(zod, Json(Text(sql, new IrFacets(Length: new IrLength(n, isFixed, unit, encoding, Type)))));
        Assert.Equal(zod, Rows(Text(sql, new IrFacets(Length: new IrLength(n, isFixed, unit, encoding, Type)))));
    }

    [Fact]
    public void A_minimum_length_comes_before_the_maximum() =>
        Assert.Equal("z.string().min(1).max(60)", Json(Text("nvarchar(60)", new IrFacets(Length: new IrLength(60, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Type), MinLength: new IrMinLength(1, Check)))));

    [Theory]
    // The IR's minimum counts UTF-16 code units, of which a code point takes one or two.
    [InlineData(2, "z.string().min(1).max(20) // At least 2 UTF-16 code units, which is at least 1 code point.")]
    [InlineData(5, "z.string().min(3).max(20) // At least 5 UTF-16 code units, which is at least 3 code points.")]
    public void A_minimum_length_in_utf_16_code_units_is_half_as_many_code_points_rounded_up(int least, string zod) =>
        Assert.Equal(zod, Json(Text("nvarchar(20)", new IrFacets(Length: new IrLength(20, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Type), MinLength: new IrMinLength(least, Check)))));

    [Fact]
    public void A_fixed_length_and_a_minimum_claim_the_greater_of_their_code_points() =>
        Assert.Equal(
            "z.string().min(3).max(8) // char(8) pads to 8 bytes and UTF-8 takes up to four a character, so the length is 2 to 8 code points. At least 6 UTF-16 code units, which is at least 3 code points.",
            Json(Text("char(8)", new IrFacets(Length: new IrLength(8, true, IrLengthUnit.Bytes, IrByteEncoding.Utf8, Type), MinLength: new IrMinLength(6, Check)))));

    [Fact]
    public void An_exact_set_of_strings_is_an_enum() =>
        Assert.Equal("z.enum([\"a\", \"b \\\"quoted\\\"\"])", Json(Text("nvarchar(10)", new IrFacets(Values: Strings(IrValueMatch.Exact, "a", "b \"quoted\"")))));

    [Fact]
    public void A_set_of_strings_under_a_collation_is_a_comment_never_an_enum()
    {
        var zod = Json(Text("varchar(10)", new IrFacets(Values: Strings(IrValueMatch.Collation, "open", "held"))));

        Assert.Equal("z.string() // One of \"open\", \"held\", as the database compares strings.", zod);
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
    public void A_padded_items_own_trailing_spaces_do_not_make_the_regex_require_them() =>
        Assert.Equal(
            "z.string().regex(/^(?:a|b)\\u0020*$/)",
            Json(Text("varchar(6)", new IrFacets(Values: Strings(IrValueMatch.Padded, "a ", "b")))));

    [Fact]
    public void A_value_in_a_comment_cannot_close_it() =>
        Assert.DoesNotContain("*/ x", Json(Text("varchar(10)", new IrFacets(Values: Strings(IrValueMatch.Collation, "*/ x")))), StringComparison.Ordinal);

    // ---- integers -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(IrFormat.Int32, "-2147483648", "2147483647", "z.int32()")]
    [InlineData(IrFormat.Uint8, "0", "255", "z.int().min(0).max(255)")]
    [InlineData(IrFormat.Int16, "-32768", "32767", "z.int().min(-32768).max(32767)")]
    [InlineData(IrFormat.Int32, "1", "5", "z.int().min(1).max(5)")]
    public void An_integer_has_its_range(IrFormat format, string min, string max, string zod)
    {
        var scalar = Number(format, "int", new IrFacets(Min: Inclusive(min), Max: Inclusive(max)));

        Assert.Equal(zod, Json(scalar));
        Assert.Equal(zod, Rows(scalar));
    }

    [Fact]
    public void An_exclusive_bound_on_a_safe_integer_stays_exclusive() =>
        Assert.Equal("z.int().gt(0).lt(100)", Json(Number(IrFormat.Int32, "int", new IrFacets(Min: Exclusive("0"), Max: Exclusive("100")))));

    [Fact]
    public void An_exact_set_of_integers_is_a_literal() =>
        Assert.Equal("z.literal([1, 2, 4])", Json(Number(IrFormat.Int16, "smallint", new IrFacets(Min: Inclusive("-32768"), Max: Inclusive("32767"), Values: Numbers("1", "2", "4")))));

    [Fact]
    public void An_empty_set_admits_no_value() =>
        Assert.Equal("z.never()", Json(Number(IrFormat.Int32, "int", new IrFacets(Values: Numbers()))));

    [Fact]
    public void A_bigint_in_json_keeps_its_bare_number_and_only_a_checks_range_inclusively()
    {
        var typeRange = new IrFacets(Min: Inclusive("-9223372036854775808"), Max: Inclusive("9223372036854775807"));
        Assert.Equal("z.number() // A bigint, exact only up to 2^53 once parsed.", Json(Number(IrFormat.Int64, "bigint", typeRange)));

        // Exclusive bounds become inclusive, because two bigints past 2^53 can parse to the same double.
        var checkRange = new IrFacets(Min: Exclusive("0"), Max: new IrBound("9007199254740993", false, Check));
        Assert.Equal("z.number().min(1).max(9007199254740992) // A bigint, exact only up to 2^53 once parsed.", Json(Number(IrFormat.Int64, "bigint", checkRange)));
    }

    [Fact]
    public void A_bigint_in_a_row_is_a_string_whose_set_is_strings()
    {
        Assert.Equal("z.string()", Rows(Number(IrFormat.Int64, "bigint", new IrFacets(Min: Inclusive("-9223372036854775808"), Max: Inclusive("9223372036854775807")))));
        Assert.Equal("z.enum([\"1\", \"20\"])", Rows(Number(IrFormat.Int64, "bigint", new IrFacets(Values: Numbers("1", "20")))));
        Assert.Equal("z.string() // A bigint arrives as a string, so its CHECK range is not checked.", Rows(Number(IrFormat.Int64, "bigint", new IrFacets(Min: Inclusive("0", Check)))));
    }

    // ---- decimals -----------------------------------------------------------------------------------

    private static IrFacets Decimal(int precision, int scale, IrBound min, IrBound max, IrValueSet? values = null) =>
        new(Digits: new IrDigits(precision, scale, Type), Min: min, Max: max, Values: values);

    // A bound a double can't hold is written as the double JavaScript reads it as, which is the check it always was, and
    // the note keeps the exact bounds for the reader.
    [Theory]
    [InlineData(19, 2, "-99999999999999999.99", "99999999999999999.99", "z.number().min(-100000000000000000).max(100000000000000000).multipleOf(1e-2) // decimal runs from -99999999999999999.99 to 99999999999999999.99, which JavaScript reads as the nearest doubles.")]
    [InlineData(5, 5, "-0.99999", "0.99999", "z.number().min(-0.99999).max(0.99999).multipleOf(1e-5)")]
    [InlineData(19, 4, "-922337203685477.5808", "922337203685477.5807", "z.number().min(-922337203685477.6).max(922337203685477.6).multipleOf(1e-4) // decimal runs from -922337203685477.5808 to 922337203685477.5807, which JavaScript reads as the nearest doubles.")]
    // An integer decimal within 2^53 is an integer; a wider one can't be z.int(), which wants safe integers.
    [InlineData(10, 0, "-9999999999", "9999999999", "z.int().min(-9999999999).max(9999999999)")]
    [InlineData(38, 0, "-99999999999999999999999999999999999999", "99999999999999999999999999999999999999", "z.number().min(-1e+38).max(1e+38).multipleOf(1) // decimal runs from -99999999999999999999999999999999999999 to 99999999999999999999999999999999999999, which JavaScript reads as the nearest doubles.")]
    public void A_decimal_in_json_has_its_range_and_its_step(int precision, int scale, string min, string max, string zod) =>
        Assert.Equal(zod, Json(Number(IrFormat.Decimal, "decimal", Decimal(precision, scale, Inclusive(min), Inclusive(max)))));

    [Theory]
    [InlineData(9, 4, "0", "1", "z.number().min(0.0001).max(0.9999).multipleOf(1e-4)")]
    [InlineData(5, 2, "-100", "100", "z.number().min(-99.99).max(99.99).multipleOf(1e-2)")]
    [InlineData(5, 0, "-1", "10", "z.int().min(0).max(9)")]
    public void An_exclusive_bound_on_a_decimal_becomes_the_nearest_inclusive_one(int precision, int scale, string min, string max, string zod) =>
        Assert.Equal(zod, Json(Number(IrFormat.Decimal, "decimal", Decimal(precision, scale, Exclusive(min), Exclusive(max)))));

    [Fact]
    public void A_single_wide_bound_says_which_side_it_is_on()
    {
        var digits = new IrDigits(19, 2, Type);
        Assert.EndsWith(
            "// decimal is at most 99999999999999999.99, which JavaScript reads as the nearest double.",
            Json(Number(IrFormat.Decimal, "decimal", new IrFacets(Digits: digits, Max: Inclusive("99999999999999999.99")))),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "// decimal is at least -99999999999999999.99, which JavaScript reads as the nearest double.",
            Json(Number(IrFormat.Decimal, "decimal", new IrFacets(Digits: digits, Min: Inclusive("-99999999999999999.99")))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_of_decimals_is_a_literal() =>
        Assert.Equal("z.literal([1.5, 2.25])", Json(Number(IrFormat.Decimal, "decimal(5,2)", Decimal(5, 2, Inclusive("-999.99"), Inclusive("999.99"), Numbers("1.5", "2.25")))));

    [Fact]
    public void A_set_of_numbers_of_no_format_is_a_literal_in_json() =>
        // A number written into JSON in the SQL's text (json_query('[2024]')), or a bit converted to text: SQL Server hands the text
        // on unchanged, so JSON.parse reads it and the literal here as one decimal, and so one double.
        Assert.Equal("z.literal([0, 2024])", Json(new IrScalar(IrScalarKind.Number, null, null, new IrFacets(Values: Numbers("0", "2024")))));

    [Fact]
    public void A_set_of_floats_stays_a_number_since_for_json_writes_sixteen_digits() =>
        Assert.Equal("z.number()", Json(Number(IrFormat.Float, "float", new IrFacets(Values: Numbers("0.1")))));

    [Fact]
    public void A_narrow_decimal_in_a_row_keeps_its_bounds() =>
        Assert.Equal("z.number().min(-214748.3648).max(214748.3647).multipleOf(1e-4)", Rows(Number(IrFormat.Decimal, "smallmoney", Decimal(10, 4, Inclusive("-214748.3648"), Inclusive("214748.3647")))));

    [Fact]
    public void A_wide_decimal_in_a_row_keeps_its_step_but_not_its_bounds_or_values()
    {
        Assert.Equal(
            "z.number().multipleOf(1e-2) // The driver may round decimal(19,2) past its bounds in a row, so they are not checked.",
            Rows(Number(IrFormat.Decimal, "decimal(19,2)", Decimal(19, 2, Inclusive("-99999999999999999.99"), Inclusive("99999999999999999.99"), Numbers("1.5")))));
    }

    [Fact]
    public void A_float_takes_only_inclusive_bounds() =>
        Assert.Equal("z.number().min(0).max(1)", Json(Number(IrFormat.Float, "float", new IrFacets(Min: Exclusive("0"), Max: Inclusive("1")))));

    [Fact]
    public void A_number_without_facets_is_unchanged() =>
        Assert.Equal("z.number()", Json(new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "decimal(19,2)")));
}

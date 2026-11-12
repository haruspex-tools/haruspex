using System.Data.SqlTypes;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>
/// The validator's checks of the IR's facets, with hand-written IR and output. Each facet has a value that keeps it
/// and one that breaks it, in JSON and in a rowset, and each edge it reports is one the value reaches.
/// </summary>
public class FacetValidationTests
{
    private const string JsonColumn = "JSON_F52E2B61-18A1-11d1-B105-00805F49916B";
    private static readonly IrFactSource From = new(IrFactSourceKind.Check, "dbo.ck");

    private static ValidationReport Json(IrScalar scalar, string valueJson)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrArray(new IrRef("Row")), [])])],
            new Dictionary<string, IrObjectType> { ["Row"] = new([new IrField("v", scalar, IrPresence.Always, [])]) },
            []);
        return IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet([JsonColumn], [[$$"""[{"v":{{valueJson}}}]"""]])]);
    }

    private static ValidationReport Row(IrScalar scalar, object value)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Row")), [])])],
            new Dictionary<string, IrObjectType> { ["Row"] = new([new IrField("v", scalar, IrPresence.Always, [])]) },
            []);
        return IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["v"], [[value]])]);
    }

    private static IrScalar Text(string sql, IrFacets facets) => new(IrScalarKind.String, null, sql, facets);

    private static IrScalar Number(IrFormat format, string sql, IrFacets facets) => new(IrScalarKind.Number, format, sql, facets);

    private static IrFacets Length(int n, bool isFixed, IrLengthUnit unit = IrLengthUnit.Utf16, IrByteEncoding encoding = IrByteEncoding.Unknown) =>
        new(Length: new IrLength(n, isFixed, unit, encoding, From));

    private static IReadOnlyList<string> Edges(ValidationReport report) => [.. report.Edges.Select(e => e.Edge).Order(StringComparer.Ordinal)];

    // ---- strings ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(5, false, "\"abcde\"", true)]
    [InlineData(5, false, "\"abcdef\"", false)]
    [InlineData(5, true, "\"ab   \"", true)]
    [InlineData(5, true, "\"ab\"", false)]
    public void A_utf16_length_is_exact(int n, bool isFixed, string value, bool keeps)
    {
        var report = Json(Text("nvarchar", Length(n, isFixed)), value);

        Assert.Equal(keeps, report.Violations.Count == 0);
    }

    [Fact]
    public void A_utf8_length_counts_bytes()
    {
        var length = Length(4, true, IrLengthUnit.Bytes, IrByteEncoding.Utf8);

        Assert.Empty(Json(Text("char(4)", length), "\"é  \"").Violations);
        Assert.Single(Json(Text("char(4)", length), "\"é   \"").Violations);
        Assert.Equal(["length 4"], Edges(Json(Text("char(4)", length), "\"é  \"")));
    }

    [Fact]
    public void A_length_in_bytes_of_an_unknown_encoding_is_only_an_upper_bound()
    {
        var length = Length(5, true, IrLengthUnit.Bytes);

        Assert.Empty(Row(Text("char(5)", length), "abc").Violations);
        Assert.Single(Row(Text("char(5)", length), "abcdef").Violations);
    }

    [Fact]
    public void A_minimum_length_is_checked_and_its_edge_reported()
    {
        var facets = new IrFacets(MinLength: new IrMinLength(2, From));

        Assert.Single(Json(Text("nvarchar(9)", facets), "\"a\"").Violations);
        Assert.Equal(["min length 2"], Edges(Json(Text("nvarchar(9)", facets), "\"ab\"")));
    }

    [Fact]
    public void An_exact_set_of_strings_is_matched_exactly()
    {
        var facets = new IrFacets(Values: new IrValueSet([new IrValue("open", true)], IrValueMatch.Exact, [From]));

        Assert.Equal(["value open"], Edges(Json(Text("varchar(9)", facets), "\"open\"")));
        Assert.Single(Json(Text("varchar(9)", facets), "\"Open\"").Violations);
    }

    [Fact]
    public void A_set_under_a_collation_is_matched_ignoring_case_and_trailing_spaces()
    {
        var facets = new IrFacets(Values: new IrValueSet([new IrValue("open", true)], IrValueMatch.Collation, [From]));

        Assert.Equal(["value open"], Edges(Row(Text("varchar(9)", facets), "OPEN  ")));
        Assert.Single(Row(Text("varchar(9)", facets), "opened").Violations);
    }

    [Fact]
    public void A_set_exact_but_for_trailing_spaces_is_matched_after_removing_them()
    {
        var facets = new IrFacets(Values: new IrValueSet([new IrValue("a.b", true)], IrValueMatch.Padded, [From]));

        Assert.Equal(["value a.b"], Edges(Row(Text("varchar(9)", facets), "a.b   ")));
        Assert.Single(Row(Text("varchar(9)", facets), "A.B").Violations);
        Assert.Single(Row(Text("varchar(9)", facets), " a.b").Violations);
    }

    [Fact]
    public void A_padded_items_own_trailing_spaces_are_ignored_too()
    {
        var facets = new IrFacets(Values: new IrValueSet([new IrValue("a ", true)], IrValueMatch.Padded, [From]));

        Assert.Empty(Row(Text("varchar(6)", facets), "a").Violations);
        Assert.Empty(Row(Text("varchar(6)", facets), "a   ").Violations);
    }

    // ---- numbers ------------------------------------------------------------------------------------

    private static IrFacets Bounds(string min, bool minInclusive, string max, bool maxInclusive, IrDigits? digits = null) =>
        new(Digits: digits, Min: new IrBound(min, minInclusive, From), Max: new IrBound(max, maxInclusive, From));

    [Theory]
    [InlineData("1", true)]
    [InlineData("5", true)]
    [InlineData("0", false)]
    [InlineData("6", false)]
    public void Inclusive_bounds_are_checked(string value, bool keeps) =>
        Assert.Equal(keeps, Json(Number(IrFormat.Int32, "int", Bounds("1", true, "5", true)), value).Violations.Count == 0);

    [Theory]
    [InlineData("0.0001", true)]
    [InlineData("0.9999", true)]
    [InlineData("0", false)]
    [InlineData("1.0000", false)]
    public void Exclusive_bounds_are_checked(string value, bool keeps) =>
        Assert.Equal(keeps, Json(Number(IrFormat.Decimal, "decimal(9,4)", Bounds("0", false, "1", false, new IrDigits(9, 4, From))), value).Violations.Count == 0);

    [Fact]
    public void Bounds_are_compared_exactly_not_as_doubles()
    {
        // 9223372036854775807 and 9223372036854775806 are the same double.
        var facets = Bounds("0", true, "9223372036854775806", true);

        Assert.Single(Json(Number(IrFormat.Int64, "bigint", facets), "9223372036854775807").Violations);
        Assert.Single(Row(Number(IrFormat.Int64, "bigint", facets), long.MaxValue).Violations);
    }

    [Fact]
    public void An_edge_is_the_bound_or_the_nearest_step_inside_an_exclusive_one()
    {
        var facets = Bounds("0", false, "1", false, new IrDigits(9, 4, From));

        Assert.Equal(["min"], Edges(Json(Number(IrFormat.Decimal, "decimal(9,4)", facets), "0.0001")));
        Assert.Equal(["max"], Edges(Row(Number(IrFormat.Decimal, "decimal(9,4)", facets), 0.9999m)));
        Assert.Empty(Edges(Json(Number(IrFormat.Decimal, "decimal(9,4)", facets), "0.5")));
    }

    [Theory]
    [InlineData("12.50", true)]
    [InlineData("999.99", true)]
    [InlineData("12.505", false)]
    [InlineData("1000", false)]
    public void Digits_are_checked(string value, bool keeps) =>
        Assert.Equal(keeps, Json(Number(IrFormat.Decimal, "decimal(5,2)", new IrFacets(Digits: new IrDigits(5, 2, From))), value).Violations.Count == 0);

    [Fact]
    public void Digits_are_checked_on_a_wide_decimal_in_a_row() =>
        Assert.Single(Row(Number(IrFormat.Decimal, "decimal(38,0)", new IrFacets(Digits: new IrDigits(37, 0, From))), SqlDecimal.Parse("99999999999999999999999999999999999999")).Violations);

    [Fact]
    public void An_exact_set_of_numbers_is_matched_by_value()
    {
        var facets = new IrFacets(Values: new IrValueSet([new IrValue("1.5", false), new IrValue("2", false)], IrValueMatch.Exact, [From]));

        Assert.Equal(["value 1.5"], Edges(Json(Number(IrFormat.Decimal, "decimal(5,2)", facets), "1.50")));
        Assert.Single(Json(Number(IrFormat.Decimal, "decimal(5,2)", facets), "2.5").Violations);
    }

    [Fact]
    public void A_violation_names_the_facet_and_the_value()
    {
        var violation = Assert.Single(Json(Number(IrFormat.Int32, "int", Bounds("1", true, "5", true)), "7").Violations);

        Assert.Equal("results[0][0].v", violation.Path);
        Assert.Equal("at most 5 (int; check dbo.ck)", violation.Claim);
        Assert.Equal("7", violation.Value);
    }
}

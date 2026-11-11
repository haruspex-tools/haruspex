using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>
/// The validator's own tests, with hand-written IR and output, so they run without SQL Server. Each claim the
/// IR makes has a case that keeps it and one that breaks it: a validator that accepts everything would pass
/// every differential test.
/// </summary>
public class IrValidatorTests
{
    private const string JsonColumn = "JSON_F52E2B61-18A1-11d1-B105-00805F49916B";

    private static readonly IrNullReason Because = new(IrNullRules.NullableColumn, "dbo.t.c");

    private static IrField Field(string name, IrType type, IrPresence presence = IrPresence.Always, bool nullable = false) =>
        new(name, type, presence, nullable ? [Because] : []);

    private static IrScalar Scalar(IrScalarKind kind, string? format = null) => new(kind, format, "sql");

    private static IrDocument Doc(IrResult result, params (string Name, IrField[] Fields)[] types) =>
        new(
            new IrSource("tsql"),
            [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [result])],
            types.ToDictionary(t => t.Name, t => new IrObjectType([.. t.Fields])),
            []);

    private static IrResult JsonResult(IrType type, bool mayBeAbsent = true) =>
        new(IrEncoding.Json, type, mayBeAbsent ? [new IrNullReason(IrNullRules.NoRows, "p.sql:1:1")] : []);

    private static IrResult RowsetResult(string row) => new(IrEncoding.Rowset, new IrArray(new IrRef(row)), []);

    private static ObservedResultSet Json(params string[] chunks) => new([JsonColumn], [.. chunks.Select(c => (IReadOnlyList<object?>)[c])]);

    /// <summary>One JSON array result of rows with the given fields, validated against <paramref name="json"/>.</summary>
    private static ValidationReport ValidateRows(string json, params IrField[] fields) =>
        IrValidator.Validate(Doc(JsonResult(new IrArray(new IrRef("Row"))), ("Row", fields)), "dbo.p", [Json(json)]);

    private static Violation Single(ValidationReport report) => Assert.Single(report.Violations);

    // ---- presence and nullability -------------------------------------------------------------------

    [Fact]
    public void A_null_in_a_non_null_field_names_the_procedure_the_path_the_claim_and_the_value()
    {
        var v = Single(ValidateRows("""[{"a":1},{"a":null}]""", Field("a", Scalar(IrScalarKind.Number, IrFormats.Int32))));

        Assert.Equal("dbo.p", v.Procedure);
        Assert.Equal("results[0][1].a", v.Path);
        Assert.Contains("non-null", v.Claim, StringComparison.Ordinal);
        Assert.Equal("null", v.Value);
        Assert.Contains("dbo.p results[0][1].a", v.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_in_a_nullable_field_that_is_always_present_is_allowed() =>
        Assert.Empty(ValidateRows("""[{"a":null}]""", Field("a", Scalar(IrScalarKind.String), nullable: true)).Violations);

    [Fact]
    public void A_missing_key_is_allowed_only_when_it_is_omitted_when_null()
    {
        Assert.Empty(ValidateRows("[{}]", Field("a", Scalar(IrScalarKind.String), IrPresence.OmittedWhenNull, nullable: true)).Violations);

        var v = Single(ValidateRows("[{}]", Field("a", Scalar(IrScalarKind.String), nullable: true)));
        Assert.Equal("results[0][0].a", v.Path);
        Assert.Contains("always present", v.Claim, StringComparison.Ordinal);
        Assert.Equal("(absent)", v.Value);
    }

    [Fact]
    public void A_null_kept_in_a_field_that_is_omitted_when_null_is_a_violation()
    {
        var v = Single(ValidateRows("""[{"a":null}]""", Field("a", Scalar(IrScalarKind.String), IrPresence.OmittedWhenNull, nullable: true)));

        Assert.Contains("omitted when null", v.Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_the_type_does_not_have_is_a_violation()
    {
        var v = Single(ValidateRows("""[{"a":"x","b":1}]""", Field("a", Scalar(IrScalarKind.String))));

        Assert.Equal("results[0][0].b", v.Path);
        Assert.Contains("no key b", v.Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_are_compared_by_case() =>
        Assert.Equal(2, ValidateRows("""[{"A":"x"}]""", Field("a", Scalar(IrScalarKind.String))).Violations.Count);

    [Fact]
    public void A_repeated_key_is_a_violation() =>
        Assert.Contains("once", Single(ValidateRows("""[{"a":"x","a":"y"}]""", Field("a", Scalar(IrScalarKind.String)))).Claim, StringComparison.Ordinal);

    [Fact]
    public void Nested_objects_and_arrays_are_checked_with_their_paths()
    {
        var doc = Doc(
            JsonResult(new IrRef("Row")),
            ("Row", [Field("lines", new IrArray(new IrRef("Line")), nullable: true)]),
            ("Line", [Field("qty", Scalar(IrScalarKind.Number, IrFormats.Int16))]));

        var v = Single(IrValidator.Validate(doc, "dbo.p", [Json("""{"lines":[{"qty":1},{"qty":"2"}]}""")]));

        Assert.Equal("results[0].lines[1].qty", v.Path);
        Assert.Equal("\"2\"", v.Value);
    }

    [Fact]
    public void An_array_where_an_object_is_claimed_is_a_violation()
    {
        var doc = Doc(JsonResult(new IrRef("Row")), ("Row", [Field("a", Scalar(IrScalarKind.Boolean))]));

        Assert.Contains("object", Single(IrValidator.Validate(doc, "dbo.p", [Json("""[{"a":true}]""")])).Claim, StringComparison.Ordinal);
    }

    // ---- scalars in JSON ----------------------------------------------------------------------------

    public static TheoryData<IrScalarKind, string?, string, bool> JsonScalars { get; } = new()
    {
        { IrScalarKind.String, null, "\"AB   \"", true },
        { IrScalarKind.String, null, "1", false },
        { IrScalarKind.Boolean, null, "true", true },
        { IrScalarKind.Boolean, null, "1", false },
        { IrScalarKind.Number, IrFormats.Uint8, "255", true },
        { IrScalarKind.Number, IrFormats.Uint8, "256", false },
        { IrScalarKind.Number, IrFormats.Uint8, "-1", false },
        { IrScalarKind.Number, IrFormats.Int16, "-32768", true },
        { IrScalarKind.Number, IrFormats.Int16, "32768", false },
        { IrScalarKind.Number, IrFormats.Int32, "2147483647", true },
        { IrScalarKind.Number, IrFormats.Int32, "2147483648", false },
        { IrScalarKind.Number, IrFormats.Int32, "1.5", false },
        { IrScalarKind.Number, IrFormats.Int32, "\"1\"", false },
        { IrScalarKind.Number, IrFormats.Int64, "-9223372036854775808", true },
        { IrScalarKind.Number, IrFormats.Int64, "9223372036854775808", false },
        { IrScalarKind.Number, IrFormats.Decimal, "12345678901234567890123456789012345678", true },
        { IrScalarKind.Number, IrFormats.Decimal, "-0.001", true },
        { IrScalarKind.Number, IrFormats.Decimal, "\"1.5\"", false },
        { IrScalarKind.Number, IrFormats.Float, "1.0000000e-001", true },
        { IrScalarKind.Number, IrFormats.Float, "true", false },
        { IrScalarKind.Date, null, "\"0001-01-01\"", true },
        { IrScalarKind.Date, null, "\"2026-02-30\"", false },
        { IrScalarKind.Date, null, "\"2026-03-04T00:00:00\"", false },
        { IrScalarKind.Time, null, "\"05:06:07\"", true },
        { IrScalarKind.Time, null, "\"05:06:07.1234567\"", true },
        { IrScalarKind.Time, null, "\"05:06\"", false },
        { IrScalarKind.Time, null, "\"24:00:00\"", false },
        { IrScalarKind.Datetime, IrFormats.Local, "\"9999-12-31T23:59:59.9999999\"", true },
        { IrScalarKind.Datetime, IrFormats.Local, "\"2026-03-04T05:06:07Z\"", false },
        { IrScalarKind.Datetime, IrFormats.Local, "\"2026-03-04 05:06:07\"", false },
        { IrScalarKind.Datetime, IrFormats.Offset, "\"2026-03-04T05:06:07Z\"", true },
        { IrScalarKind.Datetime, IrFormats.Offset, "\"2026-03-04T05:06:07.500-08:00\"", true },
        { IrScalarKind.Datetime, IrFormats.Offset, "\"2026-03-04T05:06:07\"", false },
        { IrScalarKind.Datetime, IrFormats.Offset, "\"2026-03-04T05:06:07+0530\"", false },
        { IrScalarKind.Uuid, null, "\"6F9619FF-8B86-D011-B42D-00C04FC964FF\"", true },
        { IrScalarKind.Uuid, null, "\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"", true },
        { IrScalarKind.Uuid, null, "\"6F9619FF8B86D011B42D00C04FC964FF\"", false },
    };

    [Theory]
    [MemberData(nameof(JsonScalars))]
    public void A_json_scalar_must_have_the_form_its_kind_claims(IrScalarKind kind, string? format, string json, bool valid)
    {
        var report = ValidateRows($$"""[{"a":{{json}}}]""", Field("a", Scalar(kind, format)));

        if (valid)
            Assert.Empty(report.Violations);
        else
            Assert.Equal(json, Single(report).Value);
    }

    [Fact]
    public void Json_text_in_json_is_a_string_that_parses_as_json()
    {
        Assert.Empty(ValidateRows("""[{"a":"[1,2]"}]""", Field("a", new IrJsonText())).Violations);
        Single(ValidateRows("""[{"a":"[1,"}]""", Field("a", new IrJsonText())));
    }

    [Fact]
    public void An_unknown_type_accepts_any_value_but_presence_and_null_are_still_claimed()
    {
        var unknown = new IrUnknownType(IrUnknownReasons.UnsupportedExpression);

        Assert.Empty(ValidateRows("""[{"a":{"x":[1]}},{"a":"y"},{"a":null}]""", Field("a", unknown, nullable: true)).Violations);
        Assert.Empty(ValidateRows("""[{"a":1},{}]""", Field("a", unknown, IrPresence.OmittedWhenNull, nullable: true)).Violations);
        Single(ValidateRows("""[{"a":null}]""", Field("a", unknown, IrPresence.OmittedWhenNull, nullable: true)));
    }

    // ---- results ------------------------------------------------------------------------------------

    [Fact]
    public void A_json_result_split_over_several_rows_is_joined_before_parsing() =>
        Assert.Empty(IrValidator.Validate(
            Doc(JsonResult(new IrArray(new IrRef("Row"))), ("Row", [Field("a", Scalar(IrScalarKind.Number, IrFormats.Int32))])),
            "dbo.p",
            [Json("""[{"a":""", """1},{"a":2}]""")]).Violations);

    [Fact]
    public void A_json_result_with_no_rows_is_absent_which_needs_the_no_rows_reason()
    {
        var row = ("Row", new[] { Field("a", Scalar(IrScalarKind.String)) });

        var allowed = IrValidator.Validate(Doc(JsonResult(new IrArray(new IrRef("Row"))), row), "dbo.p", [Json()]);
        Assert.Empty(allowed.Violations);
        Assert.Contains(("results[0]", Seen.Absent), allowed.Seen);

        var v = Single(IrValidator.Validate(Doc(JsonResult(new IrArray(new IrRef("Row")), mayBeAbsent: false), row), "dbo.p", [Json()]));
        Assert.Equal("results[0]", v.Path);
        Assert.Contains("has a row whenever it runs", v.Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_that_does_not_parse_is_a_violation() =>
        Assert.Contains("JSON", Single(ValidateRows("""{"a":1},{"a":2}""", Field("a", Scalar(IrScalarKind.Number, IrFormats.Int32)))).Claim, StringComparison.Ordinal);

    [Fact]
    public void A_json_result_must_be_one_column() =>
        Single(IrValidator.Validate(
            Doc(JsonResult(new IrArray(new IrRef("Row"))), ("Row", [])),
            "dbo.p",
            [new ObservedResultSet(["a", "b"], [])]));

    [Fact]
    public void The_number_of_result_sets_must_match()
    {
        var doc = Doc(JsonResult(new IrArray(new IrRef("Row"))), ("Row", []));

        var v = Single(IrValidator.Validate(doc, "dbo.p", [Json("[{}]"), Json("[{}]")]));
        Assert.Equal("results", v.Path);
        Assert.Contains("1 result set", v.Claim, StringComparison.Ordinal);
        Assert.Equal("2", v.Value);
    }

    private static readonly IrNullReason NotRun = new(IrNullRules.NotRun, "p.sql:2:1");

    private static IrDocument Results(params IrResult[] results) =>
        new(new IrSource("tsql"), [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [.. results])], new Dictionary<string, IrObjectType> { ["Row"] = new([]) }, []);

    [Fact]
    public void Trailing_results_that_may_not_run_may_be_missing_and_are_then_absent()
    {
        var doc = Results(RowsetResult("Row"), new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Row")), [NotRun]));

        var report = IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet([], [])]);
        Assert.Empty(report.Violations);
        Assert.Contains(("results[1]", Seen.Absent), report.Seen);
        Assert.Empty(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet([], []), new ObservedResultSet([], [])]).Violations);
    }

    [Fact]
    public void A_result_that_always_runs_may_not_be_missing()
    {
        var doc = Results(RowsetResult("Row"), new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Row")), [NotRun]));

        var v = Single(IrValidator.Validate(doc, "dbo.p", []));
        Assert.Contains("1 to 2 result set", v.Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_result_that_may_not_run_but_matches_rows_whenever_it_runs_has_a_row()
    {
        var doc = Results(new IrResult(IrEncoding.Json, new IrArray(new IrRef("Row")), [NotRun]));

        Assert.Single(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet([JsonColumn], [])]).Violations);
        Assert.Empty(IrValidator.Validate(doc, "dbo.p", []).Violations);
    }

    [Fact]
    public void A_rowset_several_statements_may_return_fits_one_of_their_rows_whole()
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrUnion([new IrArray(new IrRef("A")), new IrArray(new IrRef("B"))]), [])])],
            new Dictionary<string, IrObjectType>
            {
                ["A"] = new([Field("a", Scalar(IrScalarKind.String))]),
                ["B"] = new([Field("b", Scalar(IrScalarKind.String))]),
            },
            []);

        Assert.Empty(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["a"], [["x"]])]).Violations);
        Assert.Empty(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["b"], [["y"]])]).Violations);
        Assert.NotEmpty(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["b"], [[null]])]).Violations);
        Assert.NotEmpty(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["c"], [["z"]])]).Violations);
    }

    [Fact]
    public void Results_that_cannot_be_listed_accept_anything()
    {
        var doc = new IrDocument(new IrSource("tsql"), [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], null)], new Dictionary<string, IrObjectType>(), []);

        Assert.Empty(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["x"], [[1]]), Json("[")]).Violations);
    }

    // ---- rowsets ------------------------------------------------------------------------------------

    private static ValidationReport ValidateRowset(IReadOnlyList<string> columns, IReadOnlyList<object?> row, params IrField[] fields) =>
        IrValidator.Validate(Doc(RowsetResult("Row"), ("Row", fields)), "dbo.p", [new ObservedResultSet(columns, [row])]);

    [Fact]
    public void A_rowset_has_exactly_the_claimed_columns()
    {
        var fields = new[] { Field("a", Scalar(IrScalarKind.String)), Field("b", Scalar(IrScalarKind.String)) };

        Assert.Empty(ValidateRowset(["a", "b"], ["x", "y"], fields).Violations);
        Assert.Equal("results[0][0].b", Single(ValidateRowset(["a"], ["x"], fields)).Path);
        Assert.Equal("results[0][0].c", Assert.Single(ValidateRowset(["a", "b", "c"], ["x", "y", "z"], fields).Violations).Path);
    }

    [Fact]
    public void A_null_in_a_non_null_column_is_a_violation() =>
        Assert.Equal("null", Single(ValidateRowset(["a"], [null], Field("a", Scalar(IrScalarKind.String)))).Value);

    public static TheoryData<IrScalarKind, string?, object, bool> RowsetScalars { get; } = new()
    {
        { IrScalarKind.String, null, "x", true },
        { IrScalarKind.String, null, 1, false },
        { IrScalarKind.Number, IrFormats.Uint8, (byte)1, true },
        { IrScalarKind.Number, IrFormats.Int16, (short)1, true },
        { IrScalarKind.Number, IrFormats.Int32, 1, true },
        { IrScalarKind.Number, IrFormats.Int32, 1L, false },
        { IrScalarKind.Number, IrFormats.Int64, 1L, true },
        { IrScalarKind.Number, IrFormats.Decimal, 1.5m, true },
        { IrScalarKind.Number, IrFormats.Decimal, 1.5d, false },
        { IrScalarKind.Number, IrFormats.Float, 1.5d, true },
        { IrScalarKind.Number, IrFormats.Float, 1.5f, true },
        { IrScalarKind.Boolean, null, true, true },
        { IrScalarKind.Date, null, new DateTime(2026, 3, 4), true },
        { IrScalarKind.Date, null, new DateTime(2026, 3, 4, 5, 6, 7), false },
        { IrScalarKind.Time, null, new TimeSpan(5, 6, 7), true },
        { IrScalarKind.Datetime, IrFormats.Local, new DateTime(2026, 3, 4, 5, 6, 7), true },
        { IrScalarKind.Datetime, IrFormats.Local, new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), false },
        { IrScalarKind.Datetime, IrFormats.Offset, new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), true },
        { IrScalarKind.Uuid, null, Guid.Empty, true },
        { IrScalarKind.Uuid, null, "00000000-0000-0000-0000-000000000000", false },
    };

    [Theory]
    [MemberData(nameof(RowsetScalars))]
    public void A_rowset_value_must_have_the_type_its_kind_claims(IrScalarKind kind, string? format, object value, bool valid)
    {
        var report = ValidateRowset(["a"], [value], Field("a", Scalar(kind, format)));

        if (valid)
            Assert.Empty(report.Violations);
        else
            Single(report);
    }

    [Fact]
    public void Json_text_in_a_rowset_is_a_string_that_parses_as_json()
    {
        Assert.Empty(ValidateRowset(["a"], ["""{"x":1}"""], Field("a", new IrJsonText())).Violations);
        Single(ValidateRowset(["a"], ["{"], Field("a", new IrJsonText())));
    }

    // ---- what was seen ------------------------------------------------------------------------------

    [Fact]
    public void A_nested_object_left_out_counts_as_null_for_each_of_its_fields()
    {
        // A left join's columns are null together, so the object is left out rather than any one key in it:
        // the data made each of them null, which is what the seeds must show.
        var doc = Doc(
            JsonResult(new IrRef("Row")),
            ("Row", [Field("sp", new IrRef("Sp"), IrPresence.OmittedWhenNull, nullable: true)]),
            ("Sp", [Field("id", Scalar(IrScalarKind.Number, IrFormats.Int32), IrPresence.OmittedWhenNull, nullable: true)]));

        var report = IrValidator.Validate(doc, "dbo.p", [Json("{}")]);

        Assert.Empty(report.Violations);
        Assert.Contains(("results[0].sp", Seen.Null), report.Seen);
        Assert.Contains(("results[0].sp.id", Seen.Null), report.Seen);
    }

    [Fact]
    public void The_report_records_which_paths_were_seen_null_absent_or_with_a_value()
    {
        var report = ValidateRows(
            """[{"a":1,"b":null},{"a":2,"b":"x"}]""",
            Field("a", Scalar(IrScalarKind.Number, IrFormats.Int32)),
            Field("b", Scalar(IrScalarKind.String), nullable: true),
            Field("c", Scalar(IrScalarKind.String), IrPresence.OmittedWhenNull, nullable: true));

        Assert.Empty(report.Violations);
        Assert.Equal(
            [("results[0]", Seen.Value), ("results[0][].a", Seen.Value), ("results[0][].b", Seen.Null), ("results[0][].b", Seen.Value), ("results[0][].c", Seen.Null)],
            report.Seen.Order());
    }
}

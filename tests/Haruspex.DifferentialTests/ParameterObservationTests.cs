using System.Text.Json;
using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server keeps of a value sent to a procedure's parameter through the mssql driver (the version
/// <c>tests/emit-check</c> pins, 12.7.14), for each type: where it cuts a value short or rounds it without an error, where the
/// driver or the server refuses it, and how a date and time arrives. A parameter's facts in the IR, and the checks a client
/// makes of a value before sending it, rest on these (decisions 0054 and 0055); <c>ParameterAcceptanceTests</c> holds the
/// generated schemas to the server value by value.
/// </summary>
public class ParameterObservationTests(SqlServerFixture server)
{
    /// <summary>
    /// What <c>@v</c> of type <paramref name="declared"/> held when <paramref name="value"/> was sent to it, as text: a date and
    /// time in ISO 8601 (style 126), money with its four places (style 2), anything else as <c>CONVERT</c> writes it.
    /// </summary>
    /// <param name="setup">A batch run first in the call's session, such as a <c>SET</c> the conversion reads.</param>
    /// <returns>The text, or the driver's error.</returns>
    private async Task<(string? Kept, MssqlDriver.Error? Error)> Sent(
        string declared, object? value, string? timeZone = null, string setup = "", int style = -1)
    {
        var text = style < 0 ? "CONVERT(nvarchar(100), @v)" : $"CONVERT(nvarchar(100), @v, {style})";
        var calls = await MssqlDriver.RunAsync(
            server,
            null,
            [$"CREATE PROCEDURE dbo.obs_parameter @v {declared} AS SELECT {text} AS v;", .. string.IsNullOrEmpty(setup) ? Array.Empty<string>() : [setup]],
            [new("sent", ParseJson: false, Procedure: "dbo.obs_parameter", Parameters: new Dictionary<string, object?> { ["v"] = value }, TimeZone: timeZone)],
            TestContext.Current.CancellationToken);
        var response = Assert.Single(calls);
        if (response.Error is { } error)
            return (null, error);
        var row = Assert.Single(Assert.Single(response.Recordsets).Rows);
        return (row.GetProperty("v").ValueKind == JsonValueKind.Null ? null : row.GetProperty("v").GetString(), null);
    }

    /// <summary>Text sent as <c>nvarchar(max)</c>, for the server to convert to the parameter's type.</summary>
    private static MssqlDriver.Typed Max(string text) => new("NVarChar", text, ["MAX"]);

    private async Task<string?> Kept(string declared, object? value, int style = -1)
    {
        var (kept, error) = await Sent(declared, value, style: style);
        Assert.Null(error);
        return kept;
    }

    // ---- strings ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("nvarchar(5)")]
    [InlineData("varchar(5)")]
    [InlineData("nchar(5)")]
    public async Task A_string_too_long_is_cut_short_without_an_error_when_sent_as_max(string declared) =>

        // SQL Server converts the argument to the parameter's type as an assignment does, which cuts a string short.
        Assert.Equal("abcde", await Kept(declared, Max("abcdefg")));

    [Theory]
    [InlineData("NVarChar", "nvarchar(5)")]
    [InlineData("VarChar", "varchar(5)")]
    public async Task A_string_too_long_for_its_declared_length_is_refused_as_a_broken_stream(string type, string declared)
    {
        // Sent with the type declared, the driver writes a longer value than the length it declares, which the server
        // rejects with an error that names neither the procedure nor the parameter's name.
        var (_, error) = await Sent(declared, new MssqlDriver.Typed(type, "abcdefg", [5]));

        Assert.Equal(8016, error?.Number);
    }

    [Fact]
    public async Task Nvarchar_n_holds_n_utf16_code_units_whatever_the_characters() =>

        // A character outside the BMP is two code units, as JavaScript's length counts it.
        Assert.Equal("abc\U0001F600", await Kept("nvarchar(5)", new MssqlDriver.Typed("NVarChar", "abc\U0001F600", [5])));

    [Theory]
    [InlineData("NVarChar", "l\u00F3dz")]
    [InlineData("VarChar", "?\u00F3d?")]
    public async Task A_varchar_parameter_replaces_characters_its_code_page_lacks_without_an_error(string type, string kept) =>

        // SQL_Latin1_General_CP1_CI_AS is code page 1252, which has U+00F3 but not U+0142 or U+017A: the server maps those to
        // the nearest letter it has, and the driver's own conversion to ?.
        Assert.Equal(kept, await Kept("varchar(10)", new MssqlDriver.Typed(type, "\u0142\u00F3d\u017A", [10])));

    [Fact]
    public async Task A_shorter_value_is_padded_to_a_fixed_length() =>
        Assert.Equal("ab   ", await Kept("nchar(5)", new MssqlDriver.Typed("NChar", "ab", [5])));

    // ---- numbers ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Decimal", 1.234, "1.23")]
    [InlineData("Decimal", 1.235, "1.24")]
    [InlineData("NVarChar", "1.235", "1.24")]
    public async Task A_decimal_of_more_places_is_rounded_without_an_error(string type, object value, string kept) =>
        Assert.Equal(kept, await Kept("decimal(5,2)", type == "Decimal" ? new MssqlDriver.Typed(type, value, [5, 2]) : Max((string)value)));

    [Theory]
    [InlineData(1000.0)]
    [InlineData(999.995)]
    public async Task A_decimal_past_its_precision_is_refused(double value)
    {
        var (_, error) = await Sent("decimal(5,2)", new MssqlDriver.Typed("Decimal", value, [5, 2]));

        Assert.Equal(8023, error?.Number);
    }

    [Theory]
    [InlineData("Money", "1.2345")]
    [InlineData("Decimal", "1.2346")]
    public async Task Money_of_more_places_loses_them_without_an_error(string type, string kept) =>

        // The driver's money cuts the fifth place off; a decimal sent to a money parameter is rounded by the server.
        Assert.Equal(kept, await Kept("money", new MssqlDriver.Typed(type, 1.23456, type == "Decimal" ? [19, 4] : null), style: 2));

    [Fact]
    public async Task An_integer_parameter_drops_a_fraction_without_an_error() =>
        Assert.Equal("1", await Kept("int", new MssqlDriver.Typed("Int", 1.5)));

    [Theory]
    [InlineData("int", "Int", 2147483648.0)]
    [InlineData("tinyint", "TinyInt", -1.0)]
    [InlineData("smallint", "SmallInt", 40000.0)]
    public async Task An_integer_out_of_its_type_s_range_is_refused_by_the_driver(string declared, string type, double value)
    {
        var (_, error) = await Sent(declared, new MssqlDriver.Typed(type, value));

        Assert.Equal("EPARAM", error?.Code);
    }

    [Fact]
    public async Task A_bigint_sent_as_a_number_past_2_to_the_53_is_already_rounded()
    {
        // 2^53 + 1 is no double: JavaScript holds it as 2^53 before the driver sees it. A string or a BigInt keeps it.
        Assert.Equal("9007199254740992", await Kept("bigint", new MssqlDriver.Typed("BigInt", 9007199254740993.0)));
        Assert.Equal("9223372036854775807", await Kept("bigint", new MssqlDriver.Typed("BigInt", "9223372036854775807")));
    }

    [Fact]
    public async Task Not_a_number_is_sent_as_null() =>
        Assert.Null(await Kept("float", new MssqlDriver.Typed("Float", new MssqlDriver.JsValue("NaN"))));

    [Fact]
    public async Task A_real_past_its_range_is_refused()
    {
        var (_, error) = await Sent("real", new MssqlDriver.Typed("Float", 1e39));

        Assert.Equal(8114, error?.Number);
    }

    [Fact]
    public async Task A_bit_is_1_for_any_number_but_0() =>
        Assert.Equal("1", await Kept("bit", new MssqlDriver.Typed("Bit", 2)));

    // ---- dates and times ----------------------------------------------------------------------------

    [Theory]
    [InlineData("date", "2026-11-16", "2026-11-16")]
    [InlineData("date", "0001-01-01", "0001-01-01")]
    [InlineData("time(7)", "23:59:59.9999999", "23:59:59.9999999")]
    [InlineData("datetime2(7)", "2026-11-16T10:20:30.1234567", "2026-11-16T10:20:30.1234567")]
    [InlineData("datetime2(0)", "9999-12-31T23:59:59", "9999-12-31T23:59:59")]
    [InlineData("datetimeoffset(7)", "2026-11-16T10:20:30.1234567+05:30", "2026-11-16T10:20:30.1234567+05:30")]
    [InlineData("datetime", "1753-01-01T00:00:00", "1753-01-01T00:00:00")]
    [InlineData("datetime", "9999-12-31T23:59:59.997", "9999-12-31T23:59:59.997")]
    [InlineData("datetime", "2026-11-16T10:20:30.003", "2026-11-16T10:20:30.003")]
    [InlineData("smalldatetime", "2079-06-06T23:59:00", "2079-06-06T23:59:00")]
    public async Task An_iso_8601_string_converts_exactly_to_each_date_and_time_type(string declared, string sent, string kept) =>
        Assert.Equal(kept, await Kept(declared, Max(sent), style: 126));

    [Theory]
    [InlineData("datetime2(0)", "2026-11-16T10:20:30.6", "2026-11-16T10:20:31")]
    [InlineData("datetime2(7)", "2026-11-16T10:20:30.12345678", "2026-11-16T10:20:30.1234568")]
    [InlineData("time(0)", "10:20:30.6", "10:20:31")]
    [InlineData("datetimeoffset(0)", "2026-11-16T10:20:30.5+05:00", "2026-11-16T10:20:31+05:00")]
    [InlineData("datetime", "2026-11-16T10:20:30.002", "2026-11-16T10:20:30.003")]
    [InlineData("datetime", "2026-11-16T10:20:30.999", "2026-11-16T10:20:31")]
    [InlineData("smalldatetime", "2026-11-16T10:20:29.998", "2026-11-16T10:20:00")]
    [InlineData("smalldatetime", "2026-11-16T10:20:29.999", "2026-11-16T10:21:00")]
    public async Task A_fraction_finer_than_the_type_holds_is_rounded_without_an_error(string declared, string sent, string kept) =>

        // datetime holds 1/300 of a second (.000, .003, .007), smalldatetime a minute, the others their scale's digits.
        Assert.Equal(kept, await Kept(declared, Max(sent), style: 126));

    [Theory]
    [InlineData("datetime", "1752-12-31T23:59:59")]
    [InlineData("smalldatetime", "1899-12-31T23:59:00")]
    [InlineData("smalldatetime", "2079-06-06T23:59:30")]
    [InlineData("date", "2026-02-30")]
    public async Task A_date_outside_its_type_s_range_is_refused(string declared, string sent)
    {
        var (_, error) = await Sent(declared, Max(sent));

        Assert.Equal(8114, error?.Number);
    }

    [Theory]
    [InlineData("datetime2(0)", "2026-11-16T10:20:30+05:00", "2026-11-16T10:20:30")]
    [InlineData("datetime2(0)", "2026-11-16T10:20:30Z", "2026-11-16T10:20:30")]
    [InlineData("datetimeoffset(0)", "2026-11-16T10:20:30", "2026-11-16T10:20:30+00:00")]
    public async Task An_offset_is_dropped_by_a_type_without_one_and_taken_as_zero_by_one_with_it(string declared, string sent, string kept) =>
        Assert.Equal(kept, await Kept(declared, Max(sent), style: 126));

    [Theory]
    [InlineData("datetime", "2026-02-03", "2026-03-02T00:00:00")]
    [InlineData("datetime", "2026-02-03T00:00:00", "2026-02-03T00:00:00")]
    [InlineData("date", "2026-02-03", "2026-02-03")]
    public async Task A_datetime_without_its_time_is_read_in_the_session_s_date_order(string declared, string sent, string kept)
    {
        // Under SET LANGUAGE british (dmy), yyyy-mm-dd is read as yyyy-dd-mm by datetime and smalldatetime only; the form
        // with a T is read the same in every language, and date, datetime2 and datetimeoffset read either form so. The argument
        // is converted in the caller's session, before the body runs, so it is the session's language that counts.
        var (got, error) = await Sent(declared, Max(sent), setup: "SET LANGUAGE british;", style: 126);

        Assert.Null(error);
        Assert.Equal(kept, got);
    }

    [Theory]
    [InlineData("Date", "2026-11-16", "2026-11-16")]
    [InlineData("DateTime2", "2026-11-16T10:20:30", "2026-11-16T15:20:30")]
    [InlineData("DateTime", "2026-11-16T10:20:30", "2026-11-16T15:20:30")]
    public async Task A_string_given_to_the_driver_s_date_types_is_read_in_the_process_s_time_zone(string type, string sent, string kept)
    {
        // The driver parses the string as a JavaScript Date, in New York's time, and sends it in UTC (its useUTC default):
        // the clock time the application wrote is not the one stored.
        var (got, error) = await Sent(type == "Date" ? "date" : "datetime2(0)", new MssqlDriver.Typed(type, sent), timeZone: "America/New_York", style: 126);

        Assert.Null(error);
        Assert.Equal(kept, got);
    }

    [Fact]
    public async Task The_driver_s_datetime2_keeps_only_milliseconds_of_a_string() =>
        Assert.Equal(
            "2026-11-16T10:20:30.1230000",
            await Kept("datetime2(7)", new MssqlDriver.Typed("DateTime2", "2026-11-16T10:20:30.1234567Z", [7]), style: 126));

    // ---- names --------------------------------------------------------------------------------------

    [Fact]
    public async Task A_parameter_without_a_default_must_be_sent()
    {
        var calls = await MssqlDriver.RunAsync(
            server,
            null,
            ["CREATE PROCEDURE dbo.obs_parameter @v int AS SELECT @v AS v;"],
            [new("missing", ParseJson: false, Procedure: "dbo.obs_parameter")],
            TestContext.Current.CancellationToken);

        Assert.Equal(201, Assert.Single(calls).Error?.Number);
    }

    [Fact]
    public async Task A_parameter_name_the_procedure_does_not_have_is_refused()
    {
        var calls = await MssqlDriver.RunAsync(
            server,
            null,
            ["CREATE PROCEDURE dbo.obs_parameter @v int = 0 AS SELECT @v AS v;"],
            [new("misspelt", ParseJson: false, Procedure: "dbo.obs_parameter", Parameters: new Dictionary<string, object?> { ["vv"] = 1 })],
            TestContext.Current.CancellationToken);

        Assert.Equal(8145, Assert.Single(calls).Error?.Number);
    }
}

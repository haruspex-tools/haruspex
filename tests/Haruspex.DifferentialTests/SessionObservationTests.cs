using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What the options a caller's session sets change in what a procedure returns, observed under each combination, so that a
/// declared session (<c>docs/decisions/0038-the-callers-session-is-declared.md</c>) rests on what the server does rather than
/// on its documentation. An overflow, a division by zero, a COUNT past int and a value outside a math function's domain are
/// NULL only when ANSI_WARNINGS and ARITHABORT are both off; either on makes each an error, which fails the call. The options
/// the decision leaves out are observed here too, for the reason each changes no claim. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class SessionObservationTests(SqlServerFixture server)
{
    /// <summary>
    /// The four combinations, ANSI_WARNINGS then ARITHABORT, each set in that order: ANSI_WARNINGS ON also sets ARITHABORT ON
    /// (at compatibility level 90 and above), so ARITHABORT is set after it to be off with it on.
    /// </summary>
    private static readonly (string AnsiWarnings, string ArithAbort)[] Combinations = [("ON", "ON"), ("ON", "OFF"), ("OFF", "ON"), ("OFF", "OFF")];

    private Task<CallOutcome> Run(string[] setup, string call) => ObservedCall.Run(server, setup, call);

    /// <summary>
    /// The one value of <c>v</c> a batch gives, or the error that fails it, under each combination in
    /// <see cref="Combinations"/>' order: <c>8115 8115 8115 null</c>.
    /// </summary>
    private async Task<string> UnderEach(string[] setup, string batch)
    {
        var outcomes = new List<string>();
        foreach (var (ansiWarnings, arithAbort) in Combinations)
        {
            var outcome = await Run(setup, $"SET ANSI_WARNINGS {ansiWarnings}; SET ARITHABORT {arithAbort}; {batch}");
            outcomes.Add(outcome.Error is { } error ? error.ToString(System.Globalization.CultureInfo.InvariantCulture) : outcome.Sets[0]["v: ".Length..]);
        }

        return string.Join(" ", outcomes);
    }

    [Fact]
    public async Task Each_combination_is_what_the_session_then_reports()
    {
        // The order of the two SETs matters: ANSI_WARNINGS ON turns ARITHABORT on, and a later SET turns it off again.
        Assert.Equal(
            "1,1 1,0 0,1 0,0",
            await UnderEach([], "SELECT CONCAT(CAST(SESSIONPROPERTY('ANSI_WARNINGS') AS int), ',', CAST(SESSIONPROPERTY('ARITHABORT') AS int)) AS v;"));
    }

    [Fact]
    public async Task A_sqlclient_connection_sets_these_options_at_login()
    {
        // What the differential tests' own driver sets before any SET of theirs, which the meters fixture declares. Read from
        // the session, since SqlClient's documentation doesn't list them (decision 0038 lists the drivers' published ones).
        var outcome = await Run([], """
            SELECT CONCAT('ANSI_WARNINGS ', CAST(SESSIONPROPERTY('ANSI_WARNINGS') AS int), ', ARITHABORT ', CAST(SESSIONPROPERTY('ARITHABORT') AS int),
                ', CONCAT_NULL_YIELDS_NULL ', CAST(SESSIONPROPERTY('CONCAT_NULL_YIELDS_NULL') AS int), ', NUMERIC_ROUNDABORT ', CAST(SESSIONPROPERTY('NUMERIC_ROUNDABORT') AS int)) AS v;
            """);

        Assert.Equal("v: ANSI_WARNINGS 1, ARITHABORT 0, CONCAT_NULL_YIELDS_NULL 1, NUMERIC_ROUNDABORT 0", outcome.ToString());
    }

    public static TheoryData<string, string, string> Overflows { get; } = new()
    {
        { "int +", "DECLARE @i int = 2147483647; SELECT @i + 1 AS v;", "8115 8115 8115 null" },
        { "int *", "DECLARE @i int = 2147483647; SELECT @i * 2 AS v;", "8115 8115 8115 null" },
        { "a division by zero", "DECLARE @z int = 0; SELECT 1 / @z AS v;", "8134 8134 8134 null" },
        { "CAST", "DECLARE @i int = 300; SELECT CAST(@i AS tinyint) AS v;", "220 220 220 null" },
        { "an assignment", "DECLARE @i int = 300, @t tinyint; SET @t = @i; SELECT @t AS v;", "220 220 220 null" },
        { "SUM", "SELECT SUM(x) AS v FROM (VALUES (2147483647), (1)) AS t(x);", "8115 8115 8115 null" },
        { "AVG", "SELECT AVG(x) AS v FROM (VALUES (2147483647), (1)) AS t(x);", "8115 8115 8115 null" },
        { "a date function given an int", "DECLARE @i int = 2147483647; SELECT YEAR(@i) AS v;", "8115 8115 8115 null" },
        { "a window function", "SELECT SUM(x) OVER () AS v FROM (VALUES (2147483647), (1)) AS t(x);", "8115 8115 8115 null;null" },
    };

    [Theory]
    [MemberData(nameof(Overflows))]
    public async Task An_overflow_is_null_only_when_ansi_warnings_and_arithabort_are_both_off(string construct, string batch, string expected)
    {
        _ = construct;
        Assert.Equal(expected, await UnderEach([], batch));
    }

    [Fact]
    public async Task Count_over_more_rows_than_int_holds_is_an_error_unless_ansi_warnings_and_arithabort_are_both_off()
    {
        // 216^4 = 2,176,782,336 rows, counted without being built. COUNT_BIG never overflows, so only COUNT's value is read.
        string[] setup = ["""
            CREATE PROCEDURE dbo.obs_session_count AS
            WITH d AS (SELECT TOP (216) 1 AS x FROM sys.all_columns)
            SELECT COUNT(*) AS v FROM d AS a CROSS JOIN d AS b CROSS JOIN d AS c CROSS JOIN d AS e;
            """];

        Assert.Equal("8115 8115 8115 null", await UnderEach(setup, "EXEC dbo.obs_session_count;"));
    }

    [Fact]
    public async Task A_value_outside_a_math_functions_domain_is_null_only_when_both_are_off() =>
        // An invalid floating point operation (3623), as for an overflow.
        Assert.Equal("3623 3623 3623 null", await UnderEach([], "DECLARE @f float = -1; SELECT SQRT(@f) AS v;"));

    [Fact]
    public async Task A_procedure_runs_under_its_callers_options_not_those_of_whoever_created_it()
    {
        // Unlike ANSI_NULLS and QUOTED_IDENTIFIER, which a module keeps from its creation, these are the session's when it runs,
        // which is why they are declared for the callers and not read from the source.
        // Created with both off: were the options the creator's, every call would give NULL.
        string[] setup = ["SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;", "CREATE PROCEDURE dbo.obs_session_plus AS DECLARE @i int = 2147483647; SELECT @i + 1 AS v;"];

        Assert.Equal("8115 8115 8115 null", await UnderEach(setup, "EXEC dbo.obs_session_plus;"));
    }

    [Fact]
    public async Task Numeric_roundabort_on_makes_a_loss_of_precision_null_when_ansi_warnings_and_arithabort_are_both_off()
    {
        // A decimal rounded to fewer places is rounded silently with NUMERIC_ROUNDABORT off (tedious documents off as its
        // default; SqlClient connects so, A_sqlclient_connection_sets_these_options_at_login). On, it
        // is an error (8115), or NULL under both options off. haruspex does not model it: decision 0038 says why and what
        // follows. A float or money converted to a decimal of fewer places, and a decimal to int (which truncates), are not
        // a loss of precision here.
        const string Batch = """
            SET NUMERIC_ROUNDABORT ON;
            DECLARE @d decimal(10,4) = 1.2345, @f float = 1.23456;
            SELECT CONCAT(ISNULL(CAST(CAST(@d AS decimal(10,2)) AS varchar(20)), 'null'), ',', CAST(@f AS decimal(10,2)), ',', CAST(@d AS int)) AS v;
            SET NUMERIC_ROUNDABORT OFF;
            """;

        Assert.Equal("8115 8115 8115 null,1.23,1", await UnderEach([], Batch));
    }

    [Fact]
    public async Task Datefirst_and_language_change_names_and_numbering_but_no_claim_haruspex_makes()
    {
        // DATEPART(weekday) is 1 to 7 under any DATEFIRST; DATENAME's names follow the language, which is why it is never a
        // closed set; a CONVERT style with month names follows it too. So none is declared (decision 0038).
        var outcome = await Run([], """
            DECLARE @d datetime2 = '2026-02-14T10:00:00';
            SET LANGUAGE us_english;
            SELECT DATEPART(weekday, @d) AS a, DATENAME(month, @d) AS b, CONVERT(varchar(30), @d, 100) AS c;
            SET LANGUAGE French;
            SELECT DATEPART(weekday, @d) AS a, DATENAME(month, @d) AS b, CONVERT(varchar(30), @d, 100) AS c;
            SET LANGUAGE us_english; SET DATEFIRST 3;
            SELECT DATEPART(weekday, @d) AS a;
            SET DATEFIRST 7;
            """);

        Assert.Equal("a,b,c: 7,February,Feb 14 2026 10:00AM | a,b,c: 6,février,févr 14 2026 10:00AM | a: 4", outcome.ToString());
    }

    [Fact]
    public async Task Dateformat_decides_which_strings_are_dates_and_a_cast_fails_or_overflows_as_for_any_string()
    {
        // Which strings are dates depends on DATEFORMAT, and under TRY_CAST one that is not is NULL, which is nullable anyway.
        // Under CAST it is what any string that names no date is, below: so DATEFORMAT changes which values fail, never a type
        // or a reason to be null.
        var outcome = await Run([], """
            SET DATEFORMAT dmy; SELECT TRY_CAST('13/02/2026' AS date) AS a, TRY_CAST('02/13/2026' AS date) AS b;
            SET DATEFORMAT mdy; SELECT TRY_CAST('13/02/2026' AS date) AS a, TRY_CAST('02/13/2026' AS date) AS b;
            """);

        Assert.Equal("a,b: 02/13/2026 00:00:00,null | a,b: null,02/13/2026 00:00:00", outcome.ToString());
        Assert.Equal("242 242 242 null", await UnderEach([], "SET DATEFORMAT dmy; SELECT CAST('02/13/2026' AS datetime) AS v;"));
        Assert.Equal("241 241 241 241", await UnderEach([], "SET DATEFORMAT dmy; SELECT CAST('02/13/2026' AS date) AS v;"));
    }

    public static TheoryData<string, string, string> StringsToDates { get; } = new()
    {
        { "no date at all, to datetime", "SELECT CAST('abc' AS datetime) AS v;", "241 241 241 241" },
        { "a day that does not exist, to datetime", "SELECT CAST('2026-02-30' AS datetime) AS v;", "242 242 242 null" },
        { "an hour that does not exist, to smalldatetime", "SELECT CAST('2026-01-01T25:00' AS smalldatetime) AS v;", "242 242 242 null" },
        { "a year before datetime's range", "SELECT CAST('1700-01-01' AS datetime) AS v;", "242 242 242 null" },
        { "a day that does not exist, to date", "SELECT CAST('2026-02-30' AS date) AS v;", "241 241 241 241" },
        { "a day that does not exist, to datetime2", "SELECT CAST('2026-02-30' AS datetime2) AS v;", "241 241 241 241" },
    };

    [Theory]
    [MemberData(nameof(StringsToDates))]
    public async Task A_string_that_names_no_date_or_a_date_out_of_range_fails_or_overflows_by_the_target_type(string construct, string batch, string expected)
    {
        // A string that is no date fails (241) whatever the options. One that is a date out of the target's range, or that
        // doesn't exist, is an overflow into datetime and smalldatetime (242), NULL under both options off, as any overflow is;
        // into date, datetime2 and time it fails (241).
        _ = construct;
        Assert.Equal(expected, await UnderEach([], batch));
    }
}

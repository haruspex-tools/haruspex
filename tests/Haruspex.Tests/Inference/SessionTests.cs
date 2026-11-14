using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The options the callers' sessions are declared to set, and the rules that read them: an overflow, a division by zero, a
/// COUNT past int and a value outside a math function's domain give NULL only when ANSI_WARNINGS and ARITHABORT are both off
/// (observed: An_overflow_is_null_only_when_ansi_warnings_and_arithabort_are_both_off, and the observations beside it in the
/// differential tests' SessionObservationTests), and string + with a NULL keeps the other string when CONCAT_NULL_YIELDS_NULL
/// is off. Not known, each rule claims what holds under every setting. docs/decisions/0038-the-callers-session-is-declared.md.
/// </summary>
public class SessionTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, n int NOT NULL, maybe int NULL, big bigint NOT NULL, tiny tinyint NOT NULL, tiny_maybe tinyint NULL,
            ratio float NOT NULL, born date NOT NULL, short varchar(5) NULL, code char(4) NOT NULL, label varchar(8) NULL);
        GO
        CREATE FUNCTION dbo.twice (@v tinyint) RETURNS TABLE AS RETURN SELECT @v AS v;
        GO
        """;

    /// <summary>A body whose every column may overflow, divide by zero or leave a math function's domain, and nothing else.</summary>
    private const string Overflows = """
        DECLARE @small tinyint = (SELECT TOP (1) n FROM dbo.t ORDER BY id);
        SELECT n + 1 AS plus, n * 2 AS times, n / id AS divided, -n AS negated, CAST(big AS int) AS cast_down,
               CONVERT(tinyint, n) AS converted, ISNULL(tiny_maybe, n) AS isnull_wider, COALESCE(short, 0) AS coalesced,
               COUNT(*) AS counted, SUM(n) AS summed, AVG(n) AS averaged, SQRT(ratio) AS root, POWER(n, 2) AS power,
               ABS(n) AS absolute, YEAR(n) AS year_of_int, SUM(n) OVER () AS window_sum, COUNT(*) OVER () AS window_count,
               AVG(n) OVER () AS window_avg, LAG(tiny, 1, 300) OVER (ORDER BY MIN(id)) AS lagged, @small AS assigned,
               (SELECT v FROM dbo.twice(n)) AS argument
        FROM dbo.t GROUP BY n, big, tiny, tiny_maybe, short, ratio, id;
        """;

    private static IrDocument Procedure(SessionOptions session, string body) =>
        InferProcedure.Infer("dbo.p", session, ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    private static EquatableArray<IrField> Fields(IrDocument doc)
    {
        var row = (IrRef)((IrArray)Assert.Single(doc.Objects[0].Results!).Type).Items;
        return doc.Types[row.Name].Fields;
    }

    /// <summary>Each field's reasons to be null, as <c>name: rule rule</c>, one line each.</summary>
    private static string Reasons(IrDocument doc) =>
        string.Join("\n", Fields(doc).Select(f => $"{f.Name}: {string.Join(" ", f.NullableBecause.Select(r => IrWireNames.Of(r.Rule)))}".TrimEnd()));

    /// <summary>The sessions the theories below are inferred under, by a name that says what each declares.</summary>
    private static readonly Dictionary<string, SessionOptions> Sessions = new(StringComparer.Ordinal)
    {
        ["ANSI_WARNINGS on"] = new(AnsiWarnings: true),
        ["ARITHABORT on"] = new(ArithAbort: true),
        ["ANSI_WARNINGS on and ARITHABORT off"] = new(AnsiWarnings: true, ArithAbort: false),
        ["ANSI_WARNINGS off and ARITHABORT on"] = new(AnsiWarnings: false, ArithAbort: true),
        ["nothing known"] = SessionOptions.NotKnown,
        ["both off"] = new(AnsiWarnings: false, ArithAbort: false),
        ["ANSI_WARNINGS off and ARITHABORT not known"] = new(AnsiWarnings: false),
        ["ARITHABORT off and ANSI_WARNINGS not known"] = new(ArithAbort: false),
        ["only CONCAT_NULL_YIELDS_NULL known"] = new(ConcatNullYieldsNull: true),
    };

    private static readonly string[] ErrorSessions =
        ["ANSI_WARNINGS on", "ARITHABORT on", "ANSI_WARNINGS on and ARITHABORT off", "ANSI_WARNINGS off and ARITHABORT on"];

    private static readonly string[] NullSessions =
        ["nothing known", "both off", "ANSI_WARNINGS off and ARITHABORT not known", "ARITHABORT off and ANSI_WARNINGS not known", "only CONCAT_NULL_YIELDS_NULL known"];

    public static TheoryData<string> MakeOverflowAnError { get; } = [.. ErrorSessions];

    public static TheoryData<string> LeaveOverflowNull { get; } = [.. NullSessions];

    [Fact]
    public void The_theories_together_cover_every_session_named() =>
        Assert.Equal(Sessions.Keys.Order(StringComparer.Ordinal), ErrorSessions.Concat(NullSessions).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(LeaveOverflowNull))]
    public void Every_overflow_is_null_unless_an_option_known_on_makes_it_an_error(string session)
    {
        var doc = Procedure(Sessions[session], Overflows);

        Assert.Equal(
            """
            plus: arithmetic-overflow
            times: arithmetic-overflow
            divided: arithmetic-overflow
            negated: arithmetic-overflow
            cast_down: arithmetic-overflow
            converted: arithmetic-overflow
            isnull_wider: nullable-column arithmetic-overflow
            coalesced: arithmetic-overflow
            counted: count-overflow
            summed: arithmetic-overflow
            averaged: arithmetic-overflow
            root: domain-error
            power: arithmetic-overflow
            absolute: arithmetic-overflow
            year_of_int: arithmetic-overflow
            window_sum: arithmetic-overflow
            window_count: count-overflow
            window_avg: arithmetic-overflow
            lagged: arithmetic-overflow
            assigned: empty-subquery arithmetic-overflow
            argument: arithmetic-overflow
            """,
            Reasons(doc));
    }

    [Theory]
    [MemberData(nameof(MakeOverflowAnError))]
    public void With_ansi_warnings_or_arithabort_on_no_overflow_is_null(string session)
    {
        var doc = Procedure(Sessions[session], Overflows);

        // Only what is not an overflow stays: the variable is assigned a subquery, which may return no row.
        Assert.Equal(
            """
            plus:
            times:
            divided:
            negated:
            cast_down:
            converted:
            isnull_wider:
            coalesced:
            counted:
            summed:
            averaged:
            root:
            power:
            absolute:
            year_of_int:
            window_sum:
            window_count:
            window_avg:
            lagged:
            assigned: empty-subquery
            argument:
            """,
            Reasons(doc));
    }

    [Fact]
    public void A_failed_conversion_under_try_cast_stays_null_whatever_the_session()
    {
        // TRY_CAST gives NULL instead of the error, so the error the session asks for never happens
        // (Try_cast_and_try_convert_are_null_when_the_conversion_fails).
        var doc = Procedure(new SessionOptions(AnsiWarnings: true, ArithAbort: true), "SELECT TRY_CAST(big AS int) AS a, maybe + 1 AS b FROM dbo.t;");

        Assert.Equal("a: failed-conversion\nb: nullable-column", Reasons(doc));
    }

    [Fact]
    public void With_concat_null_yields_null_off_a_string_plus_one_never_null_is_never_null()
    {
        // With_concat_null_yields_null_off_a_string_plus_null_is_the_string: NULL + 'x' is 'x', so only both NULL is NULL.
        const string Body = "SELECT short + 'x' AS one_side, short + label AS both_sides, code + 'x' AS neither, 'x' + short AS other_side FROM dbo.t;";

        Assert.Equal(
            "one_side:\nboth_sides: nullable-column nullable-column\nneither:\nother_side:",
            Reasons(Procedure(new SessionOptions(ConcatNullYieldsNull: false), Body)));
        Assert.Equal(
            "one_side: nullable-column\nboth_sides: nullable-column nullable-column\nneither:\nother_side: nullable-column",
            Reasons(Procedure(new SessionOptions(ConcatNullYieldsNull: true), Body)));
        Assert.Equal(
            "one_side: nullable-column\nboth_sides: nullable-column nullable-column\nneither:\nother_side: nullable-column",
            Reasons(Procedure(SessionOptions.NotKnown, Body)));
    }

    [Fact]
    public void A_string_cast_to_a_date_type_may_overflow_unless_the_session_makes_that_an_error()
    {
        // Into datetime or smalldatetime, a date that does not exist or is out of range is an overflow (242), NULL under both
        // options off (A_string_that_names_no_date_or_a_date_out_of_range_fails_or_overflows_by_the_target_type). Into date it
        // is an error whatever the options (241), so arithmetic-overflow there claims less than the server does: sound, and
        // left as it is. A declared session makes each an error.
        const string Body = "SELECT CAST(code AS datetime) AS dt, CAST(code AS smalldatetime) AS small, CAST(code AS date) AS d FROM dbo.t;";

        Assert.Equal("dt: arithmetic-overflow\nsmall: arithmetic-overflow\nd: arithmetic-overflow", Reasons(Procedure(SessionOptions.NotKnown, Body)));
        Assert.Equal("dt:\nsmall:\nd:", Reasons(Procedure(new SessionOptions(AnsiWarnings: true), Body)));
    }

    [Theory]
    [InlineData("SET ANSI_WARNINGS OFF;")]
    [InlineData("SET ARITHABORT OFF;")]
    [InlineData("SET CONCAT_NULL_YIELDS_NULL ON;")]
    public void A_set_in_the_body_that_would_undo_the_declaration_leaves_the_results_unknown(string set)
    {
        // A SET lasts until the procedure returns, so after it the callers' options no longer hold; as before, it stops the walk.
        var doc = Procedure(new SessionOptions(AnsiWarnings: true, ArithAbort: true, ConcatNullYieldsNull: false), $"{set}\nSELECT n + 1 AS a, short + 'x' AS b FROM dbo.t;");

        Assert.Null(doc.Objects[0].Results);
    }

    [Fact]
    public void A_set_on_in_the_body_is_passed_over_and_claims_no_less_than_the_declaration()
    {
        var doc = Procedure(new SessionOptions(AnsiWarnings: true), "SET ARITHABORT ON;\nSELECT n + 1 AS a FROM dbo.t;");

        Assert.Equal("a:", Reasons(doc));
    }

    [Fact]
    public void The_document_records_the_options_declared_by_name_and_leaves_out_those_not_known()
    {
        var declared = Procedure(new SessionOptions(ConcatNullYieldsNull: false, AnsiWarnings: true), "SELECT n FROM dbo.t;");
        var none = Procedure(SessionOptions.NotKnown, "SELECT n FROM dbo.t;");

        Assert.Equal([new IrSessionOption("ansiWarnings", true), new IrSessionOption("concatNullYieldsNull", false)], declared.Source.Session);
        Assert.Empty(none.Source.Session);
    }
}

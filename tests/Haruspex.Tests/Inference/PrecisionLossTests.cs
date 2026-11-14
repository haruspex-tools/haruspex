using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A decimal rounded to fewer places (<c>precision-loss</c>): with NUMERIC_ROUNDABORT on it is NULL when ANSI_WARNINGS and
/// ARITHABORT are both off, and under TRY_CAST and TRY_CONVERT whatever they are (observed: the differential tests'
/// PrecisionLossObservationTests). Each construct that rounds gives the reason unless the callers' declared session rules it
/// out; each that keeps every place, or rounds where the option doesn't see it, never does.
/// docs/decisions/0038-the-callers-session-is-declared.md, as amended for NUMERIC_ROUNDABORT.
/// </summary>
public class PrecisionLossTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, n int NOT NULL, d4 decimal(10,4) NOT NULL, d2 decimal(10,2) NOT NULL, d2_maybe decimal(10,2) NULL,
            d380 decimal(38,0) NOT NULL, d3810 decimal(38,10) NOT NULL, d2010 decimal(20,10) NOT NULL, d3714 decimal(37,14) NOT NULL,
            d357 decimal(35,7) NOT NULL, m money NOT NULL, ratio float NOT NULL, text20 varchar(20) NOT NULL, text3 varchar(3) NOT NULL);
        GO
        CREATE FUNCTION dbo.rows_of (@p decimal(10,2)) RETURNS TABLE AS RETURN SELECT @p AS p;
        GO
        CREATE FUNCTION dbo.tagged (@p decimal(10,2)) RETURNS nvarchar(max) WITH RETURNS NULL ON NULL INPUT AS
        BEGIN
            RETURN (SELECT @p AS p FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        END
        GO
        """;

    /// <summary>A body whose every column is a value rounded to fewer places, never null otherwise.</summary>
    private const string Rounding = """
        DECLARE @declared decimal(10,2) = (SELECT TOP (1) d4 FROM dbo.t ORDER BY id), @set decimal(10,2) = 0, @selected decimal(10,2) = 0;
        SET @set = (SELECT TOP (1) d4 FROM dbo.t ORDER BY id);
        SELECT @selected = d4 FROM dbo.t WHERE id = 1;
        SELECT CAST(d4 AS decimal(10,2)) AS cast_down, CONVERT(decimal(10,2), d4) AS converted, CAST(1.2300 AS decimal(10,2)) AS literal,
               CAST(m AS decimal(10,2)) AS from_money, CAST(text20 AS decimal(10,2)) AS from_text, CAST('1.20' AS decimal(10,1)) AS text_literal,
               ISNULL(d2_maybe, d4) AS isnull_second, COALESCE(d4, d380) AS coalesced, CASE WHEN n > 0 THEN d4 ELSE d380 END AS cased,
               IIF(n > 0, d4, d380) AS iif_of, CHOOSE(1, d4, d380) AS chosen, LAG(d2, 1, d4) OVER (ORDER BY id) AS lagged,
               d2010 * d2010 AS product, d3714 + d357 AS sum_cut, d3714 - d357 AS difference_cut, d3810 / 3 AS quotient, n / d380 AS int_quotient,
               AVG(d2) OVER () AS window_avg, (SELECT p FROM dbo.rows_of(d4)) AS argument, @declared AS declared, @set AS assigned,
               @selected AS selected, JSON_VALUE(dbo.tagged(d4), '$.p') AS null_input
        FROM dbo.t;
        """;

    /// <summary>A body whose every column keeps every place, or rounds where NUMERIC_ROUNDABORT doesn't see it.</summary>
    private const string Keeping = """
        SELECT CAST(d2 AS decimal(12,4)) AS widened, d2 + d2 AS sum_within, CAST(1.23 AS decimal(10,2)) AS literal_fits,
               CAST(d4 AS int) AS truncated, CAST(ratio AS decimal(10,2)) AS from_float, CAST(m AS decimal(19,4)) AS money_kept,
               CAST(d4 AS money) AS to_money, CAST('1.2' AS decimal(10,2)) AS text_fits, CAST(text3 AS decimal(10,2)) AS short_text,
               d2 / d2 AS quotient_uncut, CAST(n AS decimal(10,0)) / d2010 AS quotient_above_threshold, d4 * d4 AS product_within,
               d3810 + d3810 AS sum_uncut, ROUND(d4, 2) AS rounded, m * m AS money_product, d4 % 0.7 AS remainder,
               ISNULL(d2_maybe, d2) AS isnull_same, COALESCE(d4, d2) AS coalesced_wider, SUM(d4) OVER () AS window_sum
        FROM dbo.t;
        """;

    private static IrDocument Procedure(SessionOptions session, string body) =>
        InferProcedure.Infer("dbo.p", session, ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    private static EquatableArray<IrField> Fields(IrDocument doc)
    {
        var row = (IrRef)((IrArray)doc.Objects[0].Results![^1].Type).Items;
        return doc.Types[row.Name].Fields;
    }

    /// <summary>Each field's reasons to be null, as <c>name: rule rule</c>, one line each.</summary>
    private static string Reasons(IrDocument doc) =>
        string.Join("\n", Fields(doc).Select(f => $"{f.Name}: {string.Join(" ", f.NullableBecause.Select(r => IrWireNames.Of(r.Rule)))}".TrimEnd()));

    /// <summary>The fields that give <c>precision-loss</c>, by name.</summary>
    private static IEnumerable<string> Losing(IrDocument doc) =>
        Fields(doc).Where(f => f.NullableBecause.Any(r => r.Rule == IrNullRule.PrecisionLoss)).Select(f => f.Name);

    /// <summary>The sessions the theories below are inferred under, by a name that says what each declares.</summary>
    private static readonly Dictionary<string, SessionOptions> Sessions = new(StringComparer.Ordinal)
    {
        ["nothing known"] = SessionOptions.NotKnown,
        ["NUMERIC_ROUNDABORT on"] = new(NumericRoundAbort: true),
        ["all three off but NUMERIC_ROUNDABORT on"] = new(AnsiWarnings: false, ArithAbort: false, NumericRoundAbort: true),
        ["ANSI_WARNINGS and ARITHABORT off"] = new(AnsiWarnings: false, ArithAbort: false),
        ["only CONCAT_NULL_YIELDS_NULL known"] = new(ConcatNullYieldsNull: true),
        ["NUMERIC_ROUNDABORT off"] = new(NumericRoundAbort: false),
        ["ANSI_WARNINGS on"] = new(AnsiWarnings: true),
        ["ARITHABORT on"] = new(ArithAbort: true),
        ["ANSI_WARNINGS on and NUMERIC_ROUNDABORT on"] = new(AnsiWarnings: true, NumericRoundAbort: true),
        ["ARITHABORT on and ANSI_WARNINGS off"] = new(AnsiWarnings: false, ArithAbort: true),
        ["as tedious connects"] = new(AnsiWarnings: true, ArithAbort: true, ConcatNullYieldsNull: true, NumericRoundAbort: false),
    };

    private static readonly string[] NullSessions =
        ["nothing known", "NUMERIC_ROUNDABORT on", "all three off but NUMERIC_ROUNDABORT on", "ANSI_WARNINGS and ARITHABORT off", "only CONCAT_NULL_YIELDS_NULL known"];

    private static readonly string[] ErrorOrSilentSessions =
        ["NUMERIC_ROUNDABORT off", "ANSI_WARNINGS on", "ARITHABORT on", "ANSI_WARNINGS on and NUMERIC_ROUNDABORT on", "ARITHABORT on and ANSI_WARNINGS off", "as tedious connects"];

    public static TheoryData<string> LeaveRoundingNull { get; } = [.. NullSessions];

    public static TheoryData<string> MakeRoundingAnErrorOrSilent { get; } = [.. ErrorOrSilentSessions];

    public static TheoryData<string> EverySession { get; } = [.. Sessions.Keys];

    [Fact]
    public void The_theories_together_cover_every_session_named() =>
        Assert.Equal(Sessions.Keys.Order(StringComparer.Ordinal), NullSessions.Concat(ErrorOrSilentSessions).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(LeaveRoundingNull))]
    public void Every_rounding_is_null_unless_the_session_makes_it_an_error_or_silent(string session)
    {
        var doc = Procedure(Sessions[session], Rounding);

        // The other reasons: a variable assigned a subquery that may return no row; an overflow, NULL under these sessions,
        // where a value may not fit (money's integer digits in decimal(10,2), a sum past 38 digits, a division) or where a
        // variable may keep its first value, 0, which an int literal assigned to it is typed as possibly overflowing; and the
        // call of a function declared RETURNS NULL ON NULL INPUT, whose argument is NULL only when rounded.
        Assert.Equal(
            """
            cast_down: precision-loss
            converted: precision-loss
            literal: precision-loss
            from_money: arithmetic-overflow precision-loss
            from_text: arithmetic-overflow precision-loss
            text_literal: arithmetic-overflow precision-loss
            isnull_second: nullable-column precision-loss
            coalesced: precision-loss
            cased: precision-loss
            iif_of: precision-loss
            chosen: precision-loss
            lagged: precision-loss
            product: precision-loss
            sum_cut: arithmetic-overflow precision-loss
            difference_cut: arithmetic-overflow precision-loss
            quotient: arithmetic-overflow precision-loss
            int_quotient: arithmetic-overflow precision-loss
            window_avg: precision-loss
            argument: precision-loss
            declared: empty-subquery precision-loss
            assigned: empty-subquery precision-loss
            selected: arithmetic-overflow precision-loss
            null_input: null-input
            """,
            Reasons(doc));
    }

    [Theory]
    [MemberData(nameof(MakeRoundingAnErrorOrSilent))]
    public void With_numeric_roundabort_off_or_ansi_warnings_or_arithabort_on_no_rounding_is_null(string session)
    {
        var doc = Procedure(Sessions[session], Rounding);

        Assert.Empty(Losing(doc));
        // Nor then is the argument of a function declared RETURNS NULL ON NULL INPUT, so neither is its call.
        Assert.Empty(Fields(doc).Single(f => f.Name == "null_input").NullableBecause);
    }

    [Theory]
    [MemberData(nameof(EverySession))]
    public void What_keeps_every_place_or_rounds_unseen_is_never_a_loss_of_precision(string session) =>
        Assert.Empty(Losing(Procedure(Sessions[session], Keeping)));

    [Fact]
    public void Under_try_cast_a_loss_of_precision_is_null_unless_numeric_roundabort_is_declared_off()
    {
        // TRY_CAST turns the error ANSI_WARNINGS or ARITHABORT would raise into NULL
        // (Under_try_cast_a_loss_of_precision_is_null_whenever_numeric_roundabort_is_on).
        const string Body = "SELECT TRY_CAST(d4 AS decimal(10,2)) AS a, TRY_CONVERT(decimal(10,2), d4) AS b, TRY_CAST(d2 AS decimal(12,4)) AS c FROM dbo.t;";

        foreach (var session in new SessionOptions[] { SessionOptions.NotKnown, new(AnsiWarnings: true, ArithAbort: true), new(NumericRoundAbort: true) })
            Assert.Equal("a: precision-loss\nb: precision-loss\nc:", Reasons(Procedure(session, Body)));
        Assert.Equal("a:\nb:\nc:", Reasons(Procedure(new SessionOptions(NumericRoundAbort: false), Body)));
    }

    [Fact]
    public void A_rounding_that_may_also_overflow_gives_both_reasons_each_removed_by_its_own_options()
    {
        // decimal(38,10) into decimal(10,2) may overflow (eight integer digits of twenty-eight) and rounds (two places of ten).
        const string Body = "SELECT CAST(d3810 AS decimal(10,2)) AS a FROM dbo.t;";

        Assert.Equal("a: arithmetic-overflow precision-loss", Reasons(Procedure(SessionOptions.NotKnown, Body)));
        Assert.Equal("a: arithmetic-overflow", Reasons(Procedure(new SessionOptions(NumericRoundAbort: false), Body)));
        Assert.Equal("a:", Reasons(Procedure(new SessionOptions(AnsiWarnings: true), Body)));
    }

    [Fact]
    public void Set_numeric_roundabort_off_in_the_body_is_passed_over_and_claims_no_less()
    {
        // It makes a loss of precision silent from there on, so the reason given claims less than holds: sound, and the results
        // stay typed, where before this every SET NUMERIC_ROUNDABORT stopped the walk.
        var doc = Procedure(SessionOptions.NotKnown, "SET NUMERIC_ROUNDABORT OFF;\nSELECT CAST(d4 AS decimal(10,2)) AS a FROM dbo.t;");

        Assert.Equal("a: precision-loss", Reasons(doc));
    }

    [Fact]
    public void Set_numeric_roundabort_on_in_the_body_leaves_the_results_unknown()
    {
        // It lasts until the procedure returns (A_loss_of_precision_is_null_when_numeric_roundabort_is_on_and_ansi_warnings_and_arithabort_are_off,
        // "a procedure that sets NUMERIC_ROUNDABORT on"), so a declaration of it off no longer holds; as for ANSI_WARNINGS
        // OFF, it stops the walk.
        var doc = Procedure(new SessionOptions(NumericRoundAbort: false), "SET NUMERIC_ROUNDABORT ON;\nSELECT CAST(d4 AS decimal(10,2)) AS a FROM dbo.t;");

        Assert.Null(doc.Objects[0].Results);
    }

    [Fact]
    public void The_document_records_numeric_roundabort_when_it_is_declared()
    {
        var declared = Procedure(new SessionOptions(NumericRoundAbort: false, AnsiWarnings: true), "SELECT d2 FROM dbo.t;");

        Assert.Equal([new IrSessionOption("ansiWarnings", true), new IrSessionOption("numericRoundAbort", false)], declared.Source.Session);
    }
}

using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server gives for the numeric expressions haruspex types: decimal arithmetic beyond 38 digits and <c>%</c>, a bit
/// beside a number, integer division at its edge, <c>AVG</c>, the statistical aggregates and the numeric functions (<c>ROUND</c>,
/// <c>FLOOR</c>, <c>CEILING</c>, <c>ABS</c>, <c>SIGN</c>, <c>POWER</c>, <c>SQRT</c>, <c>SQUARE</c>, <c>LOG</c>, <c>LOG10</c>,
/// <c>EXP</c>). Types are read as the server describes them (<c>sys.dm_exec_describe_first_result_set</c>); what can be NULL,
/// and what is an error instead, from values, by default and under a caller's ANSI_WARNINGS and ARITHABORT off. The rules rest
/// on these (Haruspex.SqlServer.Inference's README.md, "Decimal arithmetic", "Numeric functions"); <c>ArithmeticGridTests</c>
/// checks the precision rules against the server over a grid. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class NumericObservationTests(SqlServerFixture server)
{
    private const string Off = "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; ";

    private Task<CallOutcome> Run(string call) => ObservedCall.Run(server, [], call);

    /// <summary>A row of one value of each type the expressions below read, so that a column's type is its own.</summary>
    private const string Row = """
        (SELECT CAST(1 AS decimal(10,2)) AS d, CAST(1 AS decimal(19,2)) AS d192, CAST(1 AS decimal(19,6)) AS d196, CAST(1 AS decimal(38,0)) AS d380,
                CAST(0.1 AS decimal(38,38)) AS d3838, CAST(1 AS decimal(38,10)) AS d3810, CAST(1 AS numeric(10,2)) AS nd, CAST(1 AS decimal(5,5)) AS d55,
                CAST(1 AS money) AS m, CAST(1 AS smallmoney) AS sm, CAST(1 AS int) AS i, CAST(1 AS smallint) AS si, CAST(1 AS tinyint) AS ti,
                CAST(1 AS bigint) AS bi, CAST(1 AS bit) AS bt, CAST(1 AS float) AS f, CAST(1 AS real) AS r) AS x
        """;

    /// <summary>The server's description of a query's columns: <c>name type</c>, comma-separated.</summary>
    private Task<string> Describe(string select) => FirstResultSet.Describe(server, select);

    /// <summary>The error a query that does not compile gives (the least number, before the batch's own 11501).</summary>
    private async Task<int> DescribeError(string select)
    {
        var query = select.Replace("'", "''", StringComparison.Ordinal);
        var outcome = await Run($"SELECT MIN(error_number) AS c FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0);");
        Assert.Null(outcome.Error);
        return int.Parse(outcome.Sets[0]["c: ".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One value, or the error's number, by default and under ANSI_WARNINGS and ARITHABORT off: <c>8115 / null</c>. The value is
    /// read as varchar, which a .NET decimal's 28 digits would not hold, and which cannot itself overflow.
    /// </summary>
    private async Task<string> Both(string expression)
    {
        async Task<string> One(string options)
        {
            var outcome = await Run($"{options}SELECT CONVERT(varchar(100), {expression}) AS v;");
            return outcome.Error is { } error ? error.ToString(System.Globalization.CultureInfo.InvariantCulture) : outcome.Sets[0]["v: ".Length..];
        }

        return $"{await One("SET ANSI_WARNINGS ON; SET ARITHABORT ON; ")} / {await One(Off)}";
    }

    // ---- decimal arithmetic -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Decimal_arithmetic_beyond_38_digits_caps_the_precision_and_cuts_the_scale()
    {
        // Within 38 digits the documented formulas hold (ExpressionObservationTests). Beyond, the precision is 38 and the scale
        // is cut: for + and -, to 38 less the integer digits max(p1 - s1, p2 - s2), with no floor; for * and /, to
        // min(s, max(6, 38 - (p - s))), p and s being the uncapped result's, so never below 6 unless it was already. A decimal
        // literal is numeric of its own digits, so -1.0 * decimal(19,2) is numeric(22,3).
        Assert.Equal(
            "a numeric(2,1), b numeric(2,1), c numeric(22,3), e decimal(38,7), f decimal(38,0), g decimal(38,37), h decimal(38,6), "
            + "k decimal(38,0), l decimal(38,10), n decimal(38,0), o numeric(38,1), p decimal(38,38), q decimal(38,6), s decimal(38,6), t decimal(38,0)",
            await Describe($"""
                SELECT 1.0 AS a, -1.0 AS b, -1.0 * d192 AS c, d192 * d196 AS e, d380 * d380 AS f, d3838 * d3838 AS g, d380 / d3838 AS h,
                       d380 + d380 AS k, d3810 + d3810 AS l, d3810 + d380 AS n, -1.0 * d380 AS o, d3838 + d3838 AS p, d3810 / d3810 AS q,
                       d3810 * d3810 AS s, d380 - d3838 AS t
                FROM {Row}
                """));
    }

    [Fact]
    public async Task Modulo_has_the_documented_precision_and_scale_and_integers_and_money_keep_their_type()
    {
        // % over decimals: scale max(s1, s2), precision min(p1 - s1, p2 - s2) plus it, never past 38. Two integers give the wider,
        // money or smallmoney with an integer or each other theirs, as + does; float or real is an error (402).
        Assert.Equal(
            "k decimal(10,2), k2 decimal(10,2), k3 decimal(3,2), k4 decimal(3,2), k5 int, q4 numeric(3,2), q5 decimal(10,2), q6 decimal(7,2), "
            + "o money, o2 money, s decimal(12,4), s2 smallmoney, s3 smallmoney, s4 tinyint, s5 bigint, s6 decimal(38,38)",
            await Describe($"""
                SELECT i % d AS k, d % i AS k2, 7 % d AS k3, d % 3 AS k4, i % 2 AS k5, 1.5 % d AS q4, bi % d AS q5, si % d AS q6,
                       m % 2 AS o, m % m AS o2, m % d AS s, sm % 2 AS s2, sm % sm AS s3, ti % ti AS s4, i % bi AS s5, d380 % d3838 AS s6
                FROM {Row}
                """));
        Assert.Equal(402, await DescribeError($"SELECT f % 2 AS s FROM {Row}"));
        Assert.Equal(402, await DescribeError($"SELECT d % f AS s FROM {Row}"));
    }

    [Fact]
    public async Task A_bit_beside_a_number_takes_the_numbers_type_and_two_bits_are_an_error()
    {
        // Beside a decimal a bit is converted to that decimal's own type (bit * decimal(10,2) is decimal(10,2) * decimal(10,2)),
        // beside an integer to that integer, beside money to money, beside float to float; two bits are an error (402), and so is
        // negating one (8117).
        Assert.Equal(
            "a tinyint, b int, c int, d decimal(23,13), e decimal(10,2), f decimal(11,2), g numeric(11,2), h decimal(21,4), k numeric(3,1), "
            + "l decimal(38,37), n decimal(38,0), o int, p bigint, q money, s smallmoney, t float",
            await Describe($"""
                SELECT ti + bt AS a, i * bt AS b, bt - i AS c, bt / d AS d, bt % d AS e, bt + d AS f, bt + nd AS g, bt * d AS h, bt + 1.5 AS k,
                       bt * d3838 AS l, d380 + bt AS n, bt + 2 AS o, bt * bi AS p, bt + m AS q, bt + sm AS s, f + bt AS t
                FROM {Row}
                """));
        Assert.Equal(402, await DescribeError($"SELECT bt + bt AS a FROM {Row}"));
        Assert.Equal(402, await DescribeError($"SELECT bt % bt AS a FROM {Row}"));
        Assert.Equal(8117, await DescribeError($"SELECT -bt AS a FROM {Row}"));
    }

    [Fact]
    public async Task Decimal_overflow_and_division_by_zero_are_errors_by_default_and_null_under_the_callers_options()
    {
        // Past 38 digits a sum, product or quotient can overflow its capped type; a division or modulo by zero divides by zero.
        // decimal(38,10) + decimal(38,0) is decimal(38,0): the sum is rounded to a whole number, and 29 integer digits fit.
        // Each fails the call by default (8115, 8134) and is NULL under the caller's ANSI_WARNINGS and ARITHABORT off.
        Assert.Equal("8115 / null", await Both("CAST(99999999999999999999999999999999999999 AS decimal(38,0)) + CAST(1 AS decimal(38,0))"));
        Assert.Equal("8115 / null", await Both("CAST(99999999999999999999999999999999999999 AS decimal(38,0)) * CAST(2 AS decimal(1,0))"));
        Assert.Equal("8115 / null", await Both("-1.0 * CAST(99999999999999999999999999999999999999 AS decimal(38,0))"));
        Assert.Equal("8115 / null", await Both("CAST(1 AS decimal(38,0)) / CAST(0.00000000000000000000000000000000000001 AS decimal(38,38))"));
        Assert.Equal("8115 / null", await Both("CAST(9999999999999999999999999999.9999999999 AS decimal(38,10)) + CAST(1 AS decimal(38,10))"));
        Assert.Equal("10000000000000000000000000001 / 10000000000000000000000000001", await Both("CAST(9999999999999999999999999999.9999999999 AS decimal(38,10)) + CAST(1 AS decimal(38,0))"));
        Assert.Equal("8134 / null", await Both("CAST(1 AS decimal(38,0)) / CAST(0 AS decimal(5,2))"));
        Assert.Equal("8134 / null", await Both("CAST(1 AS decimal(10,2)) % CAST(0 AS int)"));
        Assert.Equal("8134 / null", await Both("CAST(1 AS money) % CAST(0 AS money)"));
    }

    [Fact]
    public async Task Decimal_arithmetic_whose_integer_digits_fit_cannot_overflow_at_its_operands_extremes()
    {
        // decimal(19,2) * decimal(19,6) is decimal(38,7): 31 integer digits hold the 30 the product needs, rounded to 7 places.
        // -1.0 * decimal(19,2) is within 38 digits. decimal(38,38) squared keeps 37 places and rounds up to 1, which its one
        // integer digit holds. A modulo is smaller than its divisor, and a division by a literal of at least 1 no greater than its
        // dividend.
        Assert.Equal(
            "a,b,c,d,e,f: -999999999999999999800000000000.0000000,-99999999999999999.990,1.0000000000000000000000000000000000000,99.99,-999999999999999999999999999.900000,0.0000010000",
            (await Run($"""
                {Off}
                SELECT CONVERT(varchar(100), CAST(-99999999999999999.99 AS decimal(19,2)) * CAST(9999999999999.999999 AS decimal(19,6))) AS a,
                       CONVERT(varchar(100), -1.0 * CAST(99999999999999999.99 AS decimal(19,2))) AS b,
                       CONVERT(varchar(100), CAST(0.99999999999999999999999999999999999999 AS decimal(38,38)) * CAST(0.99999999999999999999999999999999999999 AS decimal(38,38))) AS c,
                       CONVERT(varchar(100), CAST(99.99 AS decimal(10,2)) % 100) AS d,
                       CONVERT(varchar(100), CAST(-9999999999999999999999999999 AS decimal(28,0)) / 10) AS e,
                       CONVERT(varchar(100), CAST(1 AS decimal(10,2)) / 1000000) AS f;
                """)).ToString());
    }

    [Fact]
    public async Task Modulo_brings_both_operands_to_its_scale_first_which_can_overflow()
    {
        // % converts each operand to the scale max(s1, s2) before dividing, keeping its own integer digits: past 38 digits that
        // overflows. decimal(38,0) % 1.0 needs 39, decimal(38,38) % 1 too, decimal(10,2) % decimal(38,31) 39;
        // decimal(38,0) % 7, decimal(38,37) % 1 and decimal(10,2) % decimal(38,30) fit 38 and cannot.
        Assert.Equal("8115 / null", await Both("CAST(99999999999999999999999999999999999999 AS decimal(38,0)) % 1.0"));
        Assert.Equal("8115 / null", await Both("CAST(0.5 AS decimal(38,38)) % 1"));
        Assert.Equal("8115 / null", await Both("CAST(0.5 AS decimal(38,37)) % 10"));
        Assert.Equal("8115 / null", await Both("CAST(0.5 AS decimal(37,30)) % 2147483648"));
        Assert.Equal("8115 / null", await Both("CAST(12345678 AS decimal(10,2)) % CAST(3 AS decimal(38,31))"));
        Assert.Equal("1 / 1", await Both("CAST(99999999999999999999999999999999999999 AS decimal(38,0)) % 7"));
        Assert.Equal("0.5000000000000000000000000000000000000 / 0.5000000000000000000000000000000000000", await Both("CAST(0.5 AS decimal(38,37)) % 1"));
        Assert.Equal("0.000000000000000000000000000000 / 0.000000000000000000000000000000", await Both("CAST(12345678 AS decimal(10,2)) % CAST(3 AS decimal(38,30))"));
    }

    [Fact]
    public async Task A_bit_converted_to_a_decimal_of_places_only_overflows_when_it_is_1()
    {
        // A bit beside decimal(p, s) becomes decimal(p, s), which cannot hold 1 when p = s: 8115 by default, NULL
        // under the caller's options. With an integer digit it fits.
        Assert.Equal("8115 / null", await Both("CAST(0.5 AS decimal(9,9)) + CAST(1 AS bit)"));
        Assert.Equal("8115 / null", await Both("CAST(1 AS bit) - CAST(0.5 AS decimal(9,9))"));
        Assert.Equal("8115 / null", await Both("CAST(0.5 AS decimal(38,38)) * CAST(1 AS bit)"));
        Assert.Equal("0.500000000 / 0.500000000", await Both("CAST(0.5 AS decimal(9,9)) + CAST(0 AS bit)"));
        Assert.Equal("10.5000 / 10.5000", await Both("CAST(9.5 AS decimal(5,4)) + CAST(1 AS bit)"));
    }

    [Fact]
    public async Task Integer_and_money_division_overflow_only_at_the_least_value_divided_by_minus_one()
    {
        // The least int, smallint or money divided by -1 overflows; int's modulo by -1 too (money's does not). A division or
        // modulo by a literal of at least 1 cannot.
        Assert.Equal("8115 / null", await Both("CAST(-2147483648 AS int) / CAST(-1 AS int)"));
        Assert.Equal("8115 / null", await Both("CAST(-2147483648 AS int) % CAST(-1 AS int)"));
        Assert.Equal("8115 / null", await Both("CAST(-32768 AS smallint) / CAST(-1 AS smallint)"));
        Assert.Equal("8115 / null", await Both("CAST(-922337203685477.5808 AS money) / CAST(-1 AS money)"));
        Assert.Equal("-0.58 / -0.58", await Both("CAST(-922337203685477.5808 AS money) % CAST(-1 AS money)"));
        Assert.Equal("-2147483648 / -2147483648", await Both("CAST(-2147483648 AS int) / 1"));
        Assert.Equal("-8 / -8", await Both("CAST(-2147483648 AS int) % 10"));
        Assert.Equal("-1e+308 / -1e+308", await Both("CAST(-1e308 AS float) / 1"));
    }

    // ---- AVG and the statistical aggregates ---------------------------------------------------------------------------

    [Fact]
    public async Task Avg_is_typed_by_its_argument_with_or_without_a_window()
    {
        // tinyint, smallint and int give int, bigint bigint, decimal(p, s) decimal(38, max(s, 6)) of its own name, money and
        // smallmoney money, float and real float; a bit is an error (8117).
        Assert.Equal(
            "a int, b int, c int, d bigint, e decimal(38,6), f decimal(38,6), g decimal(38,6), h decimal(38,6), j decimal(38,38), k numeric(38,6), l money, n money, o float, q float",
            await Describe($"SELECT AVG(ti) a, AVG(si) b, AVG(i) c, AVG(bi) d, AVG(d) e, AVG(d192) f, AVG(d196) g, AVG(d380) h, AVG(d3838) j, AVG(nd) k, AVG(m) l, AVG(sm) n, AVG(f) o, AVG(r) q FROM {Row}"));
        Assert.Equal(
            "a int, b decimal(38,6), c money, d float, e decimal(38,38)",
            await Describe($"SELECT AVG(i) OVER () a, AVG(d) OVER (ORDER BY i) b, AVG(m) OVER () c, AVG(r) OVER () d, AVG(d3838) OVER () e FROM {Row}"));
        Assert.Equal(8117, await DescribeError($"SELECT AVG(bt) a FROM {Row}"));
    }

    [Fact]
    public async Task Avg_is_null_over_no_rows_and_overflows_as_sum_does()
    {
        // NULL over no rows and over only NULLs, like SUM; an int's average is an int, cut toward zero. It sums in its own type, so
        // two ints can overflow it (and money and float); a decimal of 38 digits overflows its scale of at least 6. A decimal of at
        // most 19 digits cannot (the argument SUM's README gives), nor two tinyints.
        Assert.Equal(
            "none,nulls,cut,neg: null,null,1,-1",
            (await Run("""
                SELECT (SELECT AVG(v) FROM (VALUES (1)) AS s (v) WHERE v = 0) AS none, (SELECT AVG(v) FROM (VALUES (CAST(NULL AS int))) AS s (v)) AS nulls,
                       (SELECT AVG(v) FROM (VALUES (1), (2)) AS s (v)) AS cut, (SELECT AVG(v) FROM (VALUES (-1), (-2)) AS s (v)) AS neg;
                """)).ToString());
        Assert.Equal("8115 / null", await Both("(SELECT AVG(v) FROM (VALUES (CAST(2147483647 AS int)), (CAST(2147483647 AS int))) AS s (v))"));
        Assert.Equal("8115 / null", await Both("(SELECT AVG(v) FROM (VALUES (CAST(99999999999999999999999999999999999999 AS decimal(38,0)))) AS s (v))"));
        Assert.Equal("8115 / null", await Both("(SELECT AVG(v) FROM (VALUES (CAST(922337203685477.5807 AS money)), (CAST(922337203685477.5807 AS money))) AS s (v))"));
        Assert.Equal("8115 / null", await Both("(SELECT AVG(v) FROM (VALUES (CAST(1e308 AS float)), (CAST(1e308 AS float))) AS s (v))"));
        Assert.Equal("255 / 255", await Both("(SELECT AVG(v) FROM (VALUES (CAST(255 AS tinyint)), (CAST(255 AS tinyint))) AS s (v))"));
        Assert.Equal(
            "9999999999999999999.000000 / 9999999999999999999.000000",
            await Both("(SELECT AVG(v) FROM (VALUES (CAST(9999999999999999999 AS decimal(19,0))), (CAST(9999999999999999999 AS decimal(19,0)))) AS s (v))"));
    }

    [Fact]
    public async Task The_statistical_aggregates_are_float_and_the_sample_forms_null_over_fewer_than_two_values()
    {
        // STDEV, STDEVP, VAR and VARP are float whatever they read, with a window too; a bit is an error (8117). Over one value the
        // sample forms (STDEV, VAR) are NULL, over a window too, and the population forms 0; over no rows or only NULLs all are
        // NULL.
        Assert.Equal(
            "a float, b float, c float, d float, e float, g float",
            await Describe($"SELECT STDEV(i) a, STDEVP(d) b, VAR(m) c, VARP(f) d, STDEV(r) e, VAR(ti) g FROM {Row}"));
        Assert.Equal("h float, k float", await Describe($"SELECT STDEV(i) OVER () h, VARP(d) OVER () k FROM {Row}"));
        Assert.Equal(8117, await DescribeError($"SELECT STDEV(bt) a FROM {Row}"));
        Assert.Equal(
            "sd,sdp,v,vp,none,nulls,window: null,0,null,0,null,null,null",
            (await Run("""
                SELECT (SELECT STDEV(v) FROM (VALUES (1)) AS s (v)) AS sd, (SELECT STDEVP(v) FROM (VALUES (1)) AS s (v)) AS sdp,
                       (SELECT VAR(v) FROM (VALUES (1), (NULL)) AS s (v)) AS v, (SELECT VARP(v) FROM (VALUES (1)) AS s (v)) AS vp,
                       (SELECT VARP(v) FROM (VALUES (1)) AS s (v) WHERE v = 0) AS none, (SELECT STDEVP(v) FROM (VALUES (CAST(NULL AS int))) AS s (v)) AS nulls,
                       (SELECT MAX(s) FROM (SELECT STDEV(v) OVER () AS s FROM (VALUES (1)) AS t (v)) AS u) AS window;
                """)).ToString());
    }

    [Fact]
    public async Task The_statistical_aggregates_overflow_only_over_float()
    {
        // Squares of a float can overflow float, an error by default and NULL under the caller's options; squares of the exact
        // types, decimal(38,0) and bigint at their extremes, cannot (10^76 is far inside float's range), nor a real's (10^77).
        Assert.Equal("8115 / null", await Both("(SELECT STDEV(v) FROM (VALUES (CAST(1e300 AS float)), (CAST(1e300 AS float))) AS s (v))"));
        Assert.Equal(
            "2e+076 / 2e+076",
            await Both("(SELECT VAR(v) FROM (VALUES (CAST(99999999999999999999999999999999999999 AS decimal(38,0))), (CAST(-99999999999999999999999999999999999999 AS decimal(38,0)))) AS s (v))"));
        Assert.Equal(
            "1.70141e+038 / 1.70141e+038",
            await Both("(SELECT VAR(v) FROM (VALUES (CAST(9223372036854775807 AS bigint)), (CAST(-9223372036854775808 AS bigint))) AS s (v))"));
    }

    // ---- numeric functions --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Round_floor_ceiling_abs_and_sign_are_typed_by_their_argument()
    {
        // tinyint and smallint give int; int, bigint, float keep their type; real and bit give float; money and smallmoney give
        // money. A decimal keeps its type through ROUND (both forms) and ABS, is decimal(p, 0) through FLOOR and CEILING, and through
        // SIGN keeps it unless every digit is a place, when it gains an integer digit (decimal(38,38) gives decimal(38,37)).
        Assert.Equal(
            "a decimal(10,2), b decimal(10,2), c int, e int, g money, h float, k float, l numeric(2,1), n decimal(38,0), o decimal(38,38), q bigint, s money, t int, u float",
            await Describe($"SELECT ROUND(d, 1) a, ROUND(d, 1, 1) b, ROUND(i, -1) c, ROUND(ti, 1) e, ROUND(m, 2) g, ROUND(f, 1) h, ROUND(r, 1) k, ROUND(9.5, 0) l, ROUND(d380, 0) n, ROUND(d3838, 2) o, ROUND(bi, 1) q, ROUND(sm, 1) s, ROUND(si, -2) t, ROUND(bt, 1) u FROM {Row}"));
        Assert.Equal(
            "a decimal(10,0), b decimal(10,0), c int, e int, g money, h float, k float, l numeric(2,0), n decimal(38,0), o decimal(38,0), q numeric(10,0), s money, t decimal(5,0), u float",
            await Describe($"SELECT FLOOR(d) a, CEILING(d) b, FLOOR(i) c, FLOOR(ti) e, FLOOR(m) g, FLOOR(f) h, FLOOR(r) k, FLOOR(2.5) l, CEILING(d3838) n, FLOOR(d380) o, FLOOR(nd) q, FLOOR(sm) s, CEILING(d55) t, FLOOR(bt) u FROM {Row}"));
        Assert.Equal(
            "a decimal(10,2), b int, c int, e int, g bigint, h money, k float, l float, n numeric(2,1), o numeric(10,2), q money, s decimal(5,5), u float",
            await Describe($"SELECT ABS(d) a, ABS(i) b, ABS(ti) c, ABS(si) e, ABS(bi) g, ABS(m) h, ABS(f) k, ABS(r) l, ABS(-2.5) n, ABS(nd) o, ABS(sm) q, ABS(d55) s, ABS(bt) u FROM {Row}"));
        Assert.Equal(
            "a decimal(10,2), b int, c int, e int, g bigint, h money, k float, l float, n numeric(2,1), o numeric(10,2), q money, s decimal(6,5), t decimal(38,37), u float",
            await Describe($"SELECT SIGN(d) a, SIGN(i) b, SIGN(ti) c, SIGN(si) e, SIGN(bi) g, SIGN(m) h, SIGN(f) k, SIGN(r) l, SIGN(-2.5) n, SIGN(nd) o, SIGN(sm) q, SIGN(d55) s, SIGN(d3838) t, SIGN(bt) u FROM {Row}"));
    }

    [Fact]
    public async Task Round_overflows_when_it_carries_into_a_digit_its_type_lacks_unless_it_truncates()
    {
        // ROUND(9.5, 0) is 10.0, which numeric(2,1) cannot hold; so for an int rounded to tens at its greatest, and money at its
        // ends rounded to 3, 1 or 0 places. Not to 2: money's ends are .5807 and -.5808, which round down there, and no value
        // can round up past them. A third argument other than 0 truncates instead, which cannot overflow; a
        // NULL third argument rounds. A NULL length is NULL. Rounding to as many places as the type has changes nothing.
        Assert.Equal("8115 / null", await Both("ROUND(9.5, 0)"));
        Assert.Equal("8115 / null", await Both("ROUND(CAST(9.5 AS decimal(2,1)), 0, 0)"));
        Assert.Equal("8115 / null", await Both("ROUND(CAST(9.5 AS decimal(2,1)), 0, NULL)"));
        Assert.Equal("8115 / null", await Both("ROUND(CAST(2147483647 AS int), -1)"));
        Assert.Equal("8115 / null", await Both("ROUND(CAST(922337203685477.5807 AS money), 3)"));
        Assert.Equal("8115 / null", await Both("ROUND(CAST(1.7976931348623157e308 AS float), -308)"));
        Assert.Equal("9.0 / 9.0", await Both("ROUND(CAST(9.5 AS decimal(2,1)), 0, 1)"));
        Assert.Equal("2147483640 / 2147483640", await Both("ROUND(CAST(2147483647 AS int), -1, 1)"));
        Assert.Equal("922337203685477.00 / 922337203685477.00", await Both("ROUND(CAST(922337203685477.5807 AS money), 0, 1)"));
        Assert.Equal("8115 / null", await Both("ROUND(CAST(-922337203685477.5808 AS money), 1)"));
        Assert.Equal("922337203685477.58 / 922337203685477.58", await Both("ROUND(CAST(922337203685477.5807 AS money), 2)"));
        Assert.Equal("-922337203685477.58 / -922337203685477.58", await Both("ROUND(CAST(-922337203685477.5808 AS money), 2)"));
        Assert.Equal("1000 / 1000", await Both("ROUND(CAST(999 AS smallint), -3)"));
        Assert.Equal("9.5 / 9.5", await Both("ROUND(CAST(9.5 AS decimal(2,1)), 1)"));
        Assert.Equal("null / null", await Both("ROUND(CAST(1.5 AS decimal(10,2)), CAST(NULL AS int))"));
    }

    [Fact]
    public async Task Floor_ceiling_abs_and_sign_overflow_only_where_the_type_cannot_hold_the_result()
    {
        // CEILING of decimal(2,2) is 1, which decimal(2,0) holds, and FLOOR or CEILING of a decimal never needs more digits; of money
        // at its extremes it does. ABS overflows int, bigint and money at their least value, not smallint (an int) nor a decimal.
        // SIGN never overflows.
        Assert.Equal("1 / 1", await Both("CEILING(CAST(0.99 AS decimal(2,2)))"));
        Assert.Equal("10 / 10", await Both("CEILING(CAST(9.9 AS decimal(2,1)))"));
        Assert.Equal("8115 / null", await Both("FLOOR(CAST(-922337203685477.5808 AS money))"));
        Assert.Equal("8115 / null", await Both("CEILING(CAST(922337203685477.5807 AS money))"));
        Assert.Equal("8115 / null", await Both("ABS(CAST(-2147483648 AS int))"));
        Assert.Equal("8115 / null", await Both("ABS(CAST(-9223372036854775808 AS bigint))"));
        Assert.Equal("8115 / null", await Both("ABS(CAST(-922337203685477.5808 AS money))"));
        Assert.Equal("32768 / 32768", await Both("ABS(CAST(-32768 AS smallint))"));
        Assert.Equal(
            "0.00000000000000000000000000000000000001 / 0.00000000000000000000000000000000000001",
            await Both("ABS(CAST(-0.00000000000000000000000000000000000001 AS decimal(38,38)))"));
        Assert.Equal("-1.0000000000000000000000000000000000000 / -1.0000000000000000000000000000000000000", await Both("SIGN(CAST(-0.5 AS decimal(38,38)))"));
    }

    [Fact]
    public async Task Power_is_typed_by_its_first_argument_and_the_others_are_float()
    {
        // POWER: tinyint, smallint and int give int, bigint bigint, decimal(p, s) decimal(38, s) of its own name, money and smallmoney
        // money, float, real and bit float; the exponent does not count. SQRT, SQUARE, LOG (both forms), LOG10 and EXP are float.
        Assert.Equal(
            "a decimal(38,2), b int, c int, e int, g bigint, h money, k float, l float, n numeric(38,1), o numeric(38,2), q money, s int, t decimal(38,2), u decimal(38,38), w float",
            await Describe($"SELECT POWER(d, 2) a, POWER(i, 2) b, POWER(ti, 2) c, POWER(si, 2) e, POWER(bi, 2) g, POWER(m, 2) h, POWER(f, 2) k, POWER(r, 2) l, POWER(2.5, 2) n, POWER(nd, 2) o, POWER(sm, 2) q, POWER(2, 0.5) s, POWER(d, f) t, POWER(d3838, 2) u, POWER(bt, 2) w FROM {Row}"));
        Assert.Equal(
            "a float, b float, c float, e float, g float, h float, k float, l float, n float, o float",
            await Describe($"SELECT SQRT(d) a, SQRT(i) b, SQUARE(d) c, SQUARE(i) e, LOG(d) g, LOG(i, 2) h, LOG10(m) k, EXP(d) l, EXP(r) n, SQRT(bt) o FROM {Row}"));
    }

    [Fact]
    public async Task Power_computes_in_float_and_overflows_or_divides_by_zero_as_its_type_allows()
    {
        // POWER is computed in float and converted to its type: past the type it overflows; 0 to a negative power divides by zero;
        // a negative to a fractional power is an invalid floating point operation (3623); each is NULL under the caller's options.
        // An int to 0.5 is cut to an int. decimal(4,2) squared fits decimal(38,2); tinyint cubed fits int.
        Assert.Equal("232 / null", await Both("POWER(CAST(2 AS int), 31)"));
        Assert.Equal("8115 / null", await Both("POWER(CAST(5 AS bigint), 30)"));
        Assert.Equal("8115 / null", await Both("POWER(CAST(2 AS decimal(5,2)), 200)"));
        Assert.Equal("8115 / null", await Both("POWER(CAST(0.5 AS decimal(38,38)), -1)"));
        Assert.Equal("8134 / null", await Both("POWER(CAST(0 AS decimal(5,2)), -1)"));
        Assert.Equal("3623 / null", await Both("POWER(CAST(-8 AS float), 0.5)"));
        Assert.Equal("1 / 1", await Both("POWER(CAST(2 AS int), 0.5)"));
        Assert.Equal("16581375 / 16581375", await Both("POWER(CAST(255 AS tinyint), 3)"));
        Assert.Equal("9998.00 / 9998.00", await Both("POWER(CAST(99.99 AS decimal(4,2)), 2)"));
        Assert.Equal("1.00 / 1.00", await Both("POWER(CAST(0 AS decimal(4,2)), 0)"));
        Assert.Equal("null / null", await Both("POWER(CAST(2 AS int), NULL)"));
    }

    [Fact]
    public async Task A_math_function_outside_its_domain_is_an_error_by_default_and_null_under_the_callers_options()
    {
        // SQRT of a negative, LOG of 0 or less or to a base of 0 or 1, LOG10 of 0 or less: an invalid floating point operation, 3623,
        // which is NULL under the caller's ANSI_WARNINGS and ARITHABORT off as an overflow is. EXP and SQUARE overflow float past
        // e^709 and 1.3e154, never over an exact type's or a real's square, nor EXP of a tinyint or a decimal below 100.
        Assert.Equal("3623 / null", await Both("SQRT(CAST(-1 AS int))"));
        Assert.Equal("3623 / null", await Both("SQRT(CAST(-0.1 AS decimal(5,2)))"));
        Assert.Equal("3623 / null", await Both("LOG(CAST(0 AS float))"));
        Assert.Equal("3623 / null", await Both("LOG(CAST(8 AS int), 1)"));
        Assert.Equal("3623 / null", await Both("LOG(CAST(8 AS float), -2)"));
        Assert.Equal("3623 / null", await Both("LOG10(CAST(0 AS int))"));
        Assert.Equal("8115 / null", await Both("EXP(CAST(1000 AS float))"));
        Assert.Equal("8115 / null", await Both("SQUARE(CAST(1e200 AS float))"));
        Assert.Equal("null / null", await Both("LOG(CAST(8 AS int), NULL)"));
        Assert.Equal("5.56023e+110 / 5.56023e+110", await Both("EXP(CAST(255 AS tinyint))"));
        Assert.Equal("2.66137e+043 / 2.66137e+043", await Both("EXP(CAST(99.99 AS decimal(4,2)))"));
        Assert.Equal("1e+076 / 1e+076", await Both("SQUARE(CAST(99999999999999999999999999999999999999 AS decimal(38,0)))"));
        Assert.Equal("0 / 0", await Both("SQRT(CAST(0 AS float))"));
    }
}

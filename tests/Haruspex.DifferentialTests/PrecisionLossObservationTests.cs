using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Where a decimal loses precision, observed under each combination of the three options that decide what then happens:
/// ANSI_WARNINGS, ARITHABORT and NUMERIC_ROUNDABORT. With NUMERIC_ROUNDABORT off, a value rounded to fewer places is rounded
/// silently. With it on, the rounding is an error (8115), or NULL when ANSI_WARNINGS and ARITHABORT are both off; under TRY_CAST
/// and TRY_CONVERT it is NULL whatever the other two are. Which expressions round is the server's own rule, observed here
/// construct by construct: it is decided by the types (a conversion to fewer places rounds even when the places dropped are
/// zeros), except for a string, whose own places count, and an addition, which rounds only past 38 digits. The rule the
/// precision-loss null reason follows, and why each option removes it, is decision 0038 as amended for NUMERIC_ROUNDABORT.
/// Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class PrecisionLossObservationTests(SqlServerFixture server)
{
    /// <summary>
    /// The eight combinations, NUMERIC_ROUNDABORT off and then on, each with ANSI_WARNINGS and ARITHABORT ON/ON, ON/OFF,
    /// OFF/ON and OFF/OFF, set in that order: ANSI_WARNINGS ON also sets ARITHABORT ON, so ARITHABORT is set after it.
    /// </summary>
    private static readonly (string AnsiWarnings, string ArithAbort, string NumericRoundAbort)[] Combinations =
    [
        ("ON", "ON", "OFF"), ("ON", "OFF", "OFF"), ("OFF", "ON", "OFF"), ("OFF", "OFF", "OFF"),
        ("ON", "ON", "ON"), ("ON", "OFF", "ON"), ("OFF", "ON", "ON"), ("OFF", "OFF", "ON"),
    ];

    /// <summary>What the batches below call: a procedure and functions with a decimal(10,2) parameter, and a table.</summary>
    private static readonly string[] Setup =
    [
        "CREATE TABLE dbo.obs_precision (k int NOT NULL PRIMARY KEY, kept decimal(10,2) NOT NULL, maybe decimal(10,2) NULL);",
        "CREATE PROCEDURE dbo.obs_precision_argument @p decimal(10,2) AS SELECT CONVERT(varchar(60), @p) AS v;",
        "CREATE PROCEDURE dbo.obs_precision_output @o decimal(10,4) OUTPUT AS SET @o = 1.2345;",
        "CREATE FUNCTION dbo.obs_precision_rows (@p decimal(10,2)) RETURNS TABLE AS RETURN SELECT @p AS x;",
        "CREATE PROCEDURE dbo.obs_precision_set_on AS SET NUMERIC_ROUNDABORT ON; DECLARE @d decimal(10,4) = 1.2345; SELECT CONVERT(varchar(60), CAST(@d AS decimal(10,2))) AS v;",
        "CREATE PROCEDURE dbo.obs_precision_set_off AS SET NUMERIC_ROUNDABORT OFF; DECLARE @d decimal(10,4) = 1.2345; SELECT CONVERT(varchar(60), CAST(@d AS decimal(10,2))) AS v;",
    ];

    /// <summary>The variables most batches read: 1.2345 at four places, money, a string, and a decimal(38,0).</summary>
    private const string Declared = "DECLARE @d decimal(10,4) = 1.2345, @m money = 1.2345, @sm smallmoney = 1.2345, @s varchar(20) = '1.2345', @b decimal(38,0) = 1;";

    /// <summary>
    /// What the last result set of a batch holds (its rows joined by <c>;</c>, a row's columns by <c>,</c>), or the error that
    /// fails it, under each combination in <see cref="Combinations"/>' order. NUMERIC_ROUNDABORT is set off again after the batch.
    /// </summary>
    private async Task<string> UnderEach(string batch)
    {
        var outcomes = new List<string>();
        foreach (var (ansiWarnings, arithAbort, numericRoundAbort) in Combinations)
        {
            var outcome = await ObservedCall.Run(server, Setup, $"SET ANSI_WARNINGS {ansiWarnings}; SET ARITHABORT {arithAbort}; SET NUMERIC_ROUNDABORT {numericRoundAbort};\n{batch}\nSET NUMERIC_ROUNDABORT OFF;");
            outcomes.Add(outcome.Error is { } error ? error.ToString(System.Globalization.CultureInfo.InvariantCulture) : outcome.Sets[^1][(outcome.Sets[^1].IndexOf(": ", StringComparison.Ordinal) + 2)..]);
        }

        return string.Join(" ", outcomes);
    }

    /// <summary>A value rounded silently with NUMERIC_ROUNDABORT off; with it on, 8115, or NULL when both others are off.</summary>
    private static string Rounds(string value) => $"{value} {value} {value} {value} 8115 8115 8115 null";

    /// <summary>The same value under every combination: nothing rounds that the options see.</summary>
    private static string Kept(string value) => string.Join(" ", Enumerable.Repeat(value, 8));

    [Fact]
    public async Task Each_combination_of_the_three_is_what_the_session_then_reports()
    {
        Assert.Equal(
            "1,1,0 1,0,0 0,1,0 0,0,0 1,1,1 1,0,1 0,1,1 0,0,1",
            await UnderEach("SELECT CONCAT(CAST(SESSIONPROPERTY('ANSI_WARNINGS') AS int), ',', CAST(SESSIONPROPERTY('ARITHABORT') AS int), ',', CAST(SESSIONPROPERTY('NUMERIC_ROUNDABORT') AS int)) AS v;"));
    }

    public static TheoryData<string, string, string> Rounding { get; } = new()
    {
        // Conversions to fewer places: by the types, so the dropped places being zeros changes nothing.
        { "CAST to fewer places", $"{Declared} SELECT CONVERT(varchar(60), CAST(@d AS decimal(10,2))) AS v;", Rounds("1.23") },
        { "CAST to fewer places, dropping only zeros", "DECLARE @z decimal(10,4) = 1.2300; SELECT CONVERT(varchar(60), CAST(@z AS decimal(10,2))) AS v;", Rounds("1.23") },
        { "CONVERT to fewer places", $"{Declared} SELECT CONVERT(varchar(60), CONVERT(decimal(10,2), @d)) AS v;", Rounds("1.23") },
        { "a decimal literal of more places", "SELECT CONVERT(varchar(60), CAST(1.2300 AS decimal(10,2))) AS v;", Rounds("1.23") },
        { "money to fewer than four places", $"{Declared} SELECT CONVERT(varchar(60), CAST(@m AS decimal(10,2))) AS v;", Rounds("1.23") },
        { "smallmoney to fewer than four places", $"{Declared} SELECT CONVERT(varchar(60), CAST(@sm AS decimal(10,2))) AS v;", Rounds("1.23") },
        { "a string of more places", $"{Declared} SELECT CONVERT(varchar(60), CAST(@s AS decimal(10,2))) AS v;", Rounds("1.23") },
        { "a string literal whose dropped place is a zero", "SELECT CONVERT(varchar(60), CAST('1.20' AS decimal(10,1))) AS v;", Rounds("1.2") },
        { "a JSON number read by OPENJSON into a decimal", "SELECT CONVERT(varchar(60), x) AS v FROM OPENJSON(N'{\"x\":1.2345}') WITH (x decimal(10,2));", Rounds("1.23") },

        // Assignments, which convert to the variable's type.
        { "SET", $"{Declared} DECLARE @e decimal(10,2); SET @e = @d; SELECT CONVERT(varchar(60), @e) AS v;", Rounds("1.23") },
        { "SELECT assigning a variable", $"{Declared} DECLARE @e decimal(10,2); SELECT @e = @d; SELECT CONVERT(varchar(60), @e) AS v;", Rounds("1.23") },
        { "a declaration's value", $"{Declared} DECLARE @e decimal(10,2) = @d; SELECT CONVERT(varchar(60), @e) AS v;", Rounds("1.23") },
        { "a compound assignment", $"{Declared} DECLARE @e decimal(10,2) = 1; SET @e += @d; SELECT CONVERT(varchar(60), @e) AS v;", Rounds("2.23") },
        { "a compound division", "DECLARE @e decimal(10,2) = 1; SET @e /= 3; SELECT CONVERT(varchar(60), @e) AS v;", Rounds("0.33") },
        { "FETCH INTO", "DECLARE @e decimal(10,2); DECLARE c CURSOR LOCAL FOR SELECT CAST(1.2345 AS decimal(10,4)); OPEN c; FETCH NEXT FROM c INTO @e; CLOSE c; DEALLOCATE c; SELECT CONVERT(varchar(60), @e) AS v;", Rounds("1.23") },
        { "UPDATE assigning a variable", "DECLARE @t TABLE (x decimal(10,4)); INSERT @t VALUES (1.2345); DECLARE @e decimal(10,2); UPDATE @t SET @e = x; SELECT CONVERT(varchar(60), @e) AS v;", Rounds("1.23") },
        { "an inline function's argument", $"{Declared} SELECT CONVERT(varchar(60), x) AS v FROM dbo.obs_precision_rows(@d);", Rounds("1.23") },

        // Values brought to one type with fewer places: decimal(10,4) beside decimal(38,0) is decimal(38,0).
        { "ISNULL's second argument", $"{Declared} DECLARE @n decimal(10,2); SELECT CONVERT(varchar(60), ISNULL(@n, @d)) AS v;", Rounds("1.23") },
        { "COALESCE", $"{Declared} SELECT CONVERT(varchar(60), COALESCE(@d, @b)) AS v;", Rounds("1") },
        { "CASE", $"{Declared} SELECT CONVERT(varchar(60), CASE WHEN @b = 1 THEN @d ELSE @b END) AS v;", Rounds("1") },
        { "IIF", $"{Declared} SELECT CONVERT(varchar(60), IIF(@b = 1, @d, @b)) AS v;", Rounds("1") },
        { "CHOOSE", $"{Declared} SELECT CONVERT(varchar(60), CHOOSE(1, @d, @b)) AS v;", Rounds("1") },
        { "UNION ALL", $"{Declared} SELECT CONVERT(varchar(60), x) AS v FROM (SELECT 1 AS k, @d AS x UNION ALL SELECT 2, @b) AS t ORDER BY k;", "1;1 1;1 1;1 1;1 8115 8115 8115 null;1" },
        { "LAG's default", $"{Declared} SELECT CONVERT(varchar(60), LAG(x, 1, @d) OVER (ORDER BY x)) AS v FROM (VALUES (CAST(1 AS decimal(10,2)))) AS t(x);", Rounds("1.23") },

        // Arithmetic whose scale is cut past 38 digits; division by its own threshold (the README's "Precision loss").
        { "a product whose scale is cut", "DECLARE @a decimal(20,10) = 1.0000000001; SELECT CONVERT(varchar(60), @a * @a) AS v;", Rounds("1.00000000020000000") },
        { "a product whose scale is cut, of an exact value", "DECLARE @a decimal(20,10) = 1; SELECT CONVERT(varchar(60), @a * @a) AS v;", Rounds("1.00000000000000000") },
        { "a quotient whose scale is cut", "DECLARE @w decimal(38,10) = 1.0000000001; SELECT CONVERT(varchar(60), @w / 3) AS v;", Rounds("0.3333333333") },
        { "an int divided by decimal(38,0)", "DECLARE @i int = 1, @b decimal(38,0) = 3; SELECT CONVERT(varchar(60), @i / @b) AS v;", Rounds("0.3333333333333333333333333333") },
        { "a sum whose scale is cut, of a value with many integer digits", "DECLARE @a decimal(37,14) = 0, @b decimal(35,7) = 9999999999999999999999999999.9999999; SELECT CONVERT(varchar(60), @a + @b) AS v;", Rounds("9999999999999999999999999999.9999999000") },
        { "a difference whose scale is cut, of a value with many integer digits", "DECLARE @a decimal(37,14) = 0, @b decimal(35,7) = 9999999999999999999999999999.9999999; SELECT CONVERT(varchar(60), @a - @b) AS v;", Rounds("-9999999999999999999999999999.9999999000") },

        // AVG over any decimal, exact or not, with a window too; money's and an integer's are below.
        { "AVG of a decimal", "SELECT CONVERT(varchar(60), AVG(x)) AS v FROM (VALUES (CAST(1 AS decimal(10,2))), (1), (2)) AS t(x);", Rounds("1.333333") },
        { "AVG of a decimal, exact", "SELECT CONVERT(varchar(60), AVG(x)) AS v FROM (VALUES (CAST(1 AS decimal(10,2)))) AS t(x);", Rounds("1.000000") },
        { "AVG of a decimal of more than six places", "SELECT CONVERT(varchar(60), AVG(x)) AS v FROM (VALUES (CAST(0 AS decimal(38,30))), (0)) AS t(x);", Rounds("0.000000000000000000000000000000") },
        { "AVG of a decimal over a window", "SELECT TOP (1) CONVERT(varchar(60), AVG(x) OVER ()) AS v FROM (VALUES (CAST(1 AS decimal(10,2))), (2)) AS t(x);", Rounds("1.500000") },

        // A write into a column: NULL where the column takes it, so a column declared NULL is null anyway.
        { "an INSERT into a column declared NULL", $"{Declared} INSERT dbo.obs_precision (k, kept, maybe) VALUES (1, 0, @d); SELECT CONVERT(varchar(60), maybe) AS v FROM dbo.obs_precision;", Rounds("1.23") },
        { "an UPDATE of a column declared NULL", $"{Declared} INSERT dbo.obs_precision VALUES (1, 0, 0); UPDATE dbo.obs_precision SET maybe = @d; SELECT CONVERT(varchar(60), maybe) AS v FROM dbo.obs_precision;", Rounds("1.23") },
        { "OUTPUT of the value inserted", $"{Declared} INSERT dbo.obs_precision (k, kept, maybe) OUTPUT CONVERT(varchar(60), inserted.maybe) AS v VALUES (1, 0, @d);", Rounds("1.23") },

        // A procedure's own SET lasts while it runs, whatever its caller set.
        { "a procedure that sets NUMERIC_ROUNDABORT on", "EXEC dbo.obs_precision_set_on;", "8115 8115 8115 null 8115 8115 8115 null" },
    };

    [Theory]
    [MemberData(nameof(Rounding))]
    public async Task A_loss_of_precision_is_null_when_numeric_roundabort_is_on_and_ansi_warnings_and_arithabort_are_off(string construct, string batch, string expected)
    {
        _ = construct;
        Assert.Equal(expected, await UnderEach(batch));
    }

    [Fact]
    public async Task Each_function_that_converts_a_decimal_or_averages_one_is_null_when_it_rounds_under_both_others_off()
    {
        // One call of each built-in function the theory above observes rounding (CAST, CONVERT, ISNULL, IIF, CHOOSE, AVG as an
        // aggregate and over a window, LAG's default), in one batch, so that the function table's rows can cite where each is
        // observed. ARITHABORT on ends the batch at the first; ANSI_WARNINGS on alone fails the statement.
        const string Batch = $"""
            {Declared}
            SELECT CONVERT(varchar(60), CAST(@d AS decimal(10,2))) AS a, CONVERT(varchar(60), CONVERT(decimal(10,2), @d)) AS b,
                   CONVERT(varchar(60), ISNULL(CAST(NULL AS decimal(10,2)), @d)) AS c, CONVERT(varchar(60), IIF(@b = 1, @d, @b)) AS e,
                   CONVERT(varchar(60), CHOOSE(1, @d, @b)) AS g,
                   (SELECT CONVERT(varchar(60), AVG(x)) FROM (VALUES (CAST(1 AS decimal(10,2))), (2)) AS t(x)) AS h,
                   (SELECT TOP (1) CONVERT(varchar(60), AVG(x) OVER ()) FROM (VALUES (CAST(1 AS decimal(10,2))), (2)) AS t(x)) AS k,
                   (SELECT TOP (1) CONVERT(varchar(60), LAG(x, 1, @d) OVER (ORDER BY x)) FROM (VALUES (CAST(1 AS decimal(10,2)))) AS t(x)) AS l;
            """;
        const string Values = "1.23,1.23,1.23,1,1,1.500000,1.500000,1.23";

        Assert.Equal($"{Values} {Values} {Values} {Values} 8115 8115 8115 null,null,null,null,null,null,null,null", await UnderEach(Batch));
    }

    [Fact]
    public async Task Under_try_cast_a_loss_of_precision_is_null_whenever_numeric_roundabort_is_on()
    {
        // TRY_ turns the error into NULL, so ANSI_WARNINGS and ARITHABORT on don't make it an error: only NUMERIC_ROUNDABORT
        // off keeps the value. To fewer places, of money and of a string, by TRY_CAST and TRY_CONVERT.
        const string Batch = $"""
            {Declared}
            SELECT CONVERT(varchar(60), TRY_CAST(@d AS decimal(10,2))) AS a, CONVERT(varchar(60), TRY_CONVERT(decimal(10,2), @d)) AS b,
                   CONVERT(varchar(60), TRY_CAST(@m AS decimal(10,2))) AS c, CONVERT(varchar(60), TRY_CAST(@s AS decimal(10,2))) AS e;
            """;
        const string Values = "1.23,1.23,1.23,1.23";
        const string Nulls = "null,null,null,null";

        Assert.Equal($"{Values} {Values} {Values} {Values} {Nulls} {Nulls} {Nulls} {Nulls}", await UnderEach(Batch));
    }

    public static TheoryData<string, string, string> NotRounding { get; } = new()
    {
        // Conversions that keep every place, or that truncate or round without NUMERIC_ROUNDABORT seeing it.
        { "CAST to more places", $"{Declared} SELECT CONVERT(varchar(60), CAST(@d AS decimal(12,6))) AS v;", "1.234500" },
        { "a decimal to int, which truncates", $"{Declared} SELECT CONVERT(varchar(60), CAST(@d AS int)) AS v;", "1" },
        { "a decimal to bigint", $"{Declared} SELECT CONVERT(varchar(60), CAST(@d AS bigint)) AS v;", "1" },
        { "a decimal to bit", $"{Declared} SELECT CONVERT(varchar(60), CAST(@d AS bit)) AS v;", "1" },
        { "a decimal to money, which rounds", "DECLARE @e decimal(10,6) = 1.234567; SELECT CONVERT(varchar(60), CAST(@e AS money), 2) AS v;", "1.2346" },
        { "a decimal to smallmoney", "DECLARE @e decimal(10,6) = 1.234567; SELECT CONVERT(varchar(60), CAST(@e AS smallmoney), 2) AS v;", "1.2346" },
        { "a decimal to float", $"{Declared} SELECT CONVERT(varchar(60), CAST(CAST(@d AS float) AS decimal(10,4))) AS v;", "1.2345" },
        { "float to decimal", "DECLARE @f float = 1.23456; SELECT CONVERT(varchar(60), CAST(@f AS decimal(10,2))) AS v;", "1.23" },
        { "real to decimal", "DECLARE @r real = 1.2345; SELECT CONVERT(varchar(60), CAST(@r AS decimal(10,2))) AS v;", "1.23" },
        { "int to decimal", "DECLARE @i int = 5; SELECT CONVERT(varchar(60), CAST(@i AS decimal(10,2))) AS v;", "5.00" },
        { "money to four places", $"{Declared} SELECT CONVERT(varchar(60), CAST(@m AS decimal(19,4))) AS v;", "1.2345" },
        { "a string of no more places", "DECLARE @s varchar(20) = '1.2'; SELECT CONVERT(varchar(60), CAST(@s AS decimal(10,2))) AS v;", "1.20" },
        { "a string literal of no more places", "SELECT CONVERT(varchar(60), CAST(' 1.2 ' AS decimal(10,1))) AS v;", "1.2" },
        { "a string too short to hold more places", "DECLARE @s varchar(3) = '.12'; SELECT CONVERT(varchar(60), CAST(@s AS decimal(10,2))) AS v;", "0.12" },
        { "a string to money", "SELECT CONVERT(varchar(60), CAST('1.23456' AS money), 2) AS v;", "1.2346" },
        { "PARSE", "SELECT CONVERT(varchar(60), PARSE('1.2345' AS decimal(10,2))) AS v;", "1.23" },
        { "TRY_PARSE", "SELECT CONVERT(varchar(60), TRY_PARSE('1.2345' AS decimal(10,2))) AS v;", "1.23" },
        { "a JSON number read by OPENJSON into money", "SELECT CONVERT(varchar(60), x, 2) AS v FROM OPENJSON(N'{\"x\":1.23456}') WITH (x money);", "1.2346" },

        // Arithmetic whose scale is not cut, or whose type is not a decimal, rounds without it.
        { "a quotient whose scale is not cut", "DECLARE @n decimal(10,2) = 1, @t decimal(10,2) = 3; SELECT CONVERT(varchar(60), @n / @t) AS v;", "0.3333333333333" },
        { "a quotient whose scale is cut but not below its threshold", "DECLARE @a decimal(10,0) = 1, @b decimal(20,10) = 3; SELECT CONVERT(varchar(60), @a / @b) AS v;", "0.333333333333333333" },
        { "a product within 38 digits", $"{Declared} SELECT CONVERT(varchar(60), @d * @d) AS v;", "1.52399025" },
        { "a sum whose scale is cut, of small values", "DECLARE @a decimal(38,10) = 1, @b decimal(38,20) = 0.00000000000000000001; SELECT CONVERT(varchar(60), @a + @b) AS v;", "1.0000000000" },
        { "money times money", $"{Declared} SELECT CONVERT(varchar(60), @m * @m, 2) AS v;", "1.5240" },
        { "money divided by money", "DECLARE @a money = 1, @c money = 3; SELECT CONVERT(varchar(60), @a / @c, 2) AS v;", "0.3333" },
        { "a decimal modulo a decimal", $"{Declared} SELECT CONVERT(varchar(60), @d % 0.7) AS v;", "0.5345" },
        { "SUM of a decimal", "SELECT CONVERT(varchar(60), SUM(x)) AS v FROM (VALUES (CAST(1.2345 AS decimal(10,4))), (1)) AS t(x);", "2.2345" },
        { "AVG of money", "SELECT CONVERT(varchar(60), AVG(x), 2) AS v FROM (VALUES (CAST(0.0001 AS money)), (0), (0)) AS t(x);", "0.0000" },
        { "AVG of an int", "SELECT CONVERT(varchar(60), AVG(x)) AS v FROM (VALUES (1), (1), (2)) AS t(x);", "1" },
        { "STDEV of a decimal", "SELECT CONVERT(varchar(60), CAST(STDEV(x) AS decimal(10,6))) AS v FROM (VALUES (CAST(1 AS decimal(10,2))), (2)) AS t(x);", "0.707107" },

        // The numeric functions compute their own result.
        { "ROUND", $"{Declared} SELECT CONVERT(varchar(60), ROUND(@d, 2)) AS v;", "1.2300" },
        { "ROUND truncating", $"{Declared} SELECT CONVERT(varchar(60), ROUND(@d, 2, 1)) AS v;", "1.2300" },
        { "FLOOR", $"{Declared} SELECT CONVERT(varchar(60), FLOOR(@d)) AS v;", "1" },
        { "CEILING", $"{Declared} SELECT CONVERT(varchar(60), CEILING(@d)) AS v;", "2" },
        { "SIGN of a decimal of places only", "DECLARE @x decimal(38,38) = 0.5; SELECT CONVERT(varchar(60), SIGN(@x)) AS v;", "1.0000000000000000000000000000000000000" },
        { "POWER of a decimal", $"{Declared} SELECT CONVERT(varchar(60), POWER(@d, 2)) AS v;", "1.5240" },
        { "NULLIF", $"{Declared} SELECT CONVERT(varchar(60), NULLIF(@d, @b)) AS v;", "1.2345" },

        // A procedure that sets NUMERIC_ROUNDABORT off rounds silently whatever its caller set.
        { "a procedure that sets NUMERIC_ROUNDABORT off", "EXEC dbo.obs_precision_set_off;", "1.23" },
    };

    [Theory]
    [MemberData(nameof(NotRounding))]
    public async Task These_round_or_truncate_whatever_numeric_roundabort_is(string construct, string batch, string value)
    {
        _ = construct;
        Assert.Equal(Kept(value), await UnderEach(batch));
    }

    public static TheoryData<string, string, string> Refusals { get; } = new()
    {
        // Into a column declared NOT NULL the NULL is refused (515), so a column's declaration still holds.
        { "an INSERT into a column declared NOT NULL", $"{Declared} INSERT dbo.obs_precision (k, kept) VALUES (1, @d); SELECT CONVERT(varchar(60), kept) AS v FROM dbo.obs_precision;", "1.23 1.23 1.23 1.23 8115 8115 8115 515" },
        { "an UPDATE of a column declared NOT NULL", $"{Declared} INSERT dbo.obs_precision VALUES (1, 0, 0); UPDATE dbo.obs_precision SET kept = @d; SELECT CONVERT(varchar(60), kept) AS v FROM dbo.obs_precision;", "1.23 1.23 1.23 1.23 8115 8115 8115 515" },

        // A procedure's argument, and an OUTPUT parameter copied back, fail to convert (8114) whatever the other two are.
        { "a procedure's argument of more places", $"{Declared} EXEC dbo.obs_precision_argument @p = @d;", "1.23 1.23 1.23 1.23 8114 8114 8114 8114" },
        { "a literal argument of more places", "EXEC dbo.obs_precision_argument @p = 1.2345;", "1.23 1.23 1.23 1.23 8114 8114 8114 8114" },
        { "an OUTPUT parameter copied back to fewer places", "DECLARE @e decimal(10,2); EXEC dbo.obs_precision_output @o = @e OUTPUT; SELECT CONVERT(varchar(60), @e) AS v;", "1.23 1.23 1.23 1.23 8114 8114 8114 8114" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_loss_of_precision_the_server_refuses_is_an_error_never_null(string construct, string batch, string expected)
    {
        _ = construct;
        Assert.Equal(expected, await UnderEach(batch));
    }
}

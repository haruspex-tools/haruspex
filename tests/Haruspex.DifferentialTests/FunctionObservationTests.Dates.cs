namespace Haruspex.DifferentialTests;

// The date functions that round a date, move it to an offset or test a string for one: their types for each date and time
// type, and when each is NULL. Inference's README.md, "Dates", gives the rules these rest on.
public partial class FunctionObservationTests
{
    /// <summary>One row of each date and time type.</summary>
    private const string Dates = """
        (SELECT CAST('2026-01-05' AS date) AS da, CAST('10:30:15.123' AS time(3)) AS tm, CAST('2026-01-05 10:30' AS datetime) AS dt,
                CAST('2026-01-05 10:30' AS smalldatetime) AS sdt, CAST('2026-01-05 10:30' AS datetime2(3)) AS d2,
                CAST('2026-01-05 10:30 +01:00' AS datetimeoffset(2)) AS dto, CAST(2 AS int) AS i, CAST(2 AS decimal(5,2)) AS d) AS t
        """;

    [Fact]
    public async Task Datetrunc_and_date_bucket_keep_the_dates_type_and_are_null_only_for_a_null_date_or_width()
    {
        Assert.Equal(
            "t1 date, t2 time(3), t3 datetime, t4 smalldatetime, t5 datetime2(3), t6 datetimeoffset(2), b1 date, b2 datetime2(3), b3 datetimeoffset(2), b4 time(3)",
            await Describe($"""
                SELECT DATETRUNC(month, da) AS t1, DATETRUNC(minute, tm) AS t2, DATETRUNC(day, dt) AS t3, DATETRUNC(hour, sdt) AS t4,
                       DATETRUNC(second, d2) AS t5, DATETRUNC(year, dto) AS t6, DATE_BUCKET(week, 1, da) AS b1, DATE_BUCKET(day, d, d2) AS b2,
                       DATE_BUCKET(hour, i, dto, dto) AS b3, DATE_BUCKET(minute, 15, tm) AS b4
                FROM {Dates}
                """));

        // A NULL origin is taken as the default origin, not NULL; an overflow or a part the type lacks is an error.
        Assert.Equal(
            "a,b,c: null,null,01/04/2026 00:00:00",
            await Select("DECLARE @d date = NULL, @n int = NULL; SELECT DATETRUNC(month, @d) AS a, DATE_BUCKET(day, @n, CAST('2026-01-05' AS date)) AS b, DATE_BUCKET(day, 2, CAST('2026-01-05' AS date), @d) AS c;"));
        Assert.Equal(" ! 9810", (await Run([], "SELECT DATETRUNC(hour, CAST('2026-01-01' AS date)) AS v;")).ToString());

        // The width is converted to int: a wider number may overflow, an error by default and NULL under the caller's options.
        const string Wide = "SELECT DATE_BUCKET(day, CAST(922337203685477 AS money), CAST('2026-01-05' AS datetime)) AS v;";
        Assert.Equal(" ! 237", (await Run([], Wide)).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Wide)).ToString());
    }

    [Fact]
    public async Task Switchoffset_and_todatetimeoffset_are_datetimeoffset_of_the_dates_fractional_digits()
    {
        // A date has none and gives 7; datetime gives 3 and smalldatetime 0. The offset is a string or a number of minutes.
        Assert.Equal(
            "s1 datetimeoffset(7), s2 datetimeoffset(3), s3 datetimeoffset(3), s4 datetimeoffset(0), s5 datetimeoffset(3), s6 datetimeoffset(2), o1 datetimeoffset(3), o2 datetimeoffset(7)",
            await Describe($"""
                SELECT SWITCHOFFSET(da, '+01:00') AS s1, SWITCHOFFSET(tm, 60) AS s2, SWITCHOFFSET(dt, '-05:00') AS s3, SWITCHOFFSET(sdt, i) AS s4,
                       SWITCHOFFSET(d2, '+01:00') AS s5, SWITCHOFFSET(dto, '+00:00') AS s6, TODATETIMEOFFSET(d2, '+01:00') AS o1, TODATETIMEOFFSET(da, i) AS o2
                FROM {Dates}
                """));
        Assert.Equal(
            "a,b,c: null,null,null",
            await Select("DECLARE @o datetimeoffset = NULL, @z varchar(6) = NULL; SELECT SWITCHOFFSET(@o, '+01:00') AS a, SWITCHOFFSET(SYSDATETIMEOFFSET(), @z) AS b, TODATETIMEOFFSET(CAST('2026-01-01' AS datetime2), @z) AS c;"));

        // A number of minutes is converted to smallint: a wider one may overflow, an error by default and NULL under the caller's
        // options. One that fits but is no offset is an error.
        const string Wide = "SELECT SWITCHOFFSET(CAST('2026-01-05 10:00 +00:00' AS datetimeoffset(3)), 2147483647) AS v;";
        Assert.Equal(" ! 220", (await Run([], Wide)).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Wide)).ToString());
        Assert.Equal(" ! 9812", (await Run([], "SELECT SWITCHOFFSET(CAST('2026-01-05 10:00 +00:00' AS datetimeoffset(3)), 900) AS v;")).ToString());

        // Past the range is an error, under the caller's options too.
        Assert.Equal(" ! 9813", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT SWITCHOFFSET(CAST('9999-12-31 23:00 +00:00' AS datetimeoffset), '+14:00') AS v;")).ToString());
    }

    [Fact]
    public async Task At_time_zone_is_datetimeoffset_of_the_dates_fractional_digits_and_null_only_for_a_null_date_or_zone()
    {
        // datetime gives 3 digits and smalldatetime 0; a chain keeps the first's. The zone is any string.
        Assert.Equal(
            "a datetimeoffset(3), b datetimeoffset(3), c datetimeoffset(0), d datetimeoffset(2), e datetimeoffset(3), f datetimeoffset(7), g datetimeoffset(3)",
            await Describe($"""
                SELECT d2 AT TIME ZONE 'UTC' AS a, dt AT TIME ZONE N'Pacific Standard Time' AS b, sdt AT TIME ZONE 'UTC' AS c,
                       dto AT TIME ZONE CAST('UTC' AS varchar(max)) AS d, (d2 AT TIME ZONE 'UTC') AT TIME ZONE 'GMT Standard Time' AS e,
                       CAST(d2 AS datetime2) AT TIME ZONE 'UTC' AS f, d2 AT TIME ZONE CAST('UTC' AS char(10)) AS g
                FROM {Dates}
                """));

        // NULL for a NULL date or zone. A zone the server doesn't know is an error. Moved past either end of the range, a
        // datetimeoffset is held at it, and a date without an offset is an error (9813), under the caller's options too: never NULL.
        Assert.Equal(
            "a,b: null,null",
            await Select("DECLARE @d datetime2 = NULL, @z sysname = NULL; SELECT @d AT TIME ZONE 'UTC' AS a, SYSDATETIME() AT TIME ZONE @z AS b;"));
        Assert.Equal(" ! 9820", (await Run([], "SELECT SYSDATETIME() AT TIME ZONE 'Nowhere' AS v;")).ToString());
        Assert.Equal(
            "a,b: 9999-12-31 23:59:59.9999999 +00:00,0001-01-01 00:00:00.0000000 +00:00",
            (await Run([], """
                SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;
                SELECT CONVERT(varchar(40), CAST('9999-12-31 23:00 +00:00' AS datetimeoffset) AT TIME ZONE 'Tokyo Standard Time', 121) AS a,
                       CONVERT(varchar(40), CAST('0001-01-01 00:00 +00:00' AS datetimeoffset) AT TIME ZONE 'Pacific Standard Time', 121) AS b;
                """)).ToString());
        const string Off = "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; ";
        Assert.Equal(" ! 9813", (await Run([], Off + "SELECT CAST('0001-01-01' AS datetime2) AT TIME ZONE 'Tokyo Standard Time' AS v;")).ToString());
        Assert.Equal(" ! 9813", (await Run([], Off + "SELECT CAST('9999-12-31 23:00' AS datetime2) AT TIME ZONE 'Pacific Standard Time' AS v;")).ToString());
        Assert.Equal(" ! 9813", (await Run([], Off + "SELECT CAST('9999-12-31 23:00' AS datetime) AT TIME ZONE 'Pacific Standard Time' AS v;")).ToString());

        // A date, a time, a string or a number is refused (8116), and so is a zone that is not a string.
        Assert.Equal(8116, (await Run([], $"SELECT da AT TIME ZONE 'UTC' AS v FROM {Dates};")).Error);
        Assert.Equal(8116, (await Run([], $"SELECT tm AT TIME ZONE 'UTC' AS v FROM {Dates};")).Error);
        Assert.Equal(8116, (await Run([], "SELECT '2026-01-05' AT TIME ZONE 'UTC' AS v;")).Error);
        Assert.Equal(8116, (await Run([], $"SELECT i AT TIME ZONE 'UTC' AS v FROM {Dates};")).Error);
        Assert.Equal(8116, (await Run([], $"SELECT d2 AT TIME ZONE i AS v FROM {Dates};")).Error);
    }

    [Fact]
    public async Task Isdate_is_an_int_never_null()
    {
        Assert.Equal("a int, b int, c int", await Describe($"SELECT ISDATE(CAST(d AS varchar(10))) AS a, ISDATE(dt) AS b, ISDATE(i) AS c FROM {Dates}"));
        Assert.Equal("a,b,c: 0,0,1", await Select("SELECT ISDATE(NULL) AS a, ISDATE('x') AS b, ISDATE('2026-01-05') AS c;"));
    }
}

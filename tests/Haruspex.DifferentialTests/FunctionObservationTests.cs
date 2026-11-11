using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server gives for the built-in functions haruspex types: the date functions, the string functions, <c>STRING_AGG</c>,
/// <c>JSON_VALUE</c>, the window functions, arithmetic and precedence over <c>float</c>, <c>real</c>, <c>money</c>,
/// <c>smallmoney</c> and the date and time types, the bare <c>NULL</c>, and <c>GENERATE_SERIES</c>. Types are read as the server
/// describes them (<c>sys.dm_exec_describe_first_result_set</c>); what can be NULL, and what is an error instead, from values.
/// The rules rest on these (Haruspex.SqlServer.Inference's README.md, "Built-in functions", "Window functions"). Observed on
/// SQL Server 2025 RTM-CU10.
/// </summary>
public class FunctionObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, setup, call, errorsAsMessages);

    /// <summary>
    /// Rows for the window functions: two groups, a nullable v that is NULL throughout group 1, and a never-null n.
    /// </summary>
    private const string Rows = """
        CREATE TABLE dbo.obs_w (id int NOT NULL PRIMARY KEY, g int NOT NULL, n int NOT NULL, v int NULL, s varchar(10) NOT NULL, ti tinyint NOT NULL);
        INSERT INTO dbo.obs_w VALUES (1, 1, 10, NULL, 'a', 1), (2, 1, 20, NULL, 'b', 2), (3, 2, 30, 5, 'c', 3);
        """;

    private async Task<string> Select(string query) => (await Run([Rows], query)).ToString();

    /// <summary>A row of one value of each type the expressions below read, so that a column's type is its own.</summary>
    private const string Row = """
        (SELECT CAST(1 AS float) AS f, CAST(1 AS real) AS r, CAST(1 AS int) AS i, CAST(1 AS bigint) AS bi, CAST(1 AS tinyint) AS ti,
                CAST(1 AS decimal(9,2)) AS d, CAST(1 AS money) AS m, CAST(1 AS smallmoney) AS sm, CAST(1 AS bit) AS bt,
                CAST('2026-01-01' AS date) AS da, CAST('2026-01-01' AS datetime) AS dt, CAST('2026-01-01' AS smalldatetime) AS sdt,
                CAST('2026-01-01' AS datetime2(3)) AS d2, CAST('2026-01-01' AS datetime2(1)) AS d21, CAST('10:00' AS time(2)) AS tm,
                CAST('2026-01-01' AS datetimeoffset(4)) AS dto,
                CAST('x' AS varchar(10)) AS s, CAST(N'x' AS nvarchar(20)) AS ns, CAST('a' AS char(5)) AS c, CAST(N'a' AS nchar(4)) AS nc,
                CAST('x' AS varchar(max)) AS vm, CAST(N'x' AS nvarchar(max)) AS nm) AS x
        """;

    /// <summary>The server's description of a query's columns: <c>name type</c>, comma-separated.</summary>
    private async Task<string> Describe(string select, string[]? setup = null)
    {
        var query = select.Replace("'", "''", StringComparison.Ordinal);
        var outcome = await Run(setup ?? [], $"SELECT name + ' ' + system_type_name AS c FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;");
        Assert.Null(outcome.Error);
        return outcome.Sets[0]["c: ".Length..].Replace(";", ", ", StringComparison.Ordinal);
    }

    // ---- date functions -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Date_functions_are_typed_by_their_kind_and_dateadd_by_its_dates_type()
    {
        // DATEDIFF int, DATEDIFF_BIG bigint, DATEPART, YEAR, MONTH and DAY int, DATENAME nvarchar(30), EOMONTH date; DATEADD the
        // date's own type, datetime for a string or an integer; each FROMPARTS its type, with the precision it is given.
        Assert.Equal(
            "a1 int, a2 bigint, a3 date, a4 datetime, a5 datetime2(3), a6 smalldatetime, a7 datetimeoffset(4), a8 time(2), a9 datetime, a10 datetime, "
            + "b1 int, b2 int, b3 int, b4 int, b5 nvarchar(30), b6 date, b7 date, b8 date, "
            + "c1 date, c2 datetime2(3), c3 datetime, c4 smalldatetime, c5 time(2), c6 datetimeoffset(3)",
            await Describe($"""
                SELECT DATEDIFF(day, da, dt) AS a1, DATEDIFF_BIG(second, s, d2) AS a2, DATEADD(day, 1, da) AS a3, DATEADD(day, 1, dt) AS a4,
                       DATEADD(day, 1.5, d2) AS a5, DATEADD(day, 1, sdt) AS a6, DATEADD(hour, i, dto) AS a7, DATEADD(hour, 1, tm) AS a8,
                       DATEADD(day, 1, s) AS a9, DATEADD(day, 1, i) AS a10,
                       DATEPART(year, da) AS b1, YEAR(dt) AS b2, MONTH(s) AS b3, DAY(d2) AS b4, DATENAME(month, da) AS b5, EOMONTH(da) AS b6,
                       EOMONTH(dt, 1) AS b7, EOMONTH(s) AS b8,
                       DATEFROMPARTS(2026, 1, 1) AS c1, DATETIME2FROMPARTS(2026, 1, 1, 1, 1, 1, 1, 3) AS c2, DATETIMEFROMPARTS(2026, 1, 1, 1, 1, 1, 1) AS c3,
                       SMALLDATETIMEFROMPARTS(2026, 1, 1, 1, 1) AS c4, TIMEFROMPARTS(1, 1, 1, 1, 2) AS c5, DATETIMEOFFSETFROMPARTS(2026, 1, 1, 1, 1, 1, 1, 1, 0, 3) AS c6
                FROM {Row}
                """));
    }

    [Fact]
    public async Task The_current_date_and_time_functions_are_never_null()
    {
        Assert.Equal(
            "a datetime2(7), b datetime, c datetime, d datetime2(7), e datetimeoffset(7), f datetime",
            await Describe("SELECT SYSDATETIME() AS a, GETDATE() AS b, GETUTCDATE() AS c, SYSUTCDATETIME() AS d, SYSDATETIMEOFFSET() AS e, CURRENT_TIMESTAMP AS f"));
        Assert.Equal(
            "a,b,c,d,e,f: 1,1,1,1,1,1",
            await Select("SELECT IIF(SYSDATETIME() IS NULL, 0, 1) AS a, IIF(GETDATE() IS NULL, 0, 1) AS b, IIF(GETUTCDATE() IS NULL, 0, 1) AS c, IIF(SYSUTCDATETIME() IS NULL, 0, 1) AS d, IIF(SYSDATETIMEOFFSET() IS NULL, 0, 1) AS e, IIF(CURRENT_TIMESTAMP IS NULL, 0, 1) AS f"));
    }

    [Fact]
    public async Task A_date_function_is_null_when_an_argument_is_but_eomonth_ignores_a_null_offset()
    {
        // Each is NULL for a NULL date, number or part; EOMONTH's month offset, NULL, is taken as 0.
        Assert.Equal(
            "a,b,c,d,e,f,g,h,i,j,k,l,m: null,null,null,null,null,null,null,null,null,null,null,null,2026-01-31",
            await Select("""
                DECLARE @d date = NULL, @n int = NULL;
                SELECT DATEDIFF(day, @d, GETDATE()) AS a, DATEDIFF_BIG(day, GETDATE(), @d) AS b, DATEADD(day, @n, GETDATE()) AS c, DATEADD(day, 1, @d) AS d,
                       DATEPART(year, @d) AS e, YEAR(@d) AS f, DATENAME(month, @d) AS g, EOMONTH(@d) AS h, DATEFROMPARTS(2026, @n, 1) AS i,
                       DATETIME2FROMPARTS(2026, 1, 1, 1, 1, 1, @n, 3) AS j, DATETIMEFROMPARTS(2026, 1, 1, 1, 1, 1, @n) AS k,
                       TIMEFROMPARTS(1, 1, 1, @n, 2) AS l, CONVERT(varchar(10), EOMONTH(CAST('2026-01-15' AS date), @n), 23) AS m;
                """));
    }

    [Fact]
    public async Task A_date_overflow_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off()
    {
        // Unlike arithmetic (8115), DATEADD (517), DATEDIFF (535), EOMONTH (517) and a FROMPARTS out of range (289) are errors
        // whatever the session's options, so none of them is a reason to be NULL.
        const string Off = "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;";
        Assert.Equal(" ! 517", (await Run([], $"{Off} SELECT DATEADD(year, 100, CAST('9999-01-01' AS date)) AS v;")).ToString());
        Assert.Equal(" ! 535", (await Run([], $"{Off} SELECT DATEDIFF(second, CAST('0001-01-01' AS datetime2), CAST('9999-01-01' AS datetime2)) AS v;")).ToString());
        Assert.Equal(" ! 517", (await Run([], $"{Off} SELECT EOMONTH(CAST('9999-12-01' AS date), 1) AS v;")).ToString());
        Assert.Equal(" ! 289", (await Run([], $"{Off} SELECT DATEFROMPARTS(2026, 2, 30) AS v;")).ToString());
    }

    [Fact]
    public async Task Dateadd_with_the_null_literal_as_its_number_does_not_compile_and_percentile_cont_takes_a_bit()
    {
        // DATEADD(day, NULL, d) is refused (8116), where a NULL variable is NULL; PERCENTILE_CONT over a bit is
        // a float.
        Assert.Equal(" ! 8116", (await Run([], "SELECT DATEADD(day, NULL, CAST('2026-01-01' AS date)) AS v;")).ToString());
        Assert.Equal("v float", await Describe("SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY CAST(n AS bit)) OVER () AS v FROM dbo.obs_w", [Rows]));
    }

    // ---- string functions ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Trimming_and_case_keep_the_strings_length_and_make_a_fixed_length_string_varying()
    {
        // LTRIM, RTRIM, TRIM, UPPER and LOWER: varchar or nvarchar of the string's length (max stays max); char becomes varchar.
        // With characters to trim, nvarchar if either is.
        Assert.Equal(
            "a1 varchar(10), a2 varchar(5), a3 nvarchar(4), a4 varchar(max), a5 varchar(5), a6 nvarchar(4), a7 nvarchar(max), a8 varchar(10), "
            + "a9 nvarchar(10), a10 nvarchar(20), a11 nvarchar(10), a12 varchar(3)",
            await Describe($"""
                SELECT LTRIM(s) AS a1, RTRIM(c) AS a2, TRIM(nc) AS a3, LTRIM(vm) AS a4, UPPER(c) AS a5, LOWER(nc) AS a6, UPPER(nm) AS a7,
                       TRIM('x' FROM s) AS a8, TRIM(N'x' FROM s) AS a9, TRIM(s FROM ns) AS a10, LTRIM(s, N'x') AS a11, UPPER('abc') AS a12
                FROM {Row}
                """));
    }

    [Fact]
    public async Task Left_right_and_substring_are_as_long_as_a_literal_length_allows()
    {
        // The string's length, or an integer literal's when it is shorter (at least 1); max stays max; char becomes varchar. A
        // length that is a constant expression is folded (1 + 1 gives 2), one with a variable is not; the start does not count.
        Assert.Equal(
            "b1 varchar(3), b2 varchar(2), b3 nvarchar(4), b4 varchar(3), b5 varchar(10), b6 nvarchar(max), b7 varchar(max), b8 varchar(1), "
            + "b9 varchar(10), b10 varchar(2), b11 varchar(3), b12 varchar(10)",
            await Describe($"""
                SELECT LEFT(s, 3) AS b1, LEFT(c, 2) AS b2, RIGHT(nc, i) AS b3, SUBSTRING(s, 9, 3) AS b4, SUBSTRING(s, 1, 20) AS b5, SUBSTRING(nm, 1, 2) AS b6,
                       LEFT(vm, 5) AS b7, LEFT(s, 0) AS b8, LEFT(s, 2.7) AS b9, LEFT(s, 1 + 1) AS b10, LEFT('abc', 10) AS b11, SUBSTRING(s, 1, i + 1) AS b12
                FROM {Row}
                """));

        // Past the end is '', not NULL; a NULL argument is NULL; a negative length is an error.
        Assert.Equal(
            "a,b,c,d,e,f: ,,,null,null,null",
            await Select("DECLARE @n int = NULL; SELECT SUBSTRING('abc', 5, 2) AS a, LEFT('abc', 0) AS b, SUBSTRING('abc', 0, 1) AS c, LEFT('abc', @n) AS d, SUBSTRING('abc', @n, 1) AS e, RIGHT(CAST(NULL AS varchar(3)), 1) AS f;"));
        Assert.Equal(" ! 536", (await Run([], "SELECT LEFT('abc', -1) AS v;")).ToString());
    }

    [Fact]
    public async Task Len_and_datalength_are_int_or_bigint_over_max_and_len_ignores_trailing_spaces()
    {
        Assert.Equal(
            "c1 int, c2 bigint, c3 bigint, c4 int, c5 bigint, c6 int, c7 int, c8 int",
            await Describe($"SELECT LEN(s) AS c1, LEN(vm) AS c2, LEN(nm) AS c3, DATALENGTH(s) AS c4, DATALENGTH(nm) AS c5, DATALENGTH(i) AS c6, LEN(c) AS c7, DATALENGTH(d2) AS c8 FROM {Row}"));
        Assert.Equal(
            "a,b,c,d,e: 2,4,0,null,null",
            await Select("SELECT LEN('ab  ') AS a, DATALENGTH('ab  ') AS b, LEN('  ') AS c, LEN(CAST(NULL AS varchar(3))) AS d, DATALENGTH(CAST(NULL AS int)) AS e;"));
    }

    [Fact]
    public async Task Replace_widens_to_8000_bytes_unless_its_string_is_max()
    {
        // varchar(8000) or nvarchar(4000), nvarchar if any argument is, max only when the string searched is.
        Assert.Equal(
            "d1 varchar(8000), d2 nvarchar(4000), d3 varchar(max), d4 nvarchar(4000), d5 varchar(8000), d6 nvarchar(4000), d7 varchar(8000), d8 nvarchar(max)",
            await Describe($"""
                SELECT REPLACE(s, 'a', 'b') AS d1, REPLACE(ns, 'a', 'b') AS d2, REPLACE(vm, 'a', 'b') AS d3, REPLACE(s, N'a', 'b') AS d4, REPLACE(c, 'a', 'bb') AS d5,
                       REPLACE(s, nm, 'b') AS d6, REPLACE(s, 'a', vm) AS d7, REPLACE(nm, 'a', 'b') AS d8
                FROM {Row}
                """));
        Assert.Equal(
            "a,b,c: null,null,abc",
            await Select("DECLARE @x varchar(1) = NULL; SELECT REPLACE('abc', @x, 'z') AS a, REPLACE('abc', 'a', @x) AS b, REPLACE('abc', '', 'z') AS c;"));
    }

    [Fact]
    public async Task String_escape_is_nvarchar_max_and_formatmessage_nvarchar_4000_writing_a_null_argument_as_null_in_brackets()
    {
        Assert.Equal(
            "e1 nvarchar(max), e2 nvarchar(max), e3 nvarchar(4000), e4 nvarchar(4000)",
            await Describe($"SELECT STRING_ESCAPE(s, 'json') AS e1, STRING_ESCAPE(nm, 'json') AS e2, FORMATMESSAGE('%s', s) AS e3, FORMATMESSAGE(ns, i) AS e4 FROM {Row}"));

        // A NULL argument is written "(null)", a missing one too; only a NULL format makes it NULL. A bad conversion gives text.
        Assert.Equal(
            "a,b,c,d: (null)|(null),a (null),null,null",
            await Select("DECLARE @x varchar(1) = NULL; SELECT FORMATMESSAGE('%s|%d', @x, NULL) AS a, FORMATMESSAGE('%s %s', 'a') AS b, FORMATMESSAGE(@x, 1) AS c, STRING_ESCAPE(@x, 'json') AS d;"));
        Assert.StartsWith("v: Error: 50000", await Select("SELECT FORMATMESSAGE('%d', 'abc') AS v;"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_value_is_nvarchar_4000_and_null_for_a_json_null_a_missing_path_an_object_or_a_long_string()
    {
        Assert.Equal("g1 nvarchar(4000), g2 nvarchar(4000), g3 nvarchar(4000)", await Describe($"SELECT JSON_VALUE(ns, '$.a') AS g1, JSON_VALUE(vm, '$.a') AS g2, JSON_VALUE(nm, 'strict $.a') AS g3 FROM {Row}"));

        // Lax: NULL for a JSON null, a missing property, an object, and a string longer than 4000; strict NULL for a JSON null.
        Assert.Equal(
            "a,b,c,d,e: null,null,null,null,null",
            await Select("""
                SELECT JSON_VALUE(N'{"a":null}', '$.a') AS a, JSON_VALUE(N'{}', '$.a') AS b, JSON_VALUE(N'{"a":{"b":1}}', '$.a') AS c,
                       JSON_VALUE(N'{"a":"' + REPLICATE(CAST(N'x' AS nvarchar(max)), 5000) + N'"}', '$.a') AS d, JSON_VALUE(N'{"a":null}', 'strict $.a') AS e;
                """));
        Assert.Equal(" ! 13609", (await Run([], "SELECT JSON_VALUE(N'not json', '$.a') AS v;")).ToString());
    }

    // ---- STRING_AGG -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task String_agg_is_8000_bytes_unless_max_and_null_over_no_rows_or_only_nulls()
    {
        // varchar(8000) for a varchar or char, nvarchar(4000) for an nvarchar or anything not a string, max over max.
        Assert.Equal(
            "a1 varchar(8000), a2 nvarchar(4000), a3 varchar(max), a4 varchar(8000), a5 nvarchar(4000), a6 nvarchar(max), a7 nvarchar(4000)",
            await Describe($"SELECT STRING_AGG(s, ',') AS a1, STRING_AGG(ns, ',') AS a2, STRING_AGG(vm, ',') AS a3, STRING_AGG(c, '-') AS a4, STRING_AGG(i, ',') AS a5, STRING_AGG(nm, N',') AS a6, STRING_AGG(da, ',') AS a7 FROM {Row}"));

        // NULLs are skipped: a group of only NULLs is NULL, and no rows without GROUP BY is NULL. A NULL separator is not.
        Assert.Equal("g,a,b: 1,null,ab | e: null", await Select("""
            SELECT g, STRING_AGG(CAST(v AS varchar(5)), ',') AS a, STRING_AGG(s, NULL) AS b FROM dbo.obs_w WHERE g = 1 GROUP BY g;
            SELECT STRING_AGG(s, ',') AS e FROM dbo.obs_w WHERE id = 0;
            """));
    }

    [Fact]
    public async Task String_agg_past_its_limit_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off() => Assert.Equal(" ! 9829", (await Run([Rows], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT STRING_AGG(CAST(REPLICATE('x', 5000) AS varchar(8000)), ',') AS v FROM dbo.obs_w;")).ToString());

    // ---- window functions ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Window_functions_are_typed_by_their_kind_or_their_argument()
    {
        // Ranking bigint; COUNT int and COUNT_BIG bigint; SUM, MIN and MAX as the aggregates; LAG, LEAD, FIRST_VALUE, LAST_VALUE and
        // PERCENTILE_DISC their argument's type, whatever the default; PERCENTILE_CONT float.
        Assert.Equal(
            "b1 bigint, b2 bigint, b3 bigint, b4 bigint, b5 int, b6 bigint, b7 int, b8 int, b9 varchar(10), "
            + "c1 int, c2 varchar(10), c3 tinyint, d1 int, d2 varchar(10), e1 float, e2 int, e3 varchar(10)",
            await Describe("""
                SELECT ROW_NUMBER() OVER (ORDER BY id) AS b1, RANK() OVER (ORDER BY g) AS b2, DENSE_RANK() OVER (ORDER BY g) AS b3, NTILE(2) OVER (ORDER BY id) AS b4,
                       COUNT(*) OVER () AS b5, COUNT_BIG(v) OVER () AS b6, SUM(ti) OVER (PARTITION BY g) AS b7, MAX(v) OVER () AS b8, MIN(s) OVER () AS b9,
                       LAG(n) OVER (ORDER BY id) AS c1, LEAD(s, 1, 'a longer default') OVER (ORDER BY id) AS c2, LAG(ti, 1, 1000) OVER (ORDER BY id) AS c3,
                       FIRST_VALUE(n) OVER (ORDER BY id) AS d1, LAST_VALUE(s) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS d2,
                       PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY n) OVER () AS e1, PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v) OVER () AS e2,
                       PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY s) OVER (PARTITION BY g) AS e3
                FROM dbo.obs_w
                """, [Rows]));
    }

    [Fact]
    public async Task A_frame_that_may_leave_out_the_current_row_can_be_empty_and_give_null_over_a_never_null_column()
    {
        // ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING has nothing before the first row; 1 FOLLOWING AND 2 FOLLOWING nothing after the
        // last. SUM, FIRST_VALUE and MAX are then NULL, COUNT 0. The default frame (and any holding the current row) is never empty.
        Assert.Equal(
            "id,s,f,m,c,l,fv: 1,null,20,30,2,10,10;2,10,30,30,1,20,10;3,30,null,null,0,30,10",
            await Select("""
                SELECT id, SUM(n) OVER (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING) AS s,
                       FIRST_VALUE(n) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND 2 FOLLOWING) AS f,
                       MAX(n) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING) AS m,
                       COUNT(n) OVER (ORDER BY id ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING) AS c,
                       LAST_VALUE(n) OVER (ORDER BY id) AS l, FIRST_VALUE(n) OVER (ORDER BY id) AS fv
                FROM dbo.obs_w ORDER BY id;
                """));
    }

    [Fact]
    public async Task Lag_and_lead_are_null_past_the_partitions_edge_unless_a_default_is_given_which_is_converted_to_the_columns_type()
    {
        // Past the edge: NULL, or the default converted to the column's type (a long string cut, 1.9 to 1); a NULL offset is NULL;
        // offset 0 is the row itself.
        Assert.Equal(
            "id,a,b,c,d,e,f: 1,null,a longer d,1,null,null,10;2,10,a,1,10,null,20;3,null,b,2,20,null,30",
            await Select("""
                DECLARE @k int = NULL;
                SELECT id, LAG(n) OVER (PARTITION BY g ORDER BY id) AS a, LAG(s, 1, 'a longer default') OVER (ORDER BY id) AS b,
                       LAG(ti, 1, 1.9) OVER (ORDER BY id) AS c, LAG(n, 1, NULL) OVER (ORDER BY id) AS d, LAG(n, @k) OVER (ORDER BY id) AS e,
                       LAG(n, 0) OVER (ORDER BY id) AS f
                FROM dbo.obs_w ORDER BY id;
                """));

        // A default that overflows the column's type is an error, and NULL under the caller's ANSI_WARNINGS and ARITHABORT off.
        Assert.Equal(" ! 220", (await Run([Rows], "SELECT LAG(ti, 1, 1000) OVER (ORDER BY id) AS v FROM dbo.obs_w;")).ToString());
        Assert.Equal("v: null;1;2", (await Run([Rows], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT LAG(ti, 1, 1000) OVER (ORDER BY id) AS v FROM dbo.obs_w ORDER BY id;")).ToString());
    }

    [Fact]
    public async Task Ranking_is_never_null_and_percentiles_are_null_only_over_all_null_input()
    {
        Assert.Equal(
            "id,r,k,d,t,c,cb,p,q: 1,1,1,1,1,3,0,null,null;2,2,1,1,1,3,0,null,null;3,3,3,2,2,3,1,5,5",
            await Select("""
                SELECT id, ROW_NUMBER() OVER (ORDER BY id) AS r, RANK() OVER (ORDER BY g) AS k, DENSE_RANK() OVER (ORDER BY g) AS d, NTILE(2) OVER (ORDER BY g) AS t,
                       COUNT(*) OVER () AS c, COUNT_BIG(v) OVER (PARTITION BY g) AS cb,
                       PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY v) OVER (PARTITION BY g) AS p, PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY v) OVER (PARTITION BY g) AS q
                FROM dbo.obs_w ORDER BY id;
                """));

        // NTILE of NULL, or of 0, is an error, not NULL.
        Assert.Equal(" ! 4116", (await Run([Rows], "DECLARE @k int = NULL; SELECT NTILE(@k) OVER (ORDER BY id) AS v FROM dbo.obs_w;")).ToString());
    }

    // ---- arithmetic and precedence over float, real, money and the date and time types ------------------------------------

    [Fact]
    public async Task Arithmetic_over_float_real_money_and_dates_follows_data_type_precedence()
    {
        // float beats everything numeric, real all but float; money with an integer or money is money, smallmoney with an integer
        // (even bigint) smallmoney; money with a decimal is decimal arithmetic on decimal(19,4) (smallmoney decimal(10,4)), numeric
        // beside a numeric literal. datetime and smalldatetime add and subtract numbers and each other: datetime if either is;
        // datetime2, date, time and datetimeoffset do not (an error). Negating a tinyint gives smallint.
        Assert.Equal(
            "a1 float, a2 float, a3 real, a4 real, a5 float, a6 real, a7 float, "
            + "b1 money, b2 money, b3 money, b4 smallmoney, b5 smallmoney, b6 money, b7 decimal(20,4), b8 decimal(29,6), b9 numeric(20,4), b10 decimal(12,4), b11 decimal(31,14), b12 money, "
            + "c1 datetime, c2 datetime, c3 smalldatetime, c4 smalldatetime, c5 datetime, c6 datetime, c7 datetime, c8 smalldatetime, "
            + "u1 float, u2 real, u3 money, u4 smallmoney, u5 smallint, u6 float, u7 money",
            await Describe($"""
                SELECT f + i AS a1, f * d AS a2, r + i AS a3, r + m AS a4, r * f AS a5, r + d AS a6, i / f AS a7,
                       m + m AS b1, m * i AS b2, m / bi AS b3, sm + bi AS b4, sm * sm AS b5, m + sm AS b6, m + d AS b7, m * d AS b8, m + 1.5 AS b9,
                       sm + d AS b10, m / d AS b11, i - m AS b12,
                       dt + 1 AS c1, dt - dt AS c2, sdt + f AS c3, sdt - sdt AS c4, dt + sdt AS c5, 1 + dt AS c6, sdt - dt AS c7, sdt + m AS c8,
                       -f AS u1, -r AS u2, -m AS u3, -sm AS u4, -ti AS u5, -1.5e0 AS u6, -$2 AS u7
                FROM {Row}
                """));
    }

    [Fact]
    public async Task Arithmetic_over_float_money_and_dates_overflows_to_an_error_or_to_null_under_the_callers_ansi_warnings_and_arithabort_off()
    {
        // Each can overflow (or divide by zero), which is an error by default and NULL under the caller's options; negating a float
        // cannot, negating money's least value can.
        const string Values = "DECLARE @f float = 1e308, @z float = 0, @m money = 922337203685477, @sm smallmoney = -214748.3648, @dt datetime = '9999-12-31', @sdt smalldatetime = '2079-06-06', @r real = 3e38;";
        Assert.Equal(
            "a,b,c,d,e,f,g: null,null,null,null,null,null,null",
            (await Run([], $"SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; {Values} SELECT @f * 10 AS a, 1 / @z AS b, @m * 100 AS c, -@sm AS d, @dt + 2 AS e, @sdt + 2 AS f, @r * 10 AS g; SET ANSI_WARNINGS ON;")).ToString().Split(" ~ ")[0]);
        Assert.Equal(" ! 8115", (await Run([], $"{Values} SELECT @f * 10 AS a;")).ToString());
        Assert.Equal(" ! 8115", (await Run([], $"{Values} SELECT @dt + 2 AS a;")).ToString());
        Assert.Equal(" ! 8115", (await Run([], $"{Values} SELECT -@sm AS a;")).ToString());
    }

    [Fact]
    public async Task Coalesce_over_float_money_and_dates_follows_data_type_precedence()
    {
        // float, then real; money with integers money, smallmoney smallmoney; money with a decimal a decimal as for arithmetic's
        // operands; datetime2 with datetime of at least precision 3, with smalldatetime or date its own; datetime with smalldatetime
        // datetime.
        Assert.Equal(
            "a1 float, a2 float, a3 real, a4 real, a5 money, a6 smallmoney, a7 money, a8 decimal(19,4), a9 numeric(19,4), a10 decimal(11,4), a11 float, "
            + "c1 datetime2(3), c2 datetime2(3), c3 datetime2(3), c4 datetime2(1), c5 datetime2(1), c6 datetime, c7 datetime2(3)",
            await Describe($"""
                SELECT COALESCE(f, i) AS a1, COALESCE(r, f) AS a2, COALESCE(r, i) AS a3, COALESCE(r, m) AS a4, COALESCE(m, i) AS a5, COALESCE(sm, ti) AS a6,
                       COALESCE(m, sm) AS a7, COALESCE(m, d) AS a8, COALESCE(m, 1.5) AS a9, COALESCE(sm, d) AS a10, COALESCE(m, f) AS a11,
                       COALESCE(d21, dt) AS c1, COALESCE(dt, d2) AS c2, COALESCE(d21, d2) AS c3, COALESCE(d21, sdt) AS c4, COALESCE(d21, da) AS c5,
                       COALESCE(sdt, dt) AS c6, COALESCE(sdt, d2) AS c7
                FROM {Row}
                """));
    }

    [Fact]
    public async Task Coalesce_converting_to_a_narrower_range_overflows_to_null_under_the_callers_ansi_warnings_and_arithabort_off()
    {
        // date to datetime, bigint to money, int to smallmoney: NULL under the caller's options, so such a COALESCE is not typed.
        Assert.Equal(
            "a,b,c: null,null,null",
            (await Run([], """
                SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;
                DECLARE @d date = '1000-01-01', @dt datetime = NULL, @bi bigint = 9223372036854775807, @m money = NULL, @i int = 2000000000, @sm smallmoney = NULL;
                SELECT COALESCE(@dt, @d) AS a, COALESCE(@m, @bi) AS b, COALESCE(@sm, @i) AS c;
                SET ANSI_WARNINGS ON;
                """)).ToString().Split(" ~ ")[0]);
    }

    // ---- NULL and GENERATE_SERIES ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_bare_null_is_an_int()
    {
        Assert.Equal("n int", await Describe("SELECT NULL AS n"));
        Assert.Equal("n: null", (await Run([], "SELECT NULL AS n;")).ToString());
    }

    [Fact]
    public async Task Generate_series_gives_value_of_its_arguments_type_never_null_and_no_rows_for_a_null_argument()
    {
        // Every argument of one integer type, or decimals (typed as COALESCE over them); anything else is an error.
        Assert.Equal("value int", await Describe("SELECT value FROM GENERATE_SERIES(1, 10, 2)"));
        Assert.Equal("value tinyint", await Describe("DECLARE @a tinyint = 1, @b tinyint = 5; SELECT value FROM GENERATE_SERIES(@a, @b)"));
        Assert.Equal("value numeric(6,2)", await Describe("DECLARE @a numeric(5,1) = 1; SELECT value FROM GENERATE_SERIES(@a, 3.25)"));
        Assert.Equal("value numeric(2,1)", await Describe("SELECT value FROM GENERATE_SERIES(0.0, 1.0, 0.1)"));
        Assert.Equal(" ! 5373", (await Run([], "DECLARE @a smallint = 1; SELECT value FROM GENERATE_SERIES(@a, 10);")).ToString());

        // decimal and numeric do not mix either, though COALESCE would take them.
        Assert.Equal(" ! 5373", (await Run([], "DECLARE @a decimal(10,2) = 1; SELECT value FROM GENERATE_SERIES(@a, 100.5);")).ToString());

        // A step the wrong way, or a NULL argument, gives no rows; never a NULL value.
        Assert.Equal(
            "n: 0 | n: 0 | n: 0 | n: 10",
            (await Run([], """
                DECLARE @s int = NULL;
                SELECT COUNT(*) AS n FROM GENERATE_SERIES(1, 10, -1);
                SELECT COUNT(*) AS n FROM GENERATE_SERIES(1, CAST(NULL AS int));
                SELECT COUNT(*) AS n FROM GENERATE_SERIES(1, 10, @s);
                SELECT COUNT(*) AS n FROM GENERATE_SERIES(10, 1);
                """)).ToString());
    }
}

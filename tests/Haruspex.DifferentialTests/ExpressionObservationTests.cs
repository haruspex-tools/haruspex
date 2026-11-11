using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server gives for the common expressions haruspex types: <c>COALESCE</c>, <c>ISNULL</c>, <c>CAST</c> and
/// <c>CONVERT</c> (with a style too), <c>TRY_CAST</c>, <c>CONCAT</c>, <c>CONCAT_WS</c>, string <c>+</c>, decimal arithmetic,
/// <c>SUM</c>, and the literals they are built from. Types are read as the server describes them
/// (<c>sys.dm_exec_describe_first_result_set</c>); what can be NULL, and what is cut short, is read from values. The rules
/// rest on these (Haruspex.SqlServer.Inference's README.md, "Expressions"). Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class ExpressionObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, [], call, errorsAsMessages);

    /// <summary>A row of every type the expressions below read, each value given, so that a column's type is its own.</summary>
    private const string Row = """
        (SELECT CAST('a' AS varchar(5)) AS a, CAST(N'b' AS nvarchar(10)) AS b, CAST('c' AS char(4)) AS c, CAST(N'n' AS nchar(3)) AS nc,
                CAST('m' AS varchar(max)) AS vm, CAST(N'm' AS nvarchar(max)) AS nm, CAST('v' AS varchar(8000)) AS v8, CAST(N'w' AS nvarchar(4000)) AS n4,
                CAST(1 AS decimal(10,2)) AS d, CAST(1 AS decimal(19,4)) AS d2, CAST(1 AS numeric(10,2)) AS nd, CAST(1 AS money) AS m,
                CAST(1 AS int) AS i, CAST(1 AS smallint) AS si, CAST(1 AS tinyint) AS ti, CAST(1 AS bigint) AS bi, CAST(1 AS bit) AS bt, CAST(1 AS float) AS f,
                CAST('2026-11-10' AS date) AS dd, CAST('2026-11-10' AS datetime2(3)) AS dt) AS x
        """;

    /// <summary>The server's description of a query's columns: <c>name type</c>, comma-separated.</summary>
    private Task<string> Describe(string select) => FirstResultSet.Describe(server, select);

    // ---- literals ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_literals_type_is_its_own_digits_or_characters()
    {
        // A decimal literal is numeric of its own digits: the scale counts the digits after the point, trailing zeros too, and
        // the precision adds the digits before it, less leading zeros. An integer literal beyond int is numeric too. A string
        // literal is varchar or nvarchar of its length, at least 1. Negated, 2147483648 is int's least value, an int.
        Assert.Equal(
            "a numeric(2,1), b numeric(1,1), c numeric(4,3), d numeric(5,2), e numeric(2,2), f numeric(3,2), g numeric(10,0), h int, i varchar(3), j nvarchar(2), k varchar(1), "
            + "l int, m int, n numeric(10,0), o numeric(38,0), p numeric(38,0)",
            await Describe("SELECT 2.5 AS a, 0.5 AS b, 1.234 AS c, 100.00 AS d, 0.05 AS e, 007.10 AS f, 2147483648 AS g, 2147483647 AS h, 'abc' AS i, N'ab' AS j, '' AS k, -2147483648 AS l, -(2147483648) AS m, -2147483649 AS n, -(-2147483648) AS o, - -2147483648 AS p"));
    }

    // ---- COALESCE and ISNULL -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Coalesce_takes_the_type_of_highest_precedence_and_isnull_the_first_arguments()
    {
        // COALESCE: strings are nvarchar if any argument is, varchar if any is varying, of the longest length (max if any is);
        // exact numbers are a decimal wide enough for every argument (an int variable or column counts as decimal(10,0), an
        // integer literal as decimal of its own digits, smallint (5,0), bigint (19,0)), named as its first decimal argument is
        // (decimal or numeric); integers the widest; a date with a datetime2 is the datetime2. ISNULL is its first argument's
        // type, or the second's when the first is the NULL literal.
        Assert.Equal(
            "e1 varchar(5), e2 varchar(16), e3 nvarchar(10), e4 decimal(10,2), e5 decimal(12,2), e6 decimal(11,2), e7 decimal(19,4), e8 decimal(21,2), "
            + "e9 decimal(10,2), e10 numeric(10,2), e11 numeric(2,1), e12 decimal(10,2), e13 smallint, e14 bigint, e15 varchar(5), e16 nvarchar(5), "
            + "e17 varchar(max), e18 datetime2(3), e19 varchar(5), e20 int, e21 tinyint, e22 numeric(10,2), e23 decimal(10,2), e24 int",
            await Describe($"""
                DECLARE @iv int = 1;
                SELECT ISNULL(a, 'a longer literal') AS e1, COALESCE(a, 'a longer literal') AS e2, COALESCE(a, b) AS e3, COALESCE(d, 12345) AS e4,
                       COALESCE(d, @iv) AS e5, COALESCE(d, 123456789) AS e6, COALESCE(i, d2) AS e7, COALESCE(d, bi) AS e8, COALESCE(d, nd) AS e9,
                       COALESCE(nd, d) AS e10, COALESCE(1, 2.5) AS e11, COALESCE(d, si) AS e12, COALESCE(ti, si) AS e13, COALESCE(i, bi) AS e14,
                       COALESCE(c, a) AS e15, COALESCE(nc, a) AS e16, COALESCE(vm, a) AS e17, COALESCE(dt, dd) AS e18, COALESCE(a, NULL) AS e19,
                       ISNULL(NULL, 1) AS e20, ISNULL(ti, 1000) AS e21, ISNULL(nd, 0) AS e22, ISNULL(d, 1.234) AS e23, COALESCE(ti, 1) AS e24
                FROM {Row}
                """));
    }

    [Fact]
    public async Task Isnull_converts_its_second_argument_to_the_firsts_type_cutting_a_string_short()
    {
        // ISNULL's value is converted to its first argument's type, so a longer string is cut short without an error, where
        // COALESCE keeps it whole. Each is NULL only when every argument is.
        Assert.Equal(
            "i,c,n,z: a lon,a longer literal,null,0",
            (await Run("DECLARE @short varchar(5) = NULL; SELECT ISNULL(@short, 'a longer literal') AS i, COALESCE(@short, 'a longer literal') AS c, COALESCE(@short, NULL) AS n, COALESCE(@short, NULL, 0) AS z;")).ToString());
    }

    [Fact]
    public async Task Coalesce_reads_a_subquery_twice_so_it_can_be_null_where_isnull_cannot()
    {
        // COALESCE(x, y) is CASE WHEN x IS NOT NULL THEN x ELSE y END, which evaluates a subquery x twice: the test may see a
        // value and the result NULL. A subquery that is NULL half the time, read 2,000 times, makes COALESCE NULL about a
        // quarter of the time though its last argument is 0. ISNULL reads it once and is never NULL.
        var outcome = await Run("""
            SELECT SUM(CASE WHEN c IS NULL THEN 1 ELSE 0 END) AS coalesce_nulls, SUM(CASE WHEN i IS NULL THEN 1 ELSE 0 END) AS isnull_nulls
            FROM (SELECT COALESCE((SELECT CASE WHEN CRYPT_GEN_RANDOM(1) < 0x80 THEN 1 END), 0) AS c,
                         ISNULL((SELECT CASE WHEN CRYPT_GEN_RANDOM(1) < 0x80 THEN 1 END), 0) AS i
                  FROM (SELECT TOP (2000) 1 AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS s) AS r;
            """);
        var counts = outcome.Sets[0]["coalesce_nulls,isnull_nulls: ".Length..].Split(',').Select(int.Parse).ToArray();
        Assert.True(counts[0] > 100, outcome.ToString());
        Assert.Equal(0, counts[1]);

        // An aggregate is computed once for its group, and COALESCE reads that value: never NULL with a last argument 0.
        Assert.Equal(
            "coalesce_nulls: 0",
            (await Run("""
                SELECT SUM(CASE WHEN c IS NULL THEN 1 ELSE 0 END) AS coalesce_nulls
                FROM (SELECT COALESCE(MAX(CASE WHEN CRYPT_GEN_RANDOM(1) < 0x80 THEN 1 END), 0) AS c
                      FROM (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS s
                      GROUP BY n) AS r;
                """)).ToString());
    }

    // ---- CAST and CONVERT --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cast_and_convert_are_of_the_target_type_whatever_the_style()
    {
        // The target type as written: numeric without arguments is (18,0), a string type without a length is 30 long in CAST and
        // CONVERT. A style changes the characters, not the type.
        Assert.Equal(
            "c1 decimal(19,2), c2 decimal(19,2), c3 int, c4 varchar(5), c5 bigint, c6 smallint, c7 datetime2(0), c8 date, c9 bit, "
            + "c10 varchar(10), c11 varchar(30), c12 nvarchar(50), c13 varchar(30), c14 numeric(18,0), c15 nchar(2), c16 float, c17 int, c18 decimal(19,2)",
            await Describe($"""
                SELECT CAST(d AS decimal(19,2)) AS c1, CONVERT(decimal(19,2), i) AS c2, CAST(b AS int) AS c3, CAST(i AS varchar(5)) AS c4, CAST(i AS bigint) AS c5,
                       CAST(i AS smallint) AS c6, CAST(dd AS datetime2(0)) AS c7, CAST(dt AS date) AS c8, CAST(i AS bit) AS c9, CONVERT(varchar(10), dd, 23) AS c10,
                       CONVERT(varchar(30), dt, 120) AS c11, CONVERT(nvarchar(50), dt, 126) AS c12, CONVERT(varchar, dd, 112) AS c13, CAST(d AS numeric) AS c14,
                       CAST(b AS nchar(2)) AS c15, CAST(i AS float) AS c16, TRY_CAST(b AS int) AS c17, TRY_CONVERT(decimal(19,2), d2) AS c18
                FROM {Row}
                """));
    }

    [Fact]
    public async Task An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off()
    {
        // A conversion that overflows, integer arithmetic past its type, a division by zero and a SUM past its type fail the
        // call by default (8115, 8134, 220, 232); with ANSI_WARNINGS and ARITHABORT off, which only the caller's session can
        // set for a procedure, each is NULL. So is ISNULL's second argument when it overflows the first's type. A decimal
        // rounded to fewer places can gain a digit: 99.999 as decimal(4,2) is 100.00, which needs three.
        const string Overflows = """
            DECLARE @big decimal(38,2) = 99999999999999999999999999999999999.99, @i int = 2147483647, @z int = 0, @ti tinyint = NULL, @dt2 datetime2 = '0001-01-01';
            SELECT CAST(@big AS decimal(19,2)) AS dec, @i + 1 AS intadd, @i / @z AS div0, ISNULL(@ti, 1000) AS isnull_ti, CAST(99.999 AS decimal(4,2)) AS rounded,
                   CAST(-1 AS tinyint) AS neg_ti, CAST(CAST(-999.99 AS decimal(5,2)) AS varchar(6)) AS dec_varchar, CAST(123 AS nvarchar(2)) AS int_nvarchar,
                   CAST('99999999999' AS int) AS str_int, CAST(@dt2 AS datetime) AS dt2_dt,
                   (SELECT SUM(v) FROM (VALUES (2147483647), (1)) AS s (v)) AS sum_int;
            """;
        Assert.Equal(
            "dec,intadd,div0,isnull_ti,rounded,neg_ti,dec_varchar,int_nvarchar,str_int,dt2_dt,sum_int: null,null,null,null,null,null,null,null,null,null,null",
            (await Run($"SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; {Overflows}")).ToString());
        Assert.Equal(8115, (await Run($"SET ANSI_WARNINGS ON; {Overflows}")).Error);
    }

    [Fact]
    public async Task A_failed_conversion_is_an_error_even_under_ansi_warnings_and_arithabort_off()
    {
        // Only an overflow becomes NULL: a string that is not a number or a date, or a GUID too long for its string, fails.
        const string Off = "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; ";
        Assert.Equal(245, (await Run(Off + "SELECT CAST('abc' AS int) AS x;")).Error);
        Assert.Equal(241, (await Run(Off + "SELECT CONVERT(date, '31/02/2026', 103) AS x;")).Error);
        Assert.Equal(8170, (await Run(Off + "SELECT CAST(NEWID() AS varchar(10)) AS x;")).Error);
    }

    [Fact]
    public async Task A_conversion_that_cannot_overflow_keeps_every_value()
    {
        // At the edges of their types: an integer into a decimal with as many integer digits, a decimal into one with more
        // integer digits and fewer places (rounded), a date or time of every kind into a shorter string (cut short, as a
        // string is, with or without a style), and a number into a string long enough for its sign, digits and point.
        Assert.Equal(
            "a,b,c,d,e,f,g,h: -2147483648,1000000000000000.00,9223372036854775807,2026-,2026-11-10T,-0.5,-2147483648,-999.99",
            (await Run("""
                SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;
                SELECT CAST(CAST(-2147483648 AS int) AS decimal(10,0)) AS a, CAST(CAST(999999999999999.9999 AS decimal(19,4)) AS decimal(19,2)) AS b,
                       CAST(CAST(9223372036854775807 AS bigint) AS decimal(19,0)) AS c, CONVERT(varchar(5), CAST('2026-11-10' AS date), 23) AS d,
                       CONVERT(nvarchar(11), CAST('2026-11-10T12:34:56' AS datetime2(7)), 126) AS e, CAST(CAST(-0.5 AS decimal(1,1)) AS varchar(4)) AS f,
                       CAST(CAST(-2147483648 AS int) AS varchar(11)) AS g, CAST(CAST(-999.99 AS decimal(5,2)) AS nvarchar(7)) AS h;
                """)).ToString());
        Assert.Equal(
            "a,b,c,d,e: 2026,12,2026,Nov,10",
            (await Run("""
                SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;
                SELECT CONVERT(varchar(4), CAST('2026-11-10T01:02:03' AS datetime), 120) AS a, CAST(CAST('12:34:56' AS time) AS varchar(2)) AS b,
                       CAST(CAST('2026-11-10 12:00 +01:00' AS datetimeoffset) AS varchar(4)) AS c, CAST(CAST('2026-11-10 12:00' AS smalldatetime) AS nvarchar(3)) AS d,
                       CONVERT(varchar(2), CAST('2026-11-10' AS datetime2), 103) AS e;
                """)).ToString());
    }

    [Fact]
    public async Task Try_cast_and_try_convert_are_null_when_the_conversion_fails()
    {
        Assert.Equal(
            "a,b,c,d: null,null,null,5",
            (await Run("SELECT TRY_CAST('abc' AS int) AS a, TRY_CAST(1000 AS tinyint) AS b, TRY_CONVERT(date, '31/02/2026', 103) AS c, TRY_CAST(5 AS bigint) AS d;")).ToString());
    }

    // ---- strings ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Concat_is_as_long_as_its_arguments_together_up_to_8000_bytes_and_never_null()
    {
        // CONCAT and CONCAT_WS are nvarchar if any argument is (the separator included), and as long as their arguments
        // together, plus the separator between each two, NULL counting nothing; max if any is max; otherwise at most 8000
        // bytes (4000 characters for nvarchar), past which the value is cut short. Neither is ever NULL, whatever is NULL. A
        // string + is described the same way (a NULL literal counts 1), and is NULL when an operand is.
        Assert.Equal(
            "s1 nvarchar(15), s2 varchar(max), s3 varchar(8000), s4 nvarchar(4000), s5 varchar(6), s6 varchar(5), s7 nvarchar(15), s8 nvarchar(19), s9 varchar(14), "
            + "s10 nvarchar(13), s11 varchar(6), s12 nvarchar(4000), s13 varchar(6), s14 nvarchar(15), s15 varchar(9), s16 varchar(8000), s17 nvarchar(4000), "
            + "s18 varchar(max), s19 nvarchar(max), s20 varchar(6), s21 varchar(5), s22 varchar(9)",
            await Describe($"""
                SELECT CONCAT(a, b) AS s1, CONCAT(vm, a) AS s2, CONCAT(v8, a) AS s3, CONCAT(n4, a) AS s4, CONCAT(a, 'x') AS s5, CONCAT(a, NULL) AS s6,
                       CONCAT(a, b, NULL) AS s7, CONCAT_WS('-', a, b, CAST('z' AS varchar(2))) AS s8, CONCAT_WS('--', a, CAST('y' AS varchar(7))) AS s9,
                       CONCAT_WS(N'-', a, CAST('y' AS varchar(7))) AS s10, CONCAT_WS('-', a, NULL) AS s11, CONCAT(v8, n4) AS s12,
                       a + 'x' AS s13, a + b AS s14, a + c AS s15, v8 + a AS s16, n4 + b AS s17, vm + a AS s18, a + nm AS s19, a + NULL AS s20,
                       'abc' + 'de' AS s21, CONCAT(a, c) AS s22
                FROM {Row}
                """));
        Assert.Equal(
            "c8000,p8000,c4000,cnull,wsnull,wsallnull,wsnullsep,plusnull: 8000,8000,4000,,a-c,,ab,null",
            (await Run("""
                SELECT LEN(CONCAT(REPLICATE(CAST('a' AS varchar(8000)), 8000), 'bbb')) AS c8000, DATALENGTH(REPLICATE(CAST('a' AS varchar(8000)), 8000) + 'bbb') AS p8000,
                       LEN(CONCAT(REPLICATE(CAST(N'a' AS nvarchar(4000)), 4000), N'b')) AS c4000, CONCAT(NULL, NULL) AS cnull, CONCAT_WS('-', 'a', NULL, 'c') AS wsnull,
                       CONCAT_WS('-', NULL, NULL) AS wsallnull, CONCAT_WS(NULL, 'a', 'b') AS wsnullsep, 'a' + NULL AS plusnull;
                """)).ToString());
    }

    [Fact]
    public async Task Fixed_length_strings_plus_is_fixed_length()
    {
        // char + char is char, and nchar if either is nchar, as long as both together up to 8000 bytes (4000 characters),
        // where the value is cut too; a varying operand makes it varying (Concat_is_as_long_as_its_arguments_…).
        Assert.Equal(
            "d char(6), e nchar(6), f char(8000), g nchar(4000), h nchar(6)",
            await Describe($"SELECT c3 + c3 AS d, nc3 + c3 AS e, c5k + c5k AS f, n3k + n3k AS g, c3 + nc3 AS h FROM (SELECT CAST('a' AS char(3)) AS c3, CAST(N'a' AS nchar(3)) AS nc3, CAST('a' AS char(5000)) AS c5k, CAST(N'a' AS nchar(3000)) AS n3k) AS x"));
        Assert.Equal("n: 8000", (await Run("SELECT DATALENGTH(c + c) AS n FROM (SELECT CAST('a' AS char(5000)) AS c) AS x;")).ToString());
    }

    [Fact]
    public async Task With_concat_null_yields_null_off_a_string_plus_null_is_the_string() =>
        // The setting is ON by default, and OFF is deprecated: it only makes + less often NULL, so a claim that + is NULL
        // when an operand may be holds under either.
        Assert.Equal("x: a", (await Run("SET CONCAT_NULL_YIELDS_NULL OFF; SELECT 'a' + NULL AS x;")).ToString());

    // ---- arithmetic and SUM -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Decimal_arithmetic_has_the_documented_precision_and_scale()
    {
        // p1, s1 and p2, s2: + and - give scale max(s1, s2) and precision that plus max(p1 - s1, p2 - s2) + 1; * gives p1 + p2 + 1,
        // s1 + s2; / gives scale max(6, s1 + p2 + 1) and precision p1 - s1 + s2 plus it. An integer operand is a decimal of its
        // digits as a literal, (10,0) as an int. Numeric if either operand is numeric. Integers keep the wider integer type.
        Assert.Equal(
            "n1 decimal(20,4), n2 decimal(30,6), n3 decimal(34,22), n4 decimal(11,2), n5 int, n6 decimal(21,2), n7 decimal(14,2), n8 smallint, "
            + "n9 decimal(21,6), n10 int, n11 numeric(11,2), n12 numeric(11,2), n13 decimal(12,2), n14 bigint",
            await Describe($"""
                SELECT d + d2 AS n1, d * d2 AS n2, d / d2 AS n3, d - 1 AS n4, i + 1 AS n5, i * d AS n6, d * 100 AS n7, si + ti AS n8, d2 / 3 AS n9, i / 2 AS n10,
                       d + 0.5 AS n11, d + nd AS n12, d * 2 AS n13, i - bi AS n14
                FROM {Row}
                """));
    }

    [Fact]
    public async Task Sum_widens_its_argument()
    {
        // SUM of an integer narrower than bigint is int, of bigint bigint, of decimal(p, s) decimal(38, s), of money money.
        Assert.Equal(
            "a int, b int, c int, d bigint, e decimal(38,2), f decimal(38,4), g money, h float",
            await Describe($"SELECT SUM(ti) AS a, SUM(si) AS b, SUM(i) AS c, SUM(bi) AS d, SUM(d) AS e, SUM(d2) AS f, SUM(m) AS g, SUM(f) AS h FROM {Row}"));
        Assert.Equal(
            "none,nulls: null,null",
            (await Run("SELECT (SELECT SUM(v) FROM (VALUES (1)) AS s (v) WHERE v = 0) AS none, (SELECT SUM(v) FROM (VALUES (CAST(NULL AS int))) AS s (v)) AS nulls;")).ToString());
    }
}

namespace Haruspex.DifferentialTests;

// The trigonometric functions, DEGREES, RADIANS and PI; GREATEST and LEAST; ISJSON; and NEWID: their types, and when each is
// NULL. Inference's README.md, "Numeric functions" and "Built-in functions", gives the rules these rest on.
public partial class FunctionObservationTests
{
    /// <summary>One row of numbers and strings.</summary>
    private const string Numbers = """
        (SELECT CAST(1 AS bit) AS bt, CAST(1 AS tinyint) AS ti, CAST(1 AS smallint) AS si, CAST(1 AS int) AS i, CAST(1 AS bigint) AS bi,
                CAST(1 AS real) AS r, CAST(1 AS float) AS f, CAST(1 AS decimal(5,2)) AS d, CAST(1 AS money) AS m,
                CAST('a' AS char(3)) AS c, CAST('a' AS varchar(10)) AS v, CAST('a' AS varchar(max)) AS vm, CAST(N'a' AS nvarchar(5)) AS nv,
                CAST(N'a' AS nvarchar(max)) AS nm, CAST('2026-01-05' AS date) AS da) AS t
        """;

    [Fact]
    public async Task The_trigonometric_functions_and_pi_are_float_and_null_only_outside_their_domain_under_the_callers_options()
    {
        Assert.Equal(
            "a float, b float, c float, d float, e float, f float, g float, h float, p float",
            await Describe($"SELECT SIN(i) AS a, COS(d) AS b, TAN(f) AS c, COT(bt) AS d, ASIN(r) AS e, ACOS(m) AS f, ATAN(bi) AS g, ATN2(ti, si) AS h, PI() AS p FROM {Numbers}"));

        // ASIN past 1, COT of 0 and ATN2 of 0 and 0 are an invalid floating point operation: an error by default, NULL under
        // the caller's options.
        Assert.Equal(" ! 3623", (await Run([], "SELECT ASIN(2) AS v;")).ToString());
        Assert.Equal(" ! 3623", (await Run([], "SELECT COT(0) AS v;")).ToString());
        Assert.Equal(" ! 3623", (await Run([], "SELECT ATN2(0, 0) AS v;")).ToString());
        Assert.Equal("a,b,c,d,e: null,null,null,null,null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT ASIN(2) AS a, ACOS(-2) AS b, COT(0) AS c, ATN2(0, 0) AS d, COT(CAST(0 AS bit)) AS e;")).ToString());
    }

    [Fact]
    public async Task Degrees_and_radians_keep_an_integers_type_or_a_floats_and_degrees_may_overflow()
    {
        // An integer narrower than bigint gives int; bigint bigint; a float, a real or a bit float. A decimal or money widens.
        Assert.Equal(
            "a int, b int, c int, d bigint, e float, f float, g float, h decimal(38,18), k money",
            await Describe($"SELECT DEGREES(ti) AS a, DEGREES(si) AS b, RADIANS(i) AS c, DEGREES(bi) AS d, RADIANS(r) AS e, DEGREES(f) AS f, DEGREES(bt) AS g, DEGREES(d) AS h, RADIANS(m) AS k FROM {Numbers}"));
        Assert.Equal("a,b: 1,57", await Select("SELECT RADIANS(100) AS a, DEGREES(1) AS b;"));
        Assert.Equal(" ! 8115", (await Run([], "SELECT DEGREES(2147483647) AS v;")).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT DEGREES(CAST(1.7e308 AS float)) AS v;")).ToString());
    }

    [Fact]
    public async Task Greatest_and_least_skip_nulls_and_cap_a_max_string_at_8000_bytes_which_a_longer_value_fails()
    {
        // Values of one kind: their type, strings as long as the longest and varying, max at 8000 bytes; nvarchar if any is.
        Assert.Equal(
            "a int, b bigint, c varchar(10), d nvarchar(5), e varchar(8000), f nvarchar(4000), g date, h float, k char(3)",
            await Describe($"SELECT GREATEST(i, ti) AS a, LEAST(bi, i) AS b, GREATEST(c, v) AS c, LEAST(nv, c) AS d, GREATEST(vm, v) AS e, LEAST(nm, v) AS f, GREATEST(da, da) AS g, LEAST(f, r) AS h, GREATEST(c, c) AS k FROM {Numbers}"));

        // NULL only when every value is.
        Assert.Equal("a,b,c: 1,null,b", await Select("DECLARE @n int = NULL, @s varchar(10) = NULL; SELECT GREATEST(@n, 1) AS a, GREATEST(@n, @n) AS b, LEAST(@s, 'b') AS c;"));

        // A value past 8000 bytes is an error, under the caller's options too.
        const string Long = "SELECT LEN(GREATEST(REPLICATE(CAST('x' AS varchar(max)), 9000), 'a')) AS v;";
        Assert.Equal(" ! 8152", (await Run([], Long)).ToString());
        Assert.Equal(" ! 8152", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Long)).ToString());
    }

    [Fact]
    public async Task Isdate_and_isjson_give_0_or_1_and_isjson_null_for_null()
    {
        // The reference says each returns 1 or 0; over strings of every kind they read, that is all they give, but ISJSON's NULL.
        Assert.Equal(
            "d,j: 0,null;0,0;0,1;1,0",
            await Select("""
                SELECT DISTINCT ISDATE(x) AS d, ISJSON(x) AS j
                FROM (VALUES (NULL), (''), ('x'), ('2026-01-05'), ('{}'), ('[1]'), ('1'), ('null'), ('Jan 5 2026'), ('31/02/2026')) AS v (x)
                ORDER BY d, j;
                """));
    }

    [Fact]
    public async Task Isjson_is_an_int_null_for_null_and_newid_a_uniqueidentifier_never_null()
    {
        Assert.Equal("a int, b int, c int, n uniqueidentifier", await Describe($"SELECT ISJSON(v) AS a, ISJSON(nm) AS b, ISJSON(c, OBJECT) AS c, NEWID() AS n FROM {Numbers}"));
        Assert.Equal("a,b,c,d: null,0,1,0", await Select("DECLARE @s varchar(10) = NULL; SELECT ISJSON(@s) AS a, ISJSON('x') AS b, ISJSON('{}') AS c, ISJSON('1', OBJECT) AS d;"));
    }
}

namespace Haruspex.DifferentialTests;

// The string functions that read characters or codes, or build a string from a count: their types for each kind of string,
// and every way each is NULL besides a NULL argument. Inference's README.md, "Strings", gives the rules these rest on.
public partial class FunctionObservationTests
{
    /// <summary>One row of strings: char(3), varchar(10), varchar(max), nchar(3), nvarchar(10), nvarchar(max), and integers.</summary>
    private const string Strings = """
        (SELECT CAST('abc' AS char(3)) AS c, CAST('abc' AS varchar(10)) AS v, CAST('abc' AS varchar(max)) AS vm,
                CAST(N'abc' AS nchar(3)) AS nc, CAST(N'abc' AS nvarchar(10)) AS nv, CAST(N'abc' AS nvarchar(max)) AS nm,
                CAST(65 AS tinyint) AS ti, CAST(65 AS smallint) AS si, CAST(65 AS int) AS i, CAST(65 AS bigint) AS bi,
                CAST(1.5 AS float) AS f, CAST(1.5 AS decimal(5,2)) AS d) AS t
        """;

    [Fact]
    public async Task Ascii_and_unicode_are_int_and_null_for_an_empty_string()
    {
        Assert.Equal(
            "a1 int, a2 int, a3 int, u1 int, u2 int, u3 int",
            await Describe($"SELECT ASCII(c) AS a1, ASCII(vm) AS a2, ASCII(nv) AS a3, UNICODE(c) AS u1, UNICODE(nm) AS u2, UNICODE(nv) AS u3 FROM {Strings}"));
        Assert.Equal("a,b,c,d: null,null,97,null", await Select("SELECT ASCII('') AS a, UNICODE(N'') AS b, ASCII('ab') AS c, ASCII(CAST(NULL AS varchar(1))) AS d;"));
    }

    [Fact]
    public async Task Ascii_is_a_byte_from_0_to_255_whatever_the_character_or_collation() =>
        // The first byte of the string as varchar: of every UTF-16 code unit converted under the database's code page, of every
        // one under a UTF-8 collation (whose first byte is at most 239 in the basic plane, 240 for an emoji), and of every char.
        Assert.Equal(
            "lo,hi,ulo,uhi,clo,chi,emoji: 0,255,0,239,0,255,240",
            await Select("""
                SELECT MIN(ASCII(NCHAR(g.value))) AS lo, MAX(ASCII(NCHAR(g.value))) AS hi,
                    MIN(ASCII(CAST(NCHAR(g.value) COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(4)))) AS ulo,
                    MAX(ASCII(CAST(NCHAR(g.value) COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(4)))) AS uhi,
                    MIN(ASCII(CHAR(g.value % 256))) AS clo, MAX(ASCII(CHAR(g.value % 256))) AS chi,
                    MAX(ASCII(CAST((NCHAR(55357) + NCHAR(56832)) COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(4)))) AS emoji
                FROM GENERATE_SERIES(0, 65535) AS g;
                """));

    [Fact]
    public async Task Char_is_char_1_and_null_outside_0_to_255_and_overflows_past_int()
    {
        Assert.Equal("c1 char(1), c2 char(1), c3 char(1)", await Describe($"SELECT CHAR(ti) AS c1, CHAR(i) AS c2, CHAR(bi) AS c3 FROM {Strings}"));
        Assert.Equal("a,b,c,d: null,null,A,null", await Select("SELECT CHAR(256) AS a, CHAR(-1) AS b, CHAR(65) AS c, CHAR(CAST(NULL AS int)) AS d;"));

        // A bigint past int is converted to int first: an overflow, an error by default and NULL under the caller's options off.
        Assert.Equal(" ! 8115", (await Run([], "SELECT CHAR(CAST(2147483648 AS bigint)) AS v;")).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT CHAR(CAST(2147483648 AS bigint)) AS v;")).ToString());
    }

    [Fact]
    public async Task Charindex_and_patindex_are_int_or_bigint_over_max_and_null_only_for_a_null_argument()
    {
        // bigint when the string searched is max; the string looked for doesn't count.
        Assert.Equal(
            "c1 int, c2 bigint, c3 int, c4 int, p1 int, p2 bigint, p3 int",
            await Describe($"""
                SELECT CHARINDEX('a', v) AS c1, CHARINDEX('a', nm) AS c2, CHARINDEX(vm, v) AS c3, CHARINDEX(v, nv, bi) AS c4,
                       PATINDEX('%a%', c) AS p1, PATINDEX(v, nm) AS p2, PATINDEX(nm, v) AS p3
                FROM {Strings}
                """));
        // A start below 1 is taken as 1.
        Assert.Equal(
            "a,b,c,d,e: 0,null,null,1,1",
            await Select("SELECT CHARINDEX('', 'abc') AS a, CHARINDEX('a', 'abc', NULL) AS b, PATINDEX(NULL, 'abc') AS c, CHARINDEX('a', 'abc', -5) AS d, PATINDEX('%', '') AS e;"));
    }

    [Fact]
    public async Task A_start_or_length_past_int_overflows_in_charindex_substring_left_and_right_and_one_not_a_number_is_refused()
    {
        // A start or a length is converted to int: a bigint or a decimal past it is an error by default, and NULL under the
        // caller's ANSI_WARNINGS and ARITHABORT off.
        const string Calls = "DECLARE @b bigint = 5000000000, @d decimal(38,0) = 5000000000; SELECT CHARINDEX('a', 'abc', @b) AS c, SUBSTRING('abc', @b, 1) AS s1, SUBSTRING('abc', 1, @d) AS s2, LEFT('abc', @b) AS l, RIGHT('abc', @d) AS r;";
        Assert.Equal(" ! 8115", (await Run([], Calls)).ToString());
        Assert.Equal("c,s1,s2,l,r: null,null,null,null,null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Calls)).ToString());
        // SUBSTRING refuses a start or a length that is neither an integer nor a decimal when the procedure is created (8116);
        // LEFT and RIGHT convert any number or string to int, and refuse a date (206).
        string[] refused = [
            "CREATE TABLE dbo.obs_starts (id int NOT NULL PRIMARY KEY, f float NOT NULL, m money NOT NULL, s varchar(3) NOT NULL, d decimal(5,0) NOT NULL, da date NOT NULL);",
            "INSERT INTO dbo.obs_starts VALUES (1, 2, 2, '2', 2, '2026-01-01');",
        ];
        Assert.Equal(8116, (await Run(refused, "SELECT SUBSTRING('abc', f, 1) AS x FROM dbo.obs_starts;")).Error);
        Assert.Equal(8116, (await Run(refused, "SELECT SUBSTRING('abc', 1, m) AS x FROM dbo.obs_starts;")).Error);
        Assert.Equal(206, (await Run(refused, "SELECT LEFT('abc', da) AS x FROM dbo.obs_starts;")).Error);
        Assert.Equal("x,y,z: bc,ab,bc", (await Run(refused, "SELECT SUBSTRING('abc', d, d) AS x, LEFT('abc', m) AS y, RIGHT('abc', s) AS z FROM dbo.obs_starts;")).ToString());
    }

    [Fact]
    public async Task Replicate_and_space_are_as_long_as_a_literal_count_allows_up_to_8000_bytes_and_null_for_a_negative_one()
    {
        // A count that isn't a literal gives 8000 bytes; max stays max; the count 0 gives varchar(2), whatever the string.
        Assert.Equal(
            "r1 varchar(9), r2 varchar(8000), r3 nvarchar(4000), r4 varchar(max), r5 nvarchar(30), r6 varchar(2), s1 varchar(5), s2 varchar(8000), s3 varchar(1), s4 varchar(8000)",
            await Describe($"""
                SELECT REPLICATE(c, 3) AS r1, REPLICATE(v, 1000) AS r2, REPLICATE(nv, i) AS r3, REPLICATE(vm, 2) AS r4, REPLICATE(nv, 3) AS r5,
                       REPLICATE(v, 0) AS r6, SPACE(5) AS s1, SPACE(i) AS s2, SPACE(0) AS s3, SPACE(9000) AS s4
                FROM {Strings}
                """));
        Assert.Equal("a,b,c,d: null,null,,", await Select("SELECT REPLICATE('a', -1) AS a, SPACE(-1) AS b, REPLICATE('ab', 0) AS c, SPACE(0) AS d;"));
    }

    [Fact]
    public async Task Reverse_keeps_the_strings_length_and_makes_it_varying()
    {
        Assert.Equal(
            "r1 varchar(3), r2 varchar(10), r3 varchar(max), r4 nvarchar(3), r5 nvarchar(max)",
            await Describe($"SELECT REVERSE(c) AS r1, REVERSE(v) AS r2, REVERSE(vm) AS r3, REVERSE(nc) AS r4, REVERSE(nm) AS r5 FROM {Strings}"));
        Assert.Equal("a,b: ,cba", await Select("SELECT REVERSE('') AS a, REVERSE('abc') AS b;"));
    }

    [Fact]
    public async Task Stuff_is_as_long_as_its_strings_less_a_literal_length_and_null_for_a_start_or_length_it_cannot_use()
    {
        // Both strings' lengths, less what a literal length deletes from a literal start, which is at most what is left from it;
        // 8000 bytes when the start or the length isn't a literal; max when either string is; nvarchar when either is.
        Assert.Equal(
            "s1 varchar(3), s2 varchar(17), s3 varchar(6), s4 nvarchar(12), s5 varchar(8000), s6 varchar(max), s7 nvarchar(max), s8 nvarchar(4)",
            await Describe($"""
                SELECT STUFF(c, 1, 1, 'x') AS s1, STUFF(v, 1, 3, v) AS s2, STUFF(c, 1, 0, 'xyz') AS s3, STUFF(nc, 1, 1, v) AS s4,
                       STUFF(v, i, i, c) AS s5, STUFF(vm, 1, 1, 'x') AS s6, STUFF(v, 1, 1, nm) AS s7, STUFF(c, 2, 5, N'xyz') AS s8
                FROM {Strings}
                """));

        // NULL for a start of 0 or past the end, a negative length or an empty string; a NULL replacement deletes only.
        Assert.Equal(
            "a,b,c,d,e,f: null,null,null,null,bc,abxyz",
            await Select("SELECT STUFF('abc', 0, 1, 'x') AS a, STUFF('abc', 4, 1, 'x') AS b, STUFF('abc', 1, -1, 'x') AS c, STUFF('', 1, 0, 'x') AS d, STUFF('abc', 1, 1, NULL) AS e, STUFF('abc', 3, 5, 'xyz') AS f;"));
    }

    [Fact]
    public async Task Quotename_is_nvarchar_258_and_null_past_128_characters_or_for_a_quote_it_cannot_use()
    {
        Assert.Equal("q1 nvarchar(258), q2 nvarchar(258), q3 nvarchar(258)", await Describe($"SELECT QUOTENAME(c) AS q1, QUOTENAME(nm, '\"') AS q2, QUOTENAME(v, c) AS q3 FROM {Strings}"));
        Assert.Equal(
            "a,b,c,d: null,130,null,[a]",
            await Select("SELECT QUOTENAME(REPLICATE('a', 129)) AS a, LEN(QUOTENAME(REPLICATE('a', 128))) AS b, QUOTENAME('a', 'x') AS c, QUOTENAME('a') AS d;"));
        // The quotes it reads, a longer one by its first character; a value of 128 characters fits with any of them.
        Assert.Equal(
            "a,b,c,d,e,f,g,h,i,j,k,l: [a],[a],\"a\",'a',(a),(a),<a>,<a>,{a},{a},`a`,[a]",
            await Select("SELECT QUOTENAME('a', '[') AS a, QUOTENAME('a', ']') AS b, QUOTENAME('a', '\"') AS c, QUOTENAME('a', '''') AS d, QUOTENAME('a', '(') AS e, QUOTENAME('a', ')') AS f, QUOTENAME('a', '<') AS g, QUOTENAME('a', '>') AS h, QUOTENAME('a', '{') AS i, QUOTENAME('a', '}') AS j, QUOTENAME('a', '`') AS k, QUOTENAME('a', '[x') AS l;"));
        Assert.Equal("v: 130", await Select("SELECT LEN(QUOTENAME(CAST(REPLICATE(N'a', 128) AS nvarchar(128)), '`')) AS v;"));
    }

    [Fact]
    public async Task Str_is_varchar_of_its_literal_length_10_by_default_and_null_for_a_length_it_cannot_use()
    {
        // A length that isn't a literal gives 8000; one too short for the number gives stars, not NULL.
        Assert.Equal(
            "s1 varchar(10), s2 varchar(30), s3 varchar(8000), s4 varchar(5), s5 varchar(1), s6 varchar(10)",
            await Describe($"SELECT STR(f) AS s1, STR(f, 30) AS s2, STR(f, i) AS s3, STR(d, 5, i) AS s4, STR(f, 0) AS s5, STR(i) AS s6 FROM {Strings}"));
        Assert.Equal("a,b,c: null,null,***", await Select("SELECT STR(1.0, -1) AS a, STR(1.0, 9000) AS b, STR(123456.0, 3) AS c;"));
    }

    [Fact]
    public async Task Translate_is_8000_bytes_unless_its_string_is_max_and_soundex_and_difference_are_fixed()
    {
        // TRANSLATE: varchar(8000) or nvarchar(4000), nvarchar if any argument is, max only when the string is. SOUNDEX: varchar(5)
        // of any string, DIFFERENCE an int.
        Assert.Equal(
            "t1 varchar(8000), t2 nvarchar(4000), t3 varchar(max), t4 nvarchar(4000), x1 varchar(5), x2 varchar(5), d1 int",
            await Describe($"SELECT TRANSLATE(c, 'a', 'b') AS t1, TRANSLATE(nv, 'a', 'b') AS t2, TRANSLATE(vm, 'a', 'b') AS t3, TRANSLATE(v, nv, nv) AS t4, SOUNDEX(v) AS x1, SOUNDEX(nm) AS x2, DIFFERENCE(v, nm) AS d1 FROM {Strings}"));
        Assert.Equal("a,b,c: null,0000,4", await Select("SELECT TRANSLATE('abc', NULL, NULL) AS a, SOUNDEX('') AS b, DIFFERENCE('', '') AS c;"));

        // Strings of other lengths are an error, not NULL.
        Assert.Equal(" ! 9828", (await Run([], "SELECT TRANSLATE('abc', 'ab', 'x') AS v;")).ToString());
    }
}

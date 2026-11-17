namespace Haruspex.DifferentialTests;

// The scalar regular-expression functions of SQL Server 2025: REGEXP_REPLACE, REGEXP_SUBSTR, REGEXP_INSTR and REGEXP_COUNT, and
// the predicate REGEXP_LIKE. Their types, when each is NULL, and the compatibility levels that have them. Inference's README.md,
// "Regular expressions", gives the rules these rest on.
public partial class FunctionObservationTests
{
    [Fact]
    public async Task Regexp_replace_is_8000_bytes_or_max_of_its_strings_kind_and_cuts_a_longer_result_short()
    {
        // The string searched decides the kind: varchar(8000) or nvarchar(4000), max when it or the replacement is, whatever the
        // pattern's type. A fixed-length string is described as char(8000) or nchar(4000), but its value is not padded.
        Assert.Equal(
            "a varchar(8000), b nvarchar(4000), c varchar(max), d nvarchar(max), e varchar(8000), f char(8000), g nchar(4000), h varchar(max), i nvarchar(max)",
            await Describe($"""
                SELECT REGEXP_REPLACE(s, 'x', 'y') AS a, REGEXP_REPLACE(ns, N'x', 'y', 1, 0, 'i') AS b, REGEXP_REPLACE(vm, 'x') AS c,
                       REGEXP_REPLACE(nm, 'x', N'yy') AS d, REGEXP_REPLACE(s, N'x', N'y') AS e, REGEXP_REPLACE(c, 'a', 'b') AS f,
                       REGEXP_REPLACE(nc, 'a', 'b') AS g, REGEXP_REPLACE(s, 'x', nm) AS h, REGEXP_REPLACE(ns, 'x', vm) AS i
                FROM {Row}
                """));
        Assert.Equal(
            "a,b,c: 3,ax   |,8000",
            await Select("""
                SELECT DATALENGTH(REGEXP_REPLACE(CAST('a' AS char(3)), 'a', 'b')) AS a, REGEXP_REPLACE(CAST('ab' AS char(5)), 'b', 'x') + '|' AS b,
                       DATALENGTH(REGEXP_REPLACE(REPLICATE(CAST(N'a' AS nvarchar(4000)), 4000), 'a', 'bb')) AS c;
                """));

        // A result longer than the type is cut short, not an error, in UTF-8 too; NULL when any argument is; no match leaves the
        // string as it was.
        Assert.Equal(
            "a,b,c,d,e,f,g,h: 8000,null,null,null,null,null,null,abc",
            await Select("""
                DECLARE @s varchar(10) = NULL, @n int = NULL;
                SELECT DATALENGTH(REGEXP_REPLACE(CAST(REPLICATE('a', 8000) AS varchar(8000)) COLLATE Latin1_General_100_CI_AS_SC_UTF8, 'a', N'é')) AS a,
                       REGEXP_REPLACE(@s, 'a', 'b') AS b, REGEXP_REPLACE('abc', @s, 'b') AS c, REGEXP_REPLACE('abc', 'b', @s) AS d,
                       REGEXP_REPLACE('abc', 'b', 'x', @n) AS e, REGEXP_REPLACE('abc', 'b', 'x', 1, @n) AS f, REGEXP_REPLACE('abc', 'b', 'x', 1, 0, @s) AS g,
                       REGEXP_REPLACE('abc', 'z', 'y') AS h;
                """));

        // A pattern it can't read, a start below 1, a negative occurrence and a flag it doesn't know are errors.
        Assert.Equal(" ! 19308", (await Run([], "SELECT REGEXP_REPLACE('abc', '(', 'x') AS v;")).ToString());
        Assert.Equal(" ! 19301", (await Run([], "SELECT REGEXP_REPLACE('abc', 'b', 'x', 0) AS v;")).ToString());
        Assert.Equal(" ! 19301", (await Run([], "SELECT REGEXP_REPLACE('abc', 'b', 'x', 1, -1) AS v;")).ToString());
        Assert.Equal(" ! 19303", (await Run([], "SELECT REGEXP_REPLACE('abc', 'b', 'x', 1, 0, 'q') AS v;")).ToString());
    }

    [Fact]
    public async Task Regexp_substr_is_its_strings_length_varying_and_null_when_nothing_matches()
    {
        // The string searched's length, made varying (char(5) gives varchar(5)), max staying max.
        Assert.Equal(
            "a varchar(10), b nvarchar(20), c varchar(5), d nvarchar(4), e varchar(max), f nvarchar(max), g varchar(3)",
            await Describe($"""
                SELECT REGEXP_SUBSTR(s, 'x') AS a, REGEXP_SUBSTR(ns, N'x', 1, 1, 'i', 0) AS b, REGEXP_SUBSTR(c, 'a') AS c, REGEXP_SUBSTR(nc, 'a') AS d,
                       REGEXP_SUBSTR(vm, 'x') AS e, REGEXP_SUBSTR(nm, N'x') AS f, REGEXP_SUBSTR('abc', 'b') AS g
                FROM {Row}
                """));

        // NULL when nothing matches, for a start past the end, an occurrence past the matches, a group past the pattern's, and
        // when any argument is; an empty pattern matches the empty string.
        Assert.Equal(
            "a,b,c,d,e,f,g,h,i,j,k: null,null,null,null,null,null,null,null,1,b,b",
            await Select("""
                DECLARE @s varchar(10) = NULL, @n int = NULL;
                SELECT REGEXP_SUBSTR('abc', 'z') AS a, REGEXP_SUBSTR('abc', 'b', 4) AS b, REGEXP_SUBSTR('abc', 'b', 1, 2) AS c,
                       REGEXP_SUBSTR('abc', '(b)', 1, 1, '', 2) AS d, REGEXP_SUBSTR(@s, 'b') AS e, REGEXP_SUBSTR('abc', @s) AS f,
                       REGEXP_SUBSTR('abc', 'b', @n) AS g, REGEXP_SUBSTR('abc', 'b', 1, 1, '', @n) AS h,
                       IIF(REGEXP_SUBSTR('abc', '') = '', 1, 0) AS i, REGEXP_SUBSTR('abc', '(b)', 1, 1, '', 1) AS j, REGEXP_SUBSTR('abc', '(b)', 1, 1, '', 0) AS k;
                """));

        // A start or occurrence below 1 is an error, and one wider than int may overflow: an error by default, NULL under the
        // caller's options.
        Assert.Equal(" ! 19301", (await Run([], "SELECT REGEXP_SUBSTR('abc', 'b', 0) AS v;")).ToString());
        Assert.Equal(" ! 19301", (await Run([], "SELECT REGEXP_SUBSTR('abc', 'b', 1, 0) AS v;")).ToString());
        const string Wide = "SELECT REGEXP_SUBSTR('abc', 'b', CAST(3000000000 AS bigint)) AS v;";
        Assert.Equal(" ! 8115", (await Run([], Wide)).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Wide)).ToString());
    }

    [Fact]
    public async Task Regexp_instr_and_regexp_count_are_int_from_0_and_null_only_for_a_null_argument()
    {
        // int, over max too.
        Assert.Equal(
            "a int, b int, c int, d int",
            await Describe($"SELECT REGEXP_INSTR(s, 'x') AS a, REGEXP_INSTR(nm, N'x', 1, 1, 1, 'i', 0) AS b, REGEXP_COUNT(vm, 'x') AS c, REGEXP_COUNT(c, 'a', 2, 'c') AS d FROM {Row}"));

        // 0 when nothing matches, past the end or past the groups; NULL when an argument is, but a NULL return option is taken as
        // the default.
        Assert.Equal(
            "a,b,c,d,e,f,g,h,i,j,k: 0,0,0,0,0,null,null,null,null,null,2",
            await Select("""
                DECLARE @s varchar(10) = NULL, @n int = NULL;
                SELECT REGEXP_INSTR('abc', 'z') AS a, REGEXP_INSTR('abc', 'b', 1000) AS b, REGEXP_INSTR('abc', '(b)', 1, 1, 0, '', 5) AS c,
                       REGEXP_COUNT('abc', 'z') AS d, REGEXP_COUNT('abc', 'b', 1000) AS e, REGEXP_INSTR(@s, 'b') AS f, REGEXP_COUNT('abc', @s) AS g,
                       REGEXP_COUNT('abc', 'b', @n) AS h, REGEXP_COUNT('abc', 'b', 1, @s) AS i, REGEXP_INSTR('abc', 'b', 1, 1, 0, @s) AS j,
                       REGEXP_INSTR('abc', 'b', 1, 1, @n) AS k;
                """));

        // A return option other than 0 or 1 is an error; a start wider than int may overflow, NULL under the caller's options.
        Assert.Equal(" ! 19301", (await Run([], "SELECT REGEXP_INSTR('abc', 'b', 1, 1, 2) AS v;")).ToString());
        const string Wide = "SELECT REGEXP_COUNT('abc', 'b', CAST(3000000000 AS bigint)) AS v;";
        Assert.Equal(" ! 8115", (await Run([], Wide)).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Wide)).ToString());
    }

    [Fact]
    public async Task The_regular_expression_functions_but_regexp_like_are_there_at_every_compatibility_level_and_refuse_what_they_cannot_read()
    {
        // REGEXP_LIKE needs level 170 (195 below it); the other four are there from 100. A test can't change the level of the
        // shared database inside its transaction, so this uses a database of its own.
        var token = TestContext.Current.CancellationToken;
        await using var scratch = await server.ScratchAsync();
        var outcomes = new List<string>();
        foreach (var level in new[] { 100, 130, 160, 170 })
        {
            await scratch.ExecuteAsync($"ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {level};", token);
            outcomes.Add($"{level}: {await Outcome(scratch, "SELECT REGEXP_REPLACE('abc', 'b', 'x'), REGEXP_SUBSTR('abc', 'b'), REGEXP_INSTR('abc', 'b'), REGEXP_COUNT('abc', 'b');", token)}"
                + $" {await Outcome(scratch, "SELECT 1 WHERE REGEXP_LIKE('abc', 'b');", token)}");
        }

        Assert.Equal(["100: ok 195", "130: ok 195", "160: ok 195", "170: ok ok"], outcomes);

        // Refused when the procedure is created (8116): a string searched, a pattern or a replacement that isn't a string; a max
        // pattern; flags that are national or max; a start, occurrence, return option or group that is a tinyint, a bit or a
        // decimal (a smallint, an int and a bigint are read).
        string[] refused =
        [
            "REGEXP_COUNT(i, '2')", "REGEXP_SUBSTR(da, '2')", "REGEXP_COUNT(s, i)", "REGEXP_REPLACE(s, 'x', i)", "REGEXP_COUNT(s, vm)",
            "REGEXP_INSTR(s, nm)", "REGEXP_COUNT(s, 'x', 1, N'i')", "REGEXP_COUNT(s, 'x', 1, CAST('i' AS nchar(1)))",
            "REGEXP_SUBSTR(s, 'x', 1, 1, CAST('i' AS varchar(max)))", "REGEXP_COUNT(s, 'x', ti)", "REGEXP_SUBSTR(s, 'x', 1, bt)",
            "REGEXP_INSTR(s, 'x', 1, 1, d)", "REGEXP_REPLACE(s, 'x', 'y', 1, ti)",
        ];
        foreach (var call in refused)
            Assert.Equal((call, 8116), (call, (await Run([], $"SELECT {call} AS v FROM {Row};")).Error));
        Assert.Equal("v int", await Describe($"SELECT REGEXP_COUNT(c, CAST('x' AS nchar(1)), CAST(1 AS smallint), CAST('i' AS char(1))) + REGEXP_INSTR(s, 'x', bi) AS v FROM {Row}"));

        // A literal the server can check is refused too (19301, 19303): a start or an occurrence below 1, REGEXP_REPLACE's
        // occurrence or a group below 0, a return option other than 0 or 1, a flag other than c, i, s and m.
        string[] checkedLiterals =
        [
            "REGEXP_SUBSTR(s, 'x', 0)", "REGEXP_INSTR(s, 'x', 1, 0)", "REGEXP_REPLACE(s, 'x', 'y', 1, -1)", "REGEXP_SUBSTR(s, '(x)', 1, 1, '', -1)",
            "REGEXP_INSTR(s, 'x', 1, 1, 2)", "REGEXP_COUNT(s, 'x', 1, 'I')",
        ];
        foreach (var call in checkedLiterals)
            Assert.Contains((await Run([], $"SELECT {call} AS v FROM {Row};")).Error, new int?[] { 19301, 19303 });
        Assert.Equal(
            "a int, b varchar(10), c varchar(8000), d int",
            await Describe($"SELECT REGEXP_INSTR(s, 'x', 1, 1, 0, 'ic', 0) AS a, REGEXP_SUBSTR(s, 'x', 1, 1, '', 0) AS b, REGEXP_REPLACE(s, 'x', 'y', 1, 0, 'mi') AS c, REGEXP_COUNT(s, 'x', 1, '') AS d FROM {Row}"));
    }
}

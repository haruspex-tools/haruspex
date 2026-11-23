using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What a clause of a call changes: <c>DISTINCT</c> and <c>ALL</c> in an aggregate, <c>WITHIN GROUP</c> in <c>STRING_AGG</c>,
/// <c>TRIM</c>'s side, <c>RETURNING json</c> in a JSON constructor, and <c>COLLATE</c>; and that this server refuses
/// <c>JSON_VALUE … RETURNING</c>. A row of the function table reads a clause only where a test here, or one it cites, shows what
/// the clause does; any other clause makes a call unknown (Haruspex.SqlServer.Inference's README.md, "Clauses"; decision 0113).
/// Types are read as the server describes them (<c>sys.dm_exec_describe_first_result_set</c>), what can be NULL from values.
/// Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class ClauseObservationTests(SqlServerFixture server)
{
    private Task<string> Describe(string select) => FirstResultSet.Describe(server, select);

    private async Task<string> Select(string call)
    {
        var outcome = await ObservedCall.Run(server, [], call);
        Assert.Null(outcome.Error);
        return outcome.ToString();
    }

    /// <summary>A row of never-null columns, so that a column's type is its own.</summary>
    private const string Row = """
        (SELECT CAST(1 AS int) AS i, CAST(1.5 AS decimal(10,2)) AS d, CAST('a' AS varchar(5)) AS s, CAST(N'n' AS nvarchar(5)) AS n,
                CAST(1 AS float) AS f, CAST('c' AS char(4)) AS c) AS x
        """;

    [Fact]
    public async Task Distinct_and_all_in_an_aggregate_keep_its_type_and_when_it_is_null()
    {
        // The type is the aggregate's without the clause, for each function that takes it.
        const string Calls = "COUNT({0}i) AS a, COUNT_BIG({0}i) AS b, SUM({0}i) AS c, AVG({0}d) AS d, MIN({0}s) AS e, MAX({0}s) AS f, "
            + "STDEV({0}i) AS g, STDEVP({0}f) AS h, VAR({0}i) AS i, VARP({0}i) AS j";
        var plain = await Describe($"SELECT {string.Format(System.Globalization.CultureInfo.InvariantCulture, Calls, "")} FROM {Row}");
        Assert.Equal("a int, b bigint, c int, d decimal(38,6), e varchar(5), f varchar(5), g float, h float, i float, j float", plain);
        Assert.Equal(plain, await Describe($"SELECT {string.Format(System.Globalization.CultureInfo.InvariantCulture, Calls, "DISTINCT ")} FROM {Row}"));
        Assert.Equal(plain, await Describe($"SELECT {string.Format(System.Globalization.CultureInfo.InvariantCulture, Calls, "ALL ")} FROM {Row}"));

        // NULL as without it: over no rows (COUNT 0), over only NULLs; and the sample statistics over fewer than two values,
        // which DISTINCT makes of two equal ones.
        Assert.Equal(
            "a,c,e,g,i: 0,null,null,null,null",
            await Select("SELECT COUNT(DISTINCT v) AS a, SUM(DISTINCT v) AS c, MIN(DISTINCT v) AS e, STDEV(DISTINCT v) AS g, VAR(DISTINCT v) AS i FROM (VALUES (1)) AS t (v) WHERE v = 0;"));
        Assert.Equal(
            "a,c: 0,null",
            await Select("SELECT COUNT(DISTINCT v) AS a, SUM(DISTINCT v) AS c FROM (VALUES (CAST(NULL AS int))) AS t (v);"));
        Assert.Equal(
            "a,g,i,s,v: 1,null,null,0,0",
            await Select("SELECT COUNT(DISTINCT v) AS a, STDEV(DISTINCT v) AS g, VAR(DISTINCT v) AS i, STDEV(v) AS s, VAR(v) AS v FROM (VALUES (1), (1)) AS t (v);"));
    }

    [Fact]
    public async Task String_agg_within_group_keeps_its_type_and_when_it_is_null()
    {
        Assert.Equal(
            "a varchar(8000), b nvarchar(4000), c varchar(8000)",
            await Describe($"SELECT STRING_AGG(s, ',') WITHIN GROUP (ORDER BY i DESC) AS a, STRING_AGG(n, ',') WITHIN GROUP (ORDER BY i) AS b, STRING_AGG(s, ',') AS c FROM {Row}"));

        // The order changes only the order: NULL over no rows and over only NULLs, as without it.
        Assert.Equal(
            "a,b,c: b;a,null,null",
            await Select("""
                SELECT (SELECT STRING_AGG(s, ';') WITHIN GROUP (ORDER BY s DESC) FROM (VALUES ('a'), ('b')) AS t (s)) AS a,
                       (SELECT STRING_AGG(s, ';') WITHIN GROUP (ORDER BY s) FROM (VALUES ('a')) AS t (s) WHERE s = 'z') AS b,
                       (SELECT STRING_AGG(s, ';') WITHIN GROUP (ORDER BY s) FROM (VALUES (CAST(NULL AS varchar(5)))) AS t (s)) AS c;
                """));
    }

    [Fact]
    public async Task Trim_from_one_side_keeps_the_strings_type_and_when_it_is_null()
    {
        // As TRIM(chars FROM s): the string's length, varying, nvarchar if either is; NULL when either is.
        Assert.Equal(
            "a varchar(5), b nvarchar(5), c nvarchar(5), d varchar(5), e varchar(4)",
            await Describe($"SELECT TRIM(LEADING 'x' FROM s) AS a, TRIM(TRAILING N'x' FROM s) AS b, TRIM(BOTH 'x' FROM n) AS c, TRIM('x' FROM s) AS d, TRIM(LEADING 'x' FROM c) AS e FROM {Row}"));
        Assert.Equal(
            "a,b,c,d: a..,..a,a,null",
            await Select("SELECT TRIM(LEADING '.' FROM '..a..') AS a, TRIM(TRAILING '.' FROM '..a..') AS b, TRIM(BOTH '.' FROM '..a..') AS c, TRIM(LEADING NULL FROM '.a') AS d;"));

        // A side without characters to trim is refused when the batch is compiled (156).
        Assert.Equal(156, (await ObservedCall.Run(server, [], "SELECT TRIM(BOTH FROM '  a  ') AS a;")).Error);
    }

    [Fact]
    public async Task A_json_constructor_returning_json_is_the_constructor()
    {
        // Each nests as JSON and writes what it writes without the clause; json_objectagg takes no ORDER BY (156).
        Assert.Equal(
            """a: {"v":[1,2],"w":{"a":1},"x":[2,1],"y":{"a":1}}""",
            await Select("""
                SELECT json_object('v': json_array(1, NULL, 2 RETURNING json), 'w': json_object('a': 1 RETURNING json),
                                   'x': (SELECT json_arrayagg(v ORDER BY v DESC RETURNING json) FROM (VALUES (1), (2)) AS t (v)),
                                   'y': (SELECT json_objectagg(k: v RETURNING json) FROM (VALUES ('a', 1)) AS t (k, v))) AS a;
                """));
        Assert.Equal(156, (await ObservedCall.Run(server, [], "SELECT json_objectagg(k: v ORDER BY v) AS a FROM (VALUES ('a', 1)) AS t (k, v);")).Error);
    }

    [Fact]
    public async Task Json_value_returning_is_refused_by_this_server() =>
        Assert.Equal(102, (await ObservedCall.Run(server, [], """SELECT JSON_VALUE(N'{"a":1}', '$.a' RETURNING int) AS a;""")).Error);

    [Fact]
    public async Task Collate_keeps_a_unicode_strings_characters_and_may_change_or_cut_a_non_unicode_ones()
    {
        // The type is the string's, under any collation, UTF-8 too.
        Assert.Equal(
            "a varchar(5), b nvarchar(5), c varchar(5), d nvarchar(5)",
            await Describe($"SELECT s COLLATE Greek_CI_AS AS a, n COLLATE Greek_CI_AS AS b, s COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS c, n COLLATE Japanese_CI_AS AS d FROM {Row}"));

        // é (233) in this database's code page, 1252: an nvarchar keeps it under any collation; a varchar becomes e (101) under
        // Greek's code page, and as varchar(1) under UTF-8, which needs two bytes for it, is cut to ''.
        Assert.Equal(
            "greek,unicode,japanese,utf8: 101,233,233,0",
            await Select("""
                DECLARE @t TABLE (s varchar(1) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL, n nvarchar(1) NOT NULL);
                INSERT INTO @t VALUES ('é', N'é');
                SELECT UNICODE(s COLLATE Greek_CI_AS) AS greek, UNICODE(n COLLATE Greek_CI_AS) AS unicode, UNICODE(n COLLATE Japanese_CI_AS) AS japanese,
                       DATALENGTH(s COLLATE Latin1_General_100_CI_AS_SC_UTF8) AS utf8
                FROM @t;
                """));

        // COLLATE of anything but a string is refused when the batch is compiled (447).
        Assert.Equal(447, (await ObservedCall.Run(server, [], "SELECT 1 COLLATE Latin1_General_BIN AS a;")).Error);
    }
}

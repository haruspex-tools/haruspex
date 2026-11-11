using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server gives for the expressions haruspex types: <c>CASE</c> (simple and searched), <c>IIF</c>, <c>CHOOSE</c>,
/// <c>NULLIF</c>, and <c>COALESCE</c> and <c>ISNULL</c> over JSON. Types are read as the server describes them
/// (<c>sys.dm_exec_describe_first_result_set</c>); what can be NULL, and how JSON nests, is read from values. The rules rest on
/// these (Haruspex.SqlServer.Inference's README.md, "CASE, IIF, CHOOSE and NULLIF", "COALESCE and ISNULL over JSON").
/// Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class CaseObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, [], call, errorsAsMessages);

    /// <summary>A row of the types the expressions below read, each value given, so that a column's type is its own.</summary>
    private const string Row = """
        (SELECT 1 AS id, CAST(1 AS int) AS i, CAST(1 AS tinyint) AS ti, CAST(1 AS smallint) AS si, CAST(1 AS bigint) AS bi,
                CAST(1 AS decimal(9,2)) AS d, CAST('c' AS char(4)) AS c, CAST('v' AS varchar(10)) AS v) AS x
        """;

    /// <summary>The server's description of a query's columns: <c>name type</c>, comma-separated.</summary>
    private async Task<string> Describe(string select)
    {
        var query = select.Replace("'", "''", StringComparison.Ordinal);
        var outcome = await Run($"SELECT name + ' ' + system_type_name AS c FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;");
        Assert.Null(outcome.Error);
        return outcome.Sets[0]["c: ".Length..].Replace(";", ", ", StringComparison.Ordinal);
    }

    /// <summary>Order headers 1 and 2, and lines for order 1 only, so a correlated FOR JSON subquery is empty for order 2.</summary>
    private const string Orders = """
        CREATE TABLE #o (id int NOT NULL PRIMARY KEY);
        CREATE TABLE #l (id int NOT NULL PRIMARY KEY, o int NOT NULL, q int NOT NULL);
        INSERT #o VALUES (1), (2);
        INSERT #l VALUES (1, 1, 5), (2, 1, 6);
        """;

    /// <summary>The one FOR JSON document a query returns, under the column name <c>j</c>.</summary>
    private async Task<string> Json(string select)
    {
        var outcome = await Run($"{Orders}\nSELECT ({select}) AS j;");
        Assert.Null(outcome.Error);
        return outcome.Sets[0]["j: ".Length..];
    }

    // ---- types -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Case_iif_and_choose_take_the_type_of_highest_precedence_over_their_results()
    {
        // As COALESCE (ExpressionObservationTests): strings are nvarchar if any result is, varchar if any is varying, of the
        // longest length; a char beside a varchar literal is varchar. Exact numbers: a decimal of every result's integer digits
        // and the largest scale, an integer literal counting its own digits; integers the widest. A NULL result adds nothing.
        // Conditions do not matter, only results; IIF is a two-result CASE and CHOOSE's index is not a result.
        Assert.Equal(
            "s1 varchar(6), s2 nvarchar(2), s3 varchar(1), s4 varchar(8), s5 char(4), s6 varchar(4), s7 varchar(1), s8 varchar(1), s9 varchar(2), "
            + "n1 int, n2 numeric(2,1), n3 int, n4 decimal(12,2), n5 numeric(6,1), n6 bigint, n7 numeric(10,0), n8 smallint, "
            + "i1 varchar(3), i2 int, c1 varchar(3), c2 numeric(2,1), c3 varchar(1), c4 varchar(1)",
            await Describe($"""
                SELECT CASE WHEN id = 1 THEN 'a' ELSE 'longer' END AS s1, CASE WHEN id = 1 THEN 'a' ELSE N'bb' END AS s2,
                       CASE WHEN id = 1 THEN 'a' END AS s3, CASE WHEN id = 1 THEN c ELSE 'abcdefgh' END AS s4,
                       CASE WHEN id = 1 THEN c ELSE c END AS s5, CASE WHEN id = 1 THEN c ELSE 'ab' END AS s6,
                       CASE id WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END AS s7, CASE WHEN id = 1 THEN NULL ELSE 'x' END AS s8,
                       CASE WHEN v = 'abcdefgh' THEN 'a ' ELSE 'b' END AS s9,
                       CASE WHEN id = 1 THEN 1 ELSE 2 END AS n1, CASE WHEN id = 1 THEN 1 ELSE 2.5 END AS n2,
                       CASE WHEN id = 1 THEN ti ELSE 1 END AS n3, CASE WHEN id = 1 THEN i ELSE d END AS n4,
                       CASE WHEN id = 1 THEN 1 ELSE 12345.5 END AS n5, CASE WHEN id = 1 THEN bi ELSE i END AS n6,
                       CASE WHEN id = 1 THEN 1 ELSE 2147483648 END AS n7, CASE WHEN id = 1 THEN si ELSE ti END AS n8,
                       IIF(id = 1, 'a', 'bcd') AS i1, IIF(id = 1, 1, NULL) AS i2,
                       CHOOSE(id, 'a', 'bb', 'ccc') AS c1, CHOOSE(id, 1, 2.5) AS c2, CHOOSE(ti, 'x', 'y') AS c3, CHOOSE(id, NULL, 'a') AS c4
                FROM {Row}
                """));
    }

    [Fact]
    public async Task Nullif_is_its_first_arguments_type_but_an_integer_literal_is_the_smallest_integer_holding_it()
    {
        // NULLIF(a, b) is a's type whatever b is: an int column stays int beside a decimal, a varchar beside an nvarchar. An
        // integer literal as a is the smallest integer type that holds it, where everywhere else it is int (as SELECT 1 is).
        Assert.Equal(
            "a int, b varchar(10), c char(4), d decimal(9,2), e tinyint, f int, g varchar(3), "
            + "l0 tinyint, l1 tinyint, l2 tinyint, l3 smallint, l4 smallint, l5 int, l6 smallint, l7 int, l8 smallint, l9 int, l10 tinyint, l11 numeric(10,0), l12 int",
            await Describe($"""
                SELECT NULLIF(i, 1.5) AS a, NULLIF(v, N'x') AS b, NULLIF(c, 'x') AS c, NULLIF(d, 1) AS d, NULLIF(ti, i) AS e, NULLIF(i, ti) AS f,
                       NULLIF('abc', N'x') AS g,
                       NULLIF(0, 2) AS l0, NULLIF(1, 2) AS l1, NULLIF(255, 2) AS l2, NULLIF(256, 2) AS l3, NULLIF(32767, 2) AS l4,
                       NULLIF(32768, 2) AS l5, NULLIF(-32768, 2) AS l6, NULLIF(-32769, 2) AS l7, NULLIF(-1, 2) AS l8,
                       NULLIF(2147483647, 2) AS l9, NULLIF((1), 2) AS l10, NULLIF(2147483648, 2) AS l11, (1) AS l12
                FROM {Row}
                """));
    }

    // ---- what is NULL ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Case_without_else_choose_out_of_range_and_nullif_of_equal_values_are_null()
    {
        // A CASE that no WHEN matches is NULL without an ELSE; CHOOSE is NULL for an index below 1, above its count, or NULL,
        // and truncates a decimal index or converts a string one; NULLIF is NULL when its arguments compare equal, under the
        // collation (case-insensitive here) and ignoring trailing spaces.
        Assert.Equal(
            "a,c0,c3,cneg,cnull,cdec,cstr,n1,n2,n3,n4: null,null,null,null,null,a,b,null,null,null,1",
            (await Run("""
                SELECT CASE WHEN 1 = 0 THEN 'x' END AS a, CHOOSE(0, 'a', 'b') AS c0, CHOOSE(3, 'a', 'b') AS c3, CHOOSE(-1, 'a') AS cneg,
                       CHOOSE(CAST(NULL AS int), 'a') AS cnull, CHOOSE(1.9, 'a', 'b') AS cdec, CHOOSE('2', 'a', 'b') AS cstr,
                       NULLIF('a', 'A') AS n1, NULLIF('a', 'a  ') AS n2, NULLIF(1, 1) AS n3, NULLIF(1, NULL) AS n4;
                """)).ToString());
    }

    [Fact]
    public async Task A_case_whose_results_are_all_null_is_an_error_but_choose_of_nulls_is_null()
    {
        // Error 8133, when the procedure is created as much as when it runs, for CASE and IIF; NULLIF of the NULL literal is
        // error 4151. CHOOSE of NULLs only is allowed, and NULL.
        Assert.Equal(8133, (await Run("SELECT CASE WHEN 1 = 1 THEN NULL ELSE NULL END AS a;")).Error);
        Assert.Equal(8133, (await Run("SELECT IIF(1 = 1, NULL, NULL) AS a;")).Error);
        Assert.Equal(4151, (await Run("SELECT NULLIF(NULL, 1) AS a;")).Error);
        Assert.Equal("a: null", (await Run("SELECT CHOOSE(1, NULL, NULL) AS a;")).ToString());
    }

    [Fact]
    public async Task A_literal_result_keeps_its_trailing_spaces_and_a_fixed_length_result_pads()
    {
        // A string literal is varchar, so a CASE of literals is varchar and gives each literal exactly, trailing spaces too,
        // in a rowset and in FOR JSON. Only when every result is fixed-length is the CASE char, padding the shorter.
        Assert.Equal(
            "sp,len,mixed,fixed: [a ],2,[ab],[ab  ] | j: [{\"v\":\"a \"}]",
            (await Run("""
                SELECT '[' + CASE WHEN 1 = 1 THEN 'a ' ELSE 'b' END + ']' AS sp, DATALENGTH(CASE WHEN 1 = 1 THEN 'a ' ELSE 'b' END) AS len,
                       '[' + CASE WHEN 1 = 0 THEN CAST('xy' AS char(4)) ELSE 'ab' END + ']' AS mixed,
                       '[' + CASE WHEN 1 = 0 THEN CAST('xy' AS char(4)) ELSE CAST('ab' AS char(2)) END + ']' AS fixed;
                SELECT (SELECT CASE WHEN 1 = 1 THEN 'a ' ELSE 'b' END AS v FOR JSON PATH) AS j;
                """)).ToString());
    }

    [Fact]
    public async Task A_case_branch_runs_only_when_its_condition_is_true_and_later_ones_only_when_earlier_were_not()
    {
        // In THEN, its WHEN is TRUE; in a later branch every earlier WHEN was FALSE or UNKNOWN. So CASE WHEN x IS NULL THEN 0
        // ELSE x END is never NULL, but CASE WHEN x = 1 THEN 0 ELSE x END is NULL for a NULL x: x = 1 is UNKNOWN, not FALSE.
        // A simple CASE compares with =, so CASE x WHEN 1 ... ELSE x END is NULL too, and WHEN NULL never matches. Over ROLLUP,
        // the super-aggregate row's NULL is tested like any other. A variable is the same in WHEN and THEN.
        Assert.Equal(
            "x,a,b,c,d,e,f,g: 1,1,0,0,one,1,y,1;null,0,null,null,other,null,y,0",
            (await Run("""
                DECLARE @p int = NULL;
                SELECT x, CASE WHEN x IS NULL THEN 0 ELSE x END AS a, CASE WHEN x = 1 THEN 0 ELSE x END AS b,
                       CASE x WHEN 1 THEN 0 ELSE x END AS c, CASE x WHEN NULL THEN 'null' WHEN 1 THEN 'one' ELSE 'other' END AS d,
                       CASE WHEN x <> 1 THEN 0 WHEN x IS NOT NULL THEN x END AS e, IIF(@p IS NULL, 'y', 'n') AS f,
                       CASE WHEN @p IS NULL THEN COALESCE(x, 0) ELSE @p END AS g
                FROM (VALUES (1), (NULL)) AS v (x) ORDER BY x DESC;
                """)).ToString());
        Assert.Equal(
            "g,n: a,1;total,1",
            (await Run("""
                SELECT CASE WHEN g IS NULL THEN 'total' ELSE g END AS g, COUNT_BIG(*) AS n
                FROM (VALUES ('a')) AS v (g) GROUP BY ROLLUP (g) ORDER BY GROUPING(g);
                """)).ToString());
    }

    // ---- COALESCE and ISNULL over JSON -------------------------------------------------------------------------------

    [Fact]
    public async Task Coalesce_over_a_for_json_subquery_nests_as_escaped_text_and_isnull_as_json()
    {
        // COALESCE's value is text wherever it nests: in FOR JSON, json_object and json_array it is escaped, the subquery's
        // array and the '[]' fallback alike, even when every argument is JSON. ISNULL keeps its first argument's JSON: it nests
        // as JSON, and so does the fallback, written as it is.
        Assert.Equal(
            """[{"id":1,"lines":"[{\"q\":5},{\"q\":6}]"},{"id":2,"lines":"[]"}]""",
            await Json("SELECT o.id, COALESCE((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '[]') AS lines FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            """[{"id":1,"lines":"[{\"q\":5},{\"q\":6}]"},{"id":2,"lines":"[]"}]""",
            await Json("SELECT o.id, COALESCE((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), JSON_QUERY('[]')) AS lines FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            """[{"id":1,"lines":"[{\"q\":5},{\"q\":6}]"},{"id":2,"lines":"[]"}]""",
            await Json("SELECT o.id, COALESCE(JSON_QUERY((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH)), '[]') AS lines FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            """[{"id":1,"lines":[{"q":5},{"q":6}]},{"id":2,"lines":[]}]""",
            await Json("SELECT o.id, ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '[]') AS lines FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            """[{"id":1,"x":{"lines":[{"q":5},{"q":6}]}},{"id":2,"x":{"lines":[]}}]""",
            await Json("SELECT o.id, ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), N'[]') AS [x.lines] FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            """[{"id":1,"x":{"lines":"[{\"q\":5},{\"q\":6}]"}},{"id":2,"x":{"lines":"[]"}}]""",
            await Json("SELECT o.id, JSON_OBJECT('lines': COALESCE((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '[]')) AS x FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            """[[{"q":5},{"q":6}],[],"[]"]""",
            await Json("""
                SELECT JSON_ARRAY(ISNULL((SELECT l.q FROM #l AS l WHERE l.o = 1 FOR JSON PATH), '[]'), ISNULL((SELECT l.q FROM #l AS l WHERE l.o = 2 FOR JSON PATH), '[]'),
                                  COALESCE((SELECT l.q FROM #l AS l WHERE l.o = 2 FOR JSON PATH), '[]'))
                """));

        // A scalar subquery around ISNULL keeps it JSON, as around any JSON value (JsonConstructorObservationTests).
        Assert.Equal(
            """[{"id":1,"lines":[{"q":5},{"q":6}]},{"id":2,"lines":[]}]""",
            await Json("SELECT o.id, (SELECT ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '[]')) AS lines FROM #o AS o FOR JSON PATH"));

        // Even a subquery without FROM, one row always, is not folded away: COALESCE over it is text.
        Assert.Equal(
            """[{"b":"[{\"n\":1}]","c":"[]"}]""",
            await Json("SELECT COALESCE((SELECT 1 AS n FOR JSON PATH), '[]') AS b, COALESCE((SELECT 1 AS n WHERE 1 = 0 FOR JSON PATH), '[]') AS c FOR JSON PATH"));
    }

    [Fact]
    public async Task Json_query_over_coalesce_makes_it_json_again()
    {
        // json_query parses the text: an array, the empty array, or with '{}' the empty object. Over 'null' it is an error,
        // 13609, as json_query('null') is: it takes only an object or an array.
        Assert.Equal(
            """[{"id":1,"lines":[{"q":5},{"q":6}]},{"id":2,"lines":[]}]""",
            await Json("SELECT o.id, JSON_QUERY(COALESCE((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '[]')) AS lines FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            """[{"id":1,"lines":[{"q":5},{"q":6}]},{"id":2,"lines":{}}]""",
            await Json("SELECT o.id, JSON_QUERY(COALESCE((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '{}')) AS lines FROM #o AS o FOR JSON PATH"));
        Assert.Equal(
            13609,
            (await Run($"{Orders}\nSELECT (SELECT o.id, JSON_QUERY(COALESCE((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), 'null')) AS lines FROM #o AS o FOR JSON PATH) AS j;")).Error);
        Assert.Equal(13609, (await Run("SELECT JSON_QUERY('null') AS a;")).Error);
    }

    [Fact]
    public async Task Isnull_writes_its_fallback_into_the_json_as_it_is()
    {
        // ISNULL's fallback is not checked: '{}' is an object, ' [ ] ' keeps its spaces, 'null' is JSON null (so the key is
        // there, even under ABSENT ON NULL, and json_array keeps it), a number its digits, and '' or 'x' make text that is not
        // JSON. Another JSON value is written as it is too.
        Assert.Equal(
            """[{"id":1,"a":[{"q":5},{"q":6}],"b":[{"q":5},{"q":6}],"c":[{"q":5},{"q":6}],"d":[{"q":5},{"q":6}],"e":[{"q":5},{"q":6}],"f":[{"q":5},{"q":6}],"g":[{"q":5},{"q":6}]},"""
            + """{"id":2,"a":{},"b": [ ] ,"c":null,"d":2,"e":,"f":x,"g":[{"q":5},{"q":6}]}]""",
            await Json("""
                SELECT o.id, ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '{}') AS a,
                       ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), ' [ ] ') AS b,
                       ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), 'null') AS c,
                       ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), o.id) AS d,
                       ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '') AS e,
                       ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), 'x') AS f,
                       ISNULL((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), (SELECT l.q FROM #l AS l WHERE l.o = 1 FOR JSON PATH)) AS g
                FROM #o AS o FOR JSON PATH
                """));
        Assert.Equal(
            """[null] | [null] | {"a":null}""",
            string.Join(" | ", await Json("JSON_ARRAY(ISNULL((SELECT l.q FROM #l AS l WHERE l.o = 2 FOR JSON PATH), 'null'))"),
                await Json("JSON_ARRAY(ISNULL((SELECT l.q FROM #l AS l WHERE l.o = 2 FOR JSON PATH), 'null') NULL ON NULL)"),
                await Json("JSON_OBJECT('a': ISNULL((SELECT l.q FROM #l AS l WHERE l.o = 2 FOR JSON PATH), 'null') ABSENT ON NULL)")));
    }

    [Fact]
    public async Task Without_its_array_wrapper_the_subquery_is_text_in_coalesce_and_isnull_until_json_query()
    {
        // A FOR JSON subquery WITHOUT_ARRAY_WRAPPER is text (JsonConstructorObservationTests), and so is ISNULL or COALESCE
        // over it, the fallback too; json_query over that is the object, or the empty one.
        Assert.Equal(
            """[{"id":1,"a":"{\"q\":5}","b":"{\"q\":5}","c":{"q":5}},{"id":2,"a":"{}","b":"{}","c":{}}]""",
            await Json("""
                SELECT o.id, ISNULL((SELECT TOP (1) l.q FROM #l AS l WHERE l.o = o.id ORDER BY l.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}') AS a,
                       COALESCE((SELECT TOP (1) l.q FROM #l AS l WHERE l.o = o.id ORDER BY l.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}') AS b,
                       JSON_QUERY(ISNULL((SELECT TOP (1) l.q FROM #l AS l WHERE l.o = o.id ORDER BY l.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}')) AS c
                FROM #o AS o FOR JSON PATH
                """));
    }

    [Fact]
    public async Task Case_iif_choose_and_coalesce_over_json_are_text_unless_the_server_folds_them_to_one_branch()
    {
        // Over JSON, CASE, IIF, CHOOSE and NULLIF are text like COALESCE. But a CASE or IIF whose condition is a constant, or a
        // COALESCE whose first argument is a constant never NULL, is folded to the one value it gives, which keeps its JSON:
        // so whether such an expression nests as JSON depends on the optimiser, not the type.
        Assert.Equal(
            """[{"id":1,"lines":"[{\"q\":5},{\"q\":6}]","n":"[{\"q\":5},{\"q\":6}]"},{"id":2,"lines":"[]"}]""",
            await Json("""
                SELECT o.id, CASE WHEN o.id = 1 THEN (SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH) ELSE '[]' END AS lines,
                       NULLIF((SELECT l.q FROM #l AS l WHERE l.o = o.id FOR JSON PATH), '[]') AS n
                FROM #o AS o FOR JSON PATH
                """));
        Assert.Equal(
            """[{"a":[1],"b":"{\"a\":1}","c":{"a":1},"d":"{\"a\":1}","e":{"a":1}}]""",
            await Json("""
                SELECT COALESCE(JSON_QUERY(N'[1]'), '[]') AS a, COALESCE(JSON_OBJECT('a': 1), '[]') AS b, IIF(1 = 1, JSON_OBJECT('a': 1), '[]') AS c,
                       CHOOSE(1, JSON_OBJECT('a': 1), '[]') AS d, CASE WHEN 1 = 0 THEN '[]' ELSE JSON_OBJECT('a': 1) END AS e
                FOR JSON PATH
                """));
        Assert.Equal(
            """[{"f":"{\"a\":1}","g":"{\"a\":1}"}]""",
            (await Run("""
                DECLARE @p int = 1;
                SELECT (SELECT CASE WHEN @p = 1 THEN JSON_OBJECT('a': 1) ELSE '[]' END AS f, IIF(@p = 1, JSON_OBJECT('a': 1), '[]') AS g FOR JSON PATH) AS j;
                """)).ToString()["j: ".Length..]);
    }

    [Fact]
    public async Task Coalesce_reads_a_for_json_subquery_twice_so_it_can_be_null_though_its_fallback_is_not()
    {
        // As for any subquery (ExpressionObservationTests): COALESCE((SELECT … FOR JSON PATH), '[]') tests the subquery and
        // then reads it again, and the second read may be NULL. A subquery that is empty half the time makes COALESCE NULL
        // about a quarter of the time; ISNULL reads it once and is never NULL. The subquery reads the outer row, as real ones do:
        // one that does not may be read once and spooled, and then both are never NULL.
        var outcome = await Run("""
            SELECT SUM(CASE WHEN c IS NULL THEN 1 ELSE 0 END) AS coalesce_nulls, SUM(CASE WHEN i IS NULL THEN 1 ELSE 0 END) AS isnull_nulls
            FROM (SELECT COALESCE((SELECT s.n AS a WHERE ABS(CHECKSUM(NEWID())) % 2 = 0 FOR JSON PATH), '[]') AS c,
                         ISNULL((SELECT s.n AS a WHERE ABS(CHECKSUM(NEWID())) % 2 = 0 FOR JSON PATH), '[]') AS i
                  FROM (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS s) AS r;
            """);
        var counts = outcome.Sets[0]["coalesce_nulls,isnull_nulls: ".Length..].Split(',').Select(int.Parse).ToArray();
        Assert.True(counts[0] > 100, outcome.ToString());
        Assert.Equal(0, counts[1]);
    }

    [Fact]
    public async Task In_a_rowset_coalesce_and_isnull_over_json_are_nvarchar_max_text()
    {
        // A rowset column delivers JSON as text either way: the subquery's array, or the fallback.
        Assert.Equal(
            "a nvarchar(max), b nvarchar(max), c nvarchar(max)",
            await Describe("SELECT COALESCE((SELECT 1 AS q FOR JSON PATH), '[]') AS a, ISNULL((SELECT 1 AS q FOR JSON PATH), '[]') AS b, JSON_QUERY(COALESCE((SELECT 1 AS q FOR JSON PATH), '[]')) AS c"));
        Assert.Equal(
            "a,b: [],[{\"q\":5},{\"q\":6}]",
            (await Run($"{Orders}\nSELECT ISNULL((SELECT l.q FROM #l AS l WHERE l.o = 2 FOR JSON PATH), '[]') AS a, COALESCE((SELECT l.q FROM #l AS l WHERE l.o = 1 FOR JSON PATH), '[]') AS b;")).ToString());
    }
}

using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with JSON built in a procedure on the way to where it is read: through variables and tables,
/// as a literal, and under <c>json_query</c>, <c>OPENJSON</c> and <c>JSON_VALUE</c>. The rules in Haruspex.SqlServer.Inference's
/// README.md, "JSON built here, traced", rest on these. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class JsonTracingObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string call) => ObservedCall.Run(server, [], call);

    /// <summary>Every result set of a call that succeeds, joined by <c> | </c>.</summary>
    private async Task<string> Select(string call)
    {
        var outcome = await Run(call);
        Assert.Null(outcome.Error);
        return string.Join(" | ", outcome.Sets);
    }

    /// <summary>The error a call fails with.</summary>
    private async Task<int?> Error(string call) => (await Run(call)).Error;

    /// <summary>A one-row table, so that an expression over a literal is not folded when the batch is compiled.</summary>
    private const string One = "DECLARE @one TABLE (n int NOT NULL); INSERT @one VALUES (1);";

    // ---- literals ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_json_literal_is_read_exactly_and_a_bare_scalar_or_text_that_is_not_json_is_an_error()
    {
        // An object or array is embedded as it is written, spaces, trailing zeros and a repeated key kept; N'' and '' alike.
        Assert.Equal(
            """j: [{"a":[],"b":{},"c":[ 1 , "a" ],"d":{"k":1.50,"k":2},"e":[]}]""",
            await Select($"""{One} SELECT (SELECT json_query('[]') AS a, json_query(N'{"{}"}') AS b, json_query('  [ 1 , "a" ]  ') AS c, json_query('{"{"}"k":1.50,"k":2{"}"}') AS d, json_query(char(9) + '[]' + char(10)) AS e FROM @one FOR JSON PATH) AS j;"""));

        // A bare scalar, empty text and text that is not JSON are errors (13609), in lax mode too, for json_query, OPENJSON and
        // JSON_VALUE alike.
        foreach (var text in new[] { "'1'", "'\"s\"'", "'null'", "'true'", "''", "N''", "'x'", "'[1,'" })
        {
            Assert.Equal(13609, await Error($"{One} SELECT json_query({text}) FROM @one;"));
            Assert.Equal(13609, await Error($"{One} SELECT json_query({text}, 'lax $') FROM @one;"));
        }

        Assert.Equal(13609, await Error($"{One} SELECT j.[key] FROM @one CROSS APPLY OPENJSON(N'123') AS j;"));
        Assert.Equal(13609, await Error($"{One} SELECT j.[key] FROM @one CROSS APPLY OPENJSON(N'') AS j;"));
        Assert.Equal(13609, await Error($"{One} SELECT json_value('123', '$') FROM @one;"));

        // Only the first value is checked: text after it is ignored, so '[1]x' is read as [1].
        Assert.Equal("""j: [{"a":[1],"b":[1]}]""", await Select($"{One} SELECT (SELECT json_query('[1]x') AS a, json_query('[1] [2]') AS b FROM @one FOR JSON PATH) AS j;"));
        Assert.Equal("key,value,type: 0,1,2", await Select($"{One} SELECT j.[key], j.value, j.type FROM @one CROSS APPLY OPENJSON(N'[1]x') AS j;"));
    }

    [Fact]
    public async Task A_literal_without_n_is_converted_to_the_code_page_before_it_is_read_as_json()
    {
        // Under the test database's collation (Latin1, code page 1252): ā has no code point there and
        // becomes a, a character outside the BMP two question marks, a key too; an escape is decoded first, then converted. An
        // N'' literal is read as it is.
        Assert.Equal(
            """j: [{"a":["a"],"b":["a", "??"],"c":{"a": 1},"d":["ā"]}]""",
            await Select($$"""{{One}} SELECT (SELECT json_query('["ā"]') AS a, json_query('["ā", "😀"]') AS b, json_query('{"ā": 1}') AS c, json_query(N'["ā"]') AS d FROM @one FOR JSON PATH) AS j;"""));
        Assert.Equal("v: a", await Select($$"""{{One}} SELECT json_value('["ā"]', '$[0]') AS v FROM @one;"""));
    }

    [Fact]
    public async Task Under_json_query_coalesce_case_and_isnull_with_a_json_literal_give_the_json_of_the_branch_taken()
    {
        Assert.Equal(
            """j: [{"a":[],"b":[{"q":5}],"c":[],"d":{"k":[1]},"e":[1]}]""",
            await Select("""
                DECLARE @x nvarchar(max), @l nvarchar(max) = (SELECT 5 AS q FOR JSON PATH);
                SELECT (SELECT json_query(COALESCE(@x, '[]')) AS a, json_query(COALESCE(@l, '[]')) AS b,
                               json_query(CASE WHEN @x IS NULL THEN '[]' ELSE @x END) AS c,
                               json_query(IIF(@x IS NULL, '{"k":[1]}', @x)) AS d,
                               json_query(ISNULL(@x, '[1]')) AS e
                        FOR JSON PATH) AS j;
                """));

        // ISNULL over live JSON writes its fallback into the JSON as it is: a literal that is JSON is that JSON, and one that is
        // not makes text that is not JSON.
        Assert.Equal(
            """j: {"a":[1],"b":{"k":"x"},"c":[1]x}""",
            await Select("""SELECT json_object('a': ISNULL((SELECT 1 AS q WHERE 1 = 0 FOR JSON PATH), '[1]'), 'b': ISNULL((SELECT 1 AS q WHERE 1 = 0 FOR JSON PATH), '{"k":"x"}'), 'c': ISNULL((SELECT 1 AS q WHERE 1 = 0 FOR JSON PATH), '[1]x')) AS j;"""));
    }

    // ---- variables and tables -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Json_built_here_keeps_its_content_through_variables_and_tables_until_json_query()
    {
        // A variable holds JSON as text: escaped where it nests, JSON again under json_query.
        Assert.Equal(
            """j: [{"t":"[{\"a\":1}]","q":[{"a":1}],"o":{"k":2}}]""",
            await Select("""
                DECLARE @t nvarchar(max) = (SELECT 1 AS a FOR JSON PATH), @o nvarchar(max);
                SET @o = json_object('k': 2);
                SELECT (SELECT @t AS t, json_query(@t) AS q, json_query(@o) AS o FOR JSON PATH) AS j;
                """));

        // So does a table variable or temporary table column of nvarchar(max), and a column SELECT … INTO makes.
        Assert.Equal(
            """j: [{"v":[{"a":2}],"t":{"k":3},"s":[{"a":4}]}]""",
            await Select("""
                DECLARE @tv TABLE (j nvarchar(max) NULL);
                CREATE TABLE #t (j nvarchar(max) NULL);
                INSERT @tv (j) SELECT (SELECT 2 AS a FOR JSON PATH);
                INSERT #t (j) VALUES (json_object('k': 3));
                SELECT (SELECT 4 AS a FOR JSON PATH) AS j INTO #s;
                SELECT (SELECT json_query(v.j) AS v, json_query(t.j) AS t, json_query(s.j) AS s FROM @tv AS v CROSS JOIN #t AS t CROSS JOIN #s AS s FOR JSON PATH) AS j;
                """));

        // An assignment by a SELECT that matches no rows does not happen: the variable keeps what it held.
        Assert.Equal(
            "a,b: null,{\"k\":1}",
            await Select("""
                DECLARE @a nvarchar(max), @b nvarchar(max) = json_object('k': 1);
                SELECT @a = json_object('k': 2), @b = json_object('k': 2) FROM (SELECT 1 AS n) AS d WHERE d.n = 0;
                SELECT @a AS a, @b AS b;
                """));

        // A shorter type cuts the text short without an error, so it is no longer the JSON; varchar changes a character its code
        // page lacks into another (ā into a, under the test database's default collation).
        Assert.Equal(
            "s,v: [{\"a\":\"abc,[{\"a\":\"a\"}]",
            await Select("""
                DECLARE @s nvarchar(10) = (SELECT 'abcdefghij' AS a FOR JSON PATH);
                DECLARE @v varchar(max) = (SELECT nchar(257) AS a FOR JSON PATH);
                SELECT @s AS s, @v AS v;
                """));
    }

    [Fact]
    public async Task Select_into_makes_nvarchar_max_of_built_json_and_json_of_returning_json()
    {
        Assert.Equal(
            "name,t,max_length: a,nvarchar,-1;b,nvarchar,-1;c,json,-1;d,json,-1;e,nvarchar,8000",
            await Select("""
                SELECT (SELECT 1 AS a FOR JSON PATH) AS a, json_object('k': 1) AS b, json_object('k': 1 RETURNING json) AS c,
                       json_query(json_object('k': 1 RETURNING json)) AS d, json_query(N'[1]') AS e
                INTO #v;
                SELECT c.name, type_name(c.system_type_id) AS t, c.max_length FROM tempdb.sys.columns AS c WHERE c.object_id = object_id('tempdb..#v') ORDER BY c.column_id;
                """));
    }

    // ---- OPENJSON and JSON_VALUE --------------------------------------------------------------------------------------

    /// <summary>Two rows of one column of each type FOR JSON writes, the second with a NULL that FOR JSON leaves out.</summary>
    private const string Typed = """
        CREATE TABLE #s (i int NOT NULL, ti tinyint NOT NULL, big bigint NOT NULL, d decimal(9,2) NOT NULL, n nvarchar(9) NOT NULL,
                         v varchar(5) NOT NULL, c char(4) NOT NULL, b bit NOT NULL, dt date NOT NULL, d2 datetime2(3) NOT NULL,
                         dto datetimeoffset(2) NOT NULL, g uniqueidentifier NOT NULL, m money NOT NULL, note nvarchar(5) NULL);
        INSERT #s VALUES (300, 255, 9007199254740993, 12.50, N'alphabeta', 'abc', 'ab', 1, '2024-01-02', '2024-01-02T03:04:05.120',
                          '2024-01-02T03:04:05.12+01:00', '8E4F138A-FFF7-4067-B91C-1578FA1304F9', 1.5, NULL),
                         (-7, 0, -1, -3.00, N'b', 'x', 'abcd', 0, '0001-01-01', '9999-12-31T23:59:59.999',
                          '0001-01-01T00:00:00.00-14:00', '00000000-0000-0000-0000-000000000000', -922337203685477.5808, N'n');
        DECLARE @j nvarchar(max) = (SELECT s.i, s.ti, s.big, s.d, s.n, s.v, s.c, s.b, s.dt, s.d2, s.dto, s.g, s.m, s.note FROM #s AS s FOR JSON PATH);
        """;

    [Fact]
    public async Task Openjson_with_over_json_built_here_converts_its_own_types_exactly_and_cuts_strings_short()
    {
        // Each type back into itself: the value FOR JSON wrote, unchanged.
        var back = await Select($"""
            {Typed}
            SELECT CASE WHEN EXISTS (
                SELECT s.i, s.ti, s.big, s.d, s.n, s.v, s.c, s.b, s.dt, s.d2, s.dto, s.g, s.m, s.note FROM #s AS s
                EXCEPT
                SELECT * FROM OPENJSON(@j) WITH (i int, ti tinyint, big bigint, d decimal(9,2), n nvarchar(9), v varchar(5), c char(4), b bit,
                                                 dt date, d2 datetime2(3), dto datetimeoffset(2), g uniqueidentifier, m money, note nvarchar(5)))
                THEN 'changed' ELSE 'same' END AS r;
            """);
        Assert.Equal("r: same", back);

        // A string into a shorter type is cut short, never NULL; into a longer one, the same; an integer into a wider one, the
        // same number; a missing key under a lax path NULL, and an error under strict (13608).
        Assert.Equal(
            "n3,v9,ib,tib: alp,abc,300,255;b,x,-7,0",
            await Select($"{Typed} SELECT n3, v9, ib, tib FROM OPENJSON(@j) WITH (n3 nvarchar(3) '$.n', v9 nvarchar(9) '$.v', ib bigint '$.i', tib int '$.ti');"));
        Assert.Equal("note: null;n", await Select($"{Typed} SELECT note FROM OPENJSON(@j) WITH (note nvarchar(5));"));
        Assert.Equal(13608, await Error($"{Typed} SELECT note FROM OPENJSON(@j) WITH (note nvarchar(5) 'strict $.note');"));

        // A narrower number overflows (244), and a bit, written true or false, is not a number (245).
        Assert.Equal(244, await Error($"{Typed} SELECT i FROM OPENJSON(@j) WITH (i tinyint);"));
        Assert.Equal(245, await Error($"{Typed} SELECT b FROM OPENJSON(@j) WITH (b int);"));
    }

    [Fact]
    public async Task Openjson_with_does_not_pad_nchar_but_pads_char()
    {
        // "ab" into nchar(4) is 2 characters (4 bytes), into char(4) padded to 4.
        Assert.Equal(
            "n,nb,c,cb: ab,4,ab  ,4",
            await Select("""SELECT j.n, datalength(j.n) AS nb, j.c, datalength(j.c) AS cb FROM OPENJSON(N'[{"v":"ab"}]') WITH (n nchar(4) '$.v', c char(4) '$.v') AS j;"""));

        // nchar without a length is nchar(1), and no more padded: "" into it is empty.
        Assert.Equal(
            "nb: 0",
            await Select("""SELECT datalength(j.n) AS nb FROM OPENJSON(N'[{"v":""}]') WITH (n nchar '$.v') AS j;"""));

        // In a procedure's rowset the value may arrive padded to n with U+0000 (seen in the kits fixture's part_rows, reading a
        // variable) or not (this literal); in JSON it is its own length. So "at most n" is what holds.
        var call = await ObservedCall.Run(server, ["""CREATE PROCEDURE dbo.obs_nchar AS SELECT j.n FROM OPENJSON(N'[{"v":"ab"}]') WITH (n nchar(4) '$.v') AS j FOR JSON PATH;"""], "EXEC dbo.obs_nchar;");
        Assert.Null(call.Error);
        Assert.Equal(["""JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{"n":"ab"}]"""], call.Sets);

        // An nchar(n) column through FOR JSON is written padded, so back into nchar(n) it is n long.
        Assert.Equal(
            "n,nb: ab  ,8",
            await Select("""
                DECLARE @j nvarchar(max) = (SELECT CAST(N'ab' AS nchar(4)) AS n FOR JSON PATH);
                SELECT j.n, datalength(j.n) AS nb FROM OPENJSON(@j) WITH (n nchar(4)) AS j;
                """));
    }

    [Fact]
    public async Task Openjson_with_converts_a_varchar_to_the_databases_code_page()
    {
        // A Greek varchar goes out as α and comes back, as varchar of the database's collation, as a.
        Assert.Equal(
            "v: a",
            await Select("""
                CREATE TABLE #g (v varchar(10) COLLATE Greek_CI_AS NOT NULL);
                INSERT #g VALUES (N'α');
                DECLARE @j nvarchar(max) = (SELECT g.v FROM #g AS g FOR JSON PATH);
                SELECT j.v FROM OPENJSON(@j) WITH (v varchar(10)) AS j;
                """));
    }

    [Fact]
    public async Task Openjson_without_with_over_json_built_here_gives_its_keys_and_their_types()
    {
        // FOR JSON leaves a NULL out, so no key holds a JSON null; json_object's NULL ON NULL writes one.
        Assert.Equal(
            "key,type: b,3;big,2;c,1;d,2;d2,1;dt,1;dto,1;g,1;i,2;m,2;n,1;ti,2;v,1",
            await Select($"{Typed} SELECT j.[key], j.type FROM OPENJSON(@j, '$[0]') AS j ORDER BY j.[key];"));
        Assert.Equal(
            "key,value,type: 0,{\"a\":1,\"b\":null},5",
            await Select("SELECT j.[key], j.value, j.type FROM OPENJSON((SELECT json_array(json_object('a': 1, 'b': NULL)))) AS j;"));
    }

    [Fact]
    public async Task Json_value_over_json_built_here_gives_its_scalars_as_text()
    {
        // Strings as they are, true and false as words, numbers as FOR JSON wrote them; a JSON null is NULL even under strict.
        Assert.Equal(
            "n,b,f,d,x: alphabeta,true,false,12.50,null",
            await Select("""
                DECLARE @j nvarchar(max) = (SELECT N'alphabeta' AS n, CAST(1 AS bit) AS b, CAST(0 AS bit) AS f, CAST(12.50 AS decimal(9,2)) AS d FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
                DECLARE @k nvarchar(max) = json_object('x': NULL);
                SELECT json_value(@j, '$.n') AS n, json_value(@j, 'strict $.b') AS b, json_value(@j, '$.f') AS f, json_value(@j, '$.d') AS d, json_value(@k, 'strict $.x') AS x;
                """));
        Assert.Equal(13608, await Error("DECLARE @j nvarchar(max) = json_object('a': 1); SELECT json_value(@j, 'strict $.b');"));
    }

    // ---- conversions ------------------------------------------------------------------------------------------------

    [Theory]
    // Built JSON is nvarchar(max): converted to that, however, or to json, it is still JSON.
    [InlineData("CAST((SELECT 1 AS id FOR JSON PATH) AS nvarchar(max))", """{"j":[{"id":1}]}""")]
    [InlineData("CONVERT(nvarchar(max), (SELECT 1 AS id FOR JSON PATH))", """{"j":[{"id":1}]}""")]
    [InlineData("TRY_CAST((SELECT 1 AS id FOR JSON PATH) AS nvarchar(max))", """{"j":[{"id":1}]}""")]
    [InlineData("CAST(CAST((SELECT 1 AS id FOR JSON PATH) AS nvarchar(max)) AS nvarchar(max))", """{"j":[{"id":1}]}""")]
    [InlineData("CAST(json_object('id': 1) AS nvarchar(max))", """{"j":{"id":1}}""")]
    [InlineData("CAST((SELECT 1 AS id FOR JSON PATH) AS json)", """{"j":[{"id":1}]}""")]
    [InlineData("CAST(CAST((SELECT 1 AS id FOR JSON PATH) AS json) AS json)", """{"j":[{"id":1}]}""")]
    [InlineData("CAST((SELECT 1 AS id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS json)", """{"j":{"id":1}}""")]
    [InlineData("CONVERT(json, (SELECT 1 AS id FOR JSON PATH), 1)", """{"j":[{"id":1}]}""")]
    // json converted to nvarchar(max), anything converted with a style (even 0) to a string type, and anything to another
    // string type, is text, escaped.
    [InlineData("CONVERT(nvarchar(max), (SELECT 1 AS id FOR JSON PATH), 0)", """{"j":"[{\"id\":1}]"}""")]
    [InlineData("TRY_CONVERT(nvarchar(max), (SELECT 1 AS id FOR JSON PATH), 1)", """{"j":"[{\"id\":1}]"}""")]
    [InlineData("CAST(CAST((SELECT 1 AS id FOR JSON PATH) AS json) AS nvarchar(max))", """{"j":"[{\"id\":1}]"}""")]
    [InlineData("CAST(json_object('id': 1 RETURNING json) AS nvarchar(max))", """{"j":"{\"id\":1}"}""")]
    [InlineData("CAST((SELECT 1 AS id FOR JSON PATH) AS nvarchar(4000))", """{"j":"[{\"id\":1}]"}""")]
    [InlineData("CAST((SELECT 1 AS id FOR JSON PATH) AS varchar(max))", """{"j":"[{\"id\":1}]"}""")]
    [InlineData("CAST((SELECT 1 AS id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS nvarchar(max))", """{"j":"{\"id\":1}"}""")]
    // json_query's type follows its argument's: nvarchar(4000) over a literal, so it is JSON as that and text as nvarchar(max).
    [InlineData("CAST(json_query(N'{\"id\":1}') AS nvarchar(4000))", """{"j":{"id":1}}""")]
    [InlineData("CAST(json_query(N'{\"id\":1}') AS nvarchar(max))", """{"j":"{\"id\":1}"}""")]
    public async Task Converted_json_stays_json_only_as_its_own_type_or_json(string conversion, string expected)
    {
        Assert.Equal($"j: {expected}", await Select($"SELECT (SELECT {conversion} AS j FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j;"));
        Assert.Equal($"j: {expected}", await Select($"SELECT json_object('j': {conversion}) AS j;"));
    }

    [Fact]
    public async Task A_conversion_cuts_or_pads_json_as_any_string_and_varchar_replaces_what_its_code_page_lacks()
    {
        // As text in a rowset: nvarchar(max) holds it whole, a shorter type cuts it without an error, nchar pads it, and
        // varchar writes ? for a character the database's code page lacks. A style changes nothing. An empty subquery is NULL whatever the type.
        Assert.Equal(
            """m,c,p,v,e: [{"id":1,"n":"é☃"}],[{"id":1,",[{"id":1}]    ,[{"id":1,"n":"é?"}],null""",
            await Select("""
                SELECT CAST((SELECT 1 AS id, N'é☃' AS n FOR JSON PATH) AS nvarchar(max)) AS m,
                       CAST((SELECT 1 AS id, N'é☃' AS n FOR JSON PATH) AS nvarchar(10)) AS c,
                       CONVERT(nchar(14), (SELECT 1 AS id FOR JSON PATH), 1) AS p,
                       CAST((SELECT 1 AS id, N'é☃' AS n FOR JSON PATH) AS varchar(max)) AS v,
                       CAST((SELECT 1 AS id WHERE 1 = 0 FOR JSON PATH) AS nvarchar(max)) AS e;
                """));
    }
}

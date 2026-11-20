namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server makes of JSON objects assembled as text, <c>'{"id":' + … + '}'</c>: duplicate keys, keys with escapes and keys
/// built from data, a value that is NULL, a key left out by <c>concat_ws</c>, and brackets across pieces.
/// </summary>
public partial class JsonAssembledObservationTests
{
    [Fact]
    public async Task A_key_written_twice_is_kept_twice_and_readers_differ_on_which_wins()
    {
        // json_query keeps both writes, in FOR JSON, json_object and a variable alike, so a client parses both: JSON.parse keeps
        // the last (ECMA-262: JSON.parse writes each member in turn, so a later one replaces an earlier). The server's own readers take the first
        // (JSON_VALUE) or both (OPENJSON).
        Assert.Equal(
            """j,o,first: {"v":{"a":1,"a":"x"}},{"x":{"a":1,"a":"x"}},1""",
            await Select("""
                DECLARE @d nvarchar(max) = N'{"a":1,"a":"x"}';
                SELECT (SELECT json_query(@d) AS v FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j, json_object('x': json_query(@d)) AS o,
                    JSON_VALUE(@d, '$.a') AS first;
                """));
        Assert.Equal("key,value: a,1;a,x", await Select("SELECT [key], [value] FROM OPENJSON(N'{\"a\":1,\"a\":\"x\"}');"));
    }

    [Fact]
    public async Task A_key_written_with_escapes_is_read_as_the_name_they_make()
    {
        // The backslash is built on the server with nchar(92), so no client or tool on the way decodes an escape before the
        // server reads it. OPENJSON reads each key as the name it decodes to: a quotation mark, a backslash, and the letter e
        // from its escape. json_query, in FOR JSON, keeps a key as it is spelled: the escape of e stays six characters.
        Assert.Equal(
            """key,u: a"b,null;cBd,null;e,101""".Replace('B', '\\'),
            await Select("""
                DECLARE @b nchar(1) = nchar(92);
                SELECT [key], CASE WHEN len([key]) = 1 THEN unicode([key]) END AS u
                FROM OPENJSON(json_query(N'{"a' + @b + N'"b":' + convert(nvarchar(max), 1) + N',"c' + @b + @b + N'd":2,"' + @b + N'u0065":3}'));
                """));
        Assert.Equal(
            "length,kept: 12,1",
            await Select("""
                DECLARE @b nchar(1) = nchar(92);
                DECLARE @j nvarchar(max) = N'{"' + @b + N'u0065":3}';
                SELECT len(@j) AS length,
                    CASE WHEN (SELECT json_query(@j) AS v FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) = N'{"v":' + @j + N'}' THEN 1 ELSE 0 END AS kept;
                """));
    }

    [Fact]
    public async Task Keys_built_from_data_may_repeat_and_are_kept_as_written()
    {
        // string_escape makes each name a key that reads back as the name; two rows of one name write the key twice.
        Assert.Equal(
            """j: {"v":{"plain":1,"a\"b\\c":2,"plain":3}}""",
            await Select($$"""
                {{Tables}}
                INSERT @t VALUES (3, N'plain');
                SELECT (SELECT json_query(N'{' + (SELECT string_agg(N'"' + string_escape(name, 'json') + N'":' + convert(nvarchar(max), id), N',') WITHIN GROUP (ORDER BY id) FROM @t) + N'}') AS v
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j;
                """));
    }

    [Fact]
    public async Task A_null_value_leaves_an_object_null_under_plus_and_not_json_under_concat()
    {
        // '{"a":' + NULL + '}' is NULL: FOR JSON leaves the key out, json_object writes null. concat writes {"a":}, which
        // json_query refuses (13609) in FOR JSON, in json_object and assigned to a variable. coalesce(…, 'null') writes the key
        // with a JSON null.
        Assert.Equal(
            """j,o,coalesced: {},{"x":null},{"a":null}""",
            await Select("""
                DECLARE @x int = NULL;
                SELECT (SELECT json_query(N'{"a":' + convert(nvarchar(max), @x) + N'}') AS v FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j,
                    json_object('x': json_query(N'{"a":' + convert(nvarchar(max), @x) + N'}') NULL ON NULL) AS o,
                    json_query(N'{"a":' + coalesce(convert(nvarchar(max), @x), N'null') + N'}') AS coalesced;
                """));
        Assert.Equal(13609, await Error("""DECLARE @x int = NULL; SELECT json_query(concat(N'{"a":', @x, N'}')) AS v FOR JSON PATH;"""));
        Assert.Equal(13609, await Error("""DECLARE @x int = NULL; SELECT json_object('x': json_query(concat(N'{"a":', @x, N'}')));"""));
        Assert.Equal(13609, await Error("""DECLARE @x int = NULL; DECLARE @v nvarchar(max) = json_query(concat(N'{"a":', @x, N'}')); SELECT @v AS v;"""));
    }

    [Fact]
    public async Task Concat_ws_of_members_leaves_out_those_that_are_null_and_a_stray_comma_is_not_json()
    {
        // concat_ws skips a NULL member, so the key is left out, and all NULL leaves {}. A comma beside a brace, from an empty
        // member or a member left out after a literal comma, is refused.
        Assert.Equal(
            """one,none: {"a":1},{}""",
            await Select("""
                DECLARE @n nvarchar(10) = NULL;
                SELECT json_query(N'{' + concat_ws(N',', N'"a":1', @n) + N'}') AS one, json_query(N'{' + concat_ws(N',', @n, @n) + N'}') AS none;
                """));
        Assert.Equal(13609, await Error("""DECLARE @n nvarchar(10) = NULL; SELECT json_query(N'{"a":1,' + concat_ws(N',', @n, @n) + N'}') AS v;"""));
        Assert.Equal(13609, await Error("""DECLARE @e nvarchar(10) = N''; SELECT json_query(N'{' + concat_ws(N',', @e, N'"b":2') + N'}') AS v;"""));
        Assert.Equal(13609, await Error("SELECT json_query(N'{\"a\":1,}') AS v;"));
    }

    [Fact]
    public async Task Brackets_that_do_not_balance_are_not_json_and_text_after_the_brace_is_ignored()
    {
        // An array opened in an object's literal and closed in a later one balances when the middle is NULL under concat ([]),
        // and under + the whole is NULL. Brackets that never balance are refused; text after the closing brace is ignored, so the
        // read succeeds with the first object alone.
        Assert.Equal(
            """concat,plus,after,two: {"id":1,"lines":[]},null,{"a":1},{"a":1}""",
            await Select($$"""
                {{Tables}}
                SELECT json_query(concat(N'{"id":1,"lines":[', (SELECT string_agg(convert(nvarchar(max), id), N',') FROM @none), N']}')) AS concat,
                    json_query(N'{"id":1,"lines":[' + (SELECT string_agg(convert(nvarchar(max), id), N',') FROM @none) + N']}') AS plus,
                    json_query(N'{"a":' + convert(nvarchar(max), 1) + N'}x') AS after,
                    json_query(N'{"a":' + convert(nvarchar(max), 1) + N'},{"b":2}') AS two;
                """));
        Assert.Equal(13609, await Error("""SELECT json_query(N'{"lines":[' + convert(nvarchar(max), 1) + N'}') AS v;"""));
    }
}

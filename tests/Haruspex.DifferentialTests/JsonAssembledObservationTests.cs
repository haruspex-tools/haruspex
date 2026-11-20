using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server makes of JSON assembled as text: an array glued together with <c>+</c>, <c>concat</c>, <c>concat_ws</c> and
/// <c>string_agg</c> rather than built by <c>json_arrayagg</c>, <c>json_array</c> or <c>FOR JSON</c>. The rules in
/// Haruspex.SqlServer.Inference's README.md, "JSON assembled as text", rest on these. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class JsonAssembledObservationTests(SqlServerFixture server)
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

    /// <summary>
    /// A table of ids and names, and an empty one, so that an aggregate over them is computed when the batch runs rather than
    /// folded when it is compiled.
    /// </summary>
    private const string Tables = """
        DECLARE @t TABLE (id int NOT NULL, name nvarchar(20) NULL);
        INSERT @t VALUES (1, N'plain'), (2, N'a"b\c');
        DECLARE @none TABLE (id int NOT NULL, name nvarchar(20) NULL);
        """;

    // ---- an empty or NULL aggregate ---------------------------------------------------------------------------------

    [Fact]
    public async Task An_aggregate_over_no_rows_is_null_so_plus_is_null_concat_is_empty_and_quotes_in_concat_make_one_empty_string()
    {
        // string_agg over no rows is NULL. '[' + NULL + ']' is NULL, but concat counts NULL as empty, so it makes [], and with
        // the quotes in its literals it makes [""], an array of one empty string. coalesce(…, '') does what concat does.
        Assert.Equal(
            "plus,concat,quoted,coalesced: null,[],[\"\"],[]",
            await Select($"""
                {Tables}
                SELECT N'[' + (SELECT string_agg(convert(nvarchar(max), id), N',') FROM @none) + N']' AS plus,
                    concat(N'[', (SELECT string_agg(convert(nvarchar(max), id), N',') FROM @none), N']') AS concat,
                    concat(N'["', (SELECT string_agg(name, N'","') FROM @none), N'"]') AS quoted,
                    N'[' + coalesce((SELECT string_agg(convert(nvarchar(max), id), N',') FROM @none), N'') + N']' AS coalesced;
                """));

        // Over rows whose values are all NULL, string_agg skips them all and is NULL too.
        Assert.Equal(
            "plus,concat: null,[]",
            await Select($"""
                {Tables}
                SELECT N'[' + (SELECT string_agg(convert(nvarchar(max), NULLIF(id, id)), N',') FROM @t) + N']' AS plus,
                    concat(N'[', (SELECT string_agg(convert(nvarchar(max), NULLIF(id, id)), N',') FROM @t), N']') AS concat;
                """));
    }

    [Fact]
    public async Task With_concat_null_yields_null_off_plus_over_a_null_aggregate_is_empty_brackets()
    {
        // A caller's session may set CONCAT_NULL_YIELDS_NULL off, and then '[' + NULL + ']' is '[]', as concat makes it.
        Assert.Equal(
            "plus,quoted: [],[\"\"]",
            await Select($"""
                SET CONCAT_NULL_YIELDS_NULL OFF;
                {Tables}
                SELECT N'[' + (SELECT string_agg(convert(nvarchar(max), id), N',') FROM @none) + N']' AS plus,
                    N'["' + (SELECT string_agg(name, N'","') FROM @none) + N'"]' AS quoted;
                """));
    }

    // ---- a value in quotes -------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unescaped_value_in_quotes_can_make_more_elements_a_number_or_cut_the_array_short()
    {
        // The value a","b closes its string and opens another: two elements. a",1,"b puts a number among the strings. a"]x ends
        // the array, and json_query reads only the first value, so the rest is ignored and the read succeeds.
        Assert.Equal(
            """j: [{"two":["a","b"],"number":["a",1,"b"],"short":["a"]}]""",
            await Select("""
                DECLARE @v TABLE (two nvarchar(20), number nvarchar(20), short nvarchar(20));
                INSERT @v VALUES (N'a","b', N'a",1,"b', N'a"]x');
                SELECT (SELECT json_query(N'["' + two + N'"]') AS two, json_query(N'["' + number + N'"]') AS number,
                    json_query(N'["' + short + N'"]') AS short FROM @v FOR JSON PATH) AS j;
                """));

        // string_escape(x, 'json') escapes the quotation mark, the backslash and the control characters, so each value is one
        // string, read back exactly.
        Assert.Equal(
            """j: [{"names":["plain","a\"b\\c"],"tab":["x\ty"]}]""",
            await Select($"""
                {Tables}
                SELECT (SELECT json_query(N'[' + (SELECT string_agg(N'"' + string_escape(name, 'json') + N'"', N',') FROM @t) + N']') AS names,
                    json_query(N'["' + string_escape(N'x' + nchar(9) + N'y', 'json') + N'"]') AS tab FOR JSON PATH) AS j;
                """));
    }

    // ---- numbers as text -----------------------------------------------------------------------------------------------

    [Theory]
    // Integers, decimals and bit are written in full, a decimal with its scale and a leading zero, bigint past 2^53 too: a
    // JSON number of the value.
    [InlineData("CAST(-5 AS int)", "-5")]
    [InlineData("CAST(255 AS tinyint)", "255")]
    [InlineData("CAST(-32768 AS smallint)", "-32768")]
    [InlineData("CAST(9223372036854775807 AS bigint)", "9223372036854775807")]
    [InlineData("CAST(12.5 AS decimal(9,2))", "12.50")]
    [InlineData("CAST(0.12345 AS decimal(5,5))", "0.12345")]
    [InlineData("CAST(-0.5 AS numeric(5,2))", "-0.50")]
    [InlineData("CAST(1 AS bit)", "1")]
    // money and smallmoney are rounded to two places by default, and style 1 adds commas: [1,234.50] is two numbers. Styles 2
    // and 126 keep all four places.
    [InlineData("CAST(12.3456 AS money)", "12.35")]
    [InlineData("CAST(-1234567.3456 AS money), 2", "-1234567.3456")]
    [InlineData("CAST(-1234567.3456 AS money), 126", "-1234567.3456")]
    [InlineData("CAST(1234.5 AS money), 1", "1,234.50")]
    [InlineData("CAST(-3 AS smallmoney), 2", "-3.0000")]
    // float and real keep six significant digits by default, so the number differs from the value.
    [InlineData("CAST(1000000 AS float)", "1e+006")]
    [InlineData("CAST(3.14159265 AS float)", "3.14159")]
    [InlineData("CAST(3.14159265 AS real)", "3.14159")]
    public async Task A_number_converted_to_text_is_written_so(string converted, string text) =>
        Assert.Equal($"v: {text}", await Select($"SELECT convert(nvarchar(max), {converted}) AS v;"));

    [Fact]
    public async Task String_agg_and_concat_write_a_number_as_convert_with_no_style_does()
    {
        // An integer, a decimal and a bit converted implicitly, by string_agg or concat, are written as convert writes them.
        Assert.Equal(
            "ints,decimals,bits,money: -5,7,12.50|-0.50,1|0,12.35 | concatenated: [-5,1]",
            await Select("""
                DECLARE @n TABLE (i int, d decimal(9,2), b bit, m money);
                INSERT @n VALUES (-5, 12.5, 1, 12.3456), (7, -0.5, 0, NULL);
                SELECT string_agg(i, ',') WITHIN GROUP (ORDER BY i) AS ints, string_agg(d, '|') WITHIN GROUP (ORDER BY i) AS decimals,
                    string_agg(b, '|') WITHIN GROUP (ORDER BY i) AS bits, string_agg(m, '|') WITHIN GROUP (ORDER BY i) AS money
                FROM @n;
                SELECT concat('[', i, ',', b, ']') AS concatenated FROM @n WHERE i = -5;
                """));
    }

    [Fact]
    public async Task Live_json_in_plus_and_string_agg_is_its_text()
    {
        // A constructor and a FOR JSON subquery are nvarchar(max), whose text + and string_agg write as it is.
        Assert.Equal(
            """plus,agg: [{"id":1}],[{"id":1},{"id":2}]""",
            await Select($"""
                {Tables}
                SELECT N'[' + json_object('id': 1) + N']' AS plus,
                    N'[' + (SELECT string_agg(json_object('id': id), N',') WITHIN GROUP (ORDER BY id) FROM @t) + N']' AS agg;
                """));
    }

    [Fact]
    public async Task A_number_cast_to_text_too_short_is_an_error_in_nvarchar_and_a_star_in_varchar()
    {
        // CAST with no length is 30 characters, which a decimal of 38 digits overflows (8115). Into varchar too short, an int is *.
        Assert.Equal(8115, await Error("DECLARE @d decimal(38,2) = 123456789012345678901234567890123456.78; SELECT cast(@d AS nvarchar) AS v;"));
        Assert.Equal(8115, await Error("DECLARE @i int = 123456; SELECT cast(@i AS nvarchar(3)) AS v;"));
        Assert.Equal("v: *", await Select("DECLARE @i int = 123456; SELECT cast(@i AS varchar(3)) AS v;"));
    }

    // ---- fragments -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_null_fragment_in_concat_leaves_a_stray_comma_which_json_query_refuses_unless_it_is_the_only_one()
    {
        // concat counts NULL as empty: [,{"a":1}] and [{"a":1},] are not JSON, and json_query refuses them (13609) in FOR JSON,
        // in json_object and assigned to a variable. A lone NULL fragment leaves [], which is JSON.
        Assert.Equal(13609, await Error("""SELECT json_query(concat(N'[', CAST(NULL AS nvarchar(max)), N',', json_object('a': 1), N']')) AS x FOR JSON PATH;"""));
        Assert.Equal(13609, await Error("""SELECT json_object('a': json_query(concat(N'[', json_object('a': 1), N',', CAST(NULL AS nvarchar(max)), N']')));"""));
        Assert.Equal(13609, await Error("""DECLARE @x nvarchar(max) = json_query(concat(N'[', CAST(NULL AS nvarchar(max)), N',', N'{"a":1}', N']')); SELECT @x AS x;"""));
        Assert.Equal("""j: {"x":[]}""", await Select("SELECT (SELECT json_query(concat(N'[', CAST(NULL AS nvarchar(max)), N']')) AS x FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j;"));
    }

    [Fact]
    public async Task A_for_json_fragment_without_its_array_wrapper_holds_every_row_so_in_brackets_it_is_an_array_of_them()
    {
        // WITHOUT_ARRAY_WRAPPER over two rows writes {"id":1},{"id":2}, one value, so in brackets it is two elements, not one.
        Assert.Equal(
            """j: [{"id":1},{"id":2}]""",
            await Select($"""
                {Tables}
                SELECT json_query(concat(N'[', (SELECT id FROM @t ORDER BY id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N']')) AS j;
                """));
    }

    // A NULL separator doesn't make string_agg NULL: the values are written with nothing between them, so 1 and 2 make 12.
    [Fact]
    public async Task String_agg_with_a_null_separator_glues_its_values_together() =>
        Assert.Equal("j: [12]", await Select($"{Tables} SELECT N'[' + (SELECT string_agg(convert(nvarchar(max), id), NULL) WITHIN GROUP (ORDER BY id) FROM @t) + N']' AS j;"));

    // ---- concat_ws ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Concat_ws_skips_null_values_so_quoted_literals_make_any_subset_and_none_makes_empty_brackets()
    {
        Assert.Equal(
            """subset,none,blank: ["retail"],[],["retail",,"trade"]""",
            await Select("""
                DECLARE @a nvarchar(10) = NULL, @b nvarchar(10) = N'"retail"', @e nvarchar(10) = N'';
                SELECT N'[' + concat_ws(N',', @a, @b, @a) + N']' AS subset, N'[' + concat_ws(N',', @a, @a) + N']' AS none,
                    N'[' + concat_ws(N',', @b, @e, N'"trade"') + N']' AS blank;
                """));

        // An empty string is not skipped, and the stray comma it leaves is refused.
        Assert.Equal(13609, await Error("""DECLARE @e nvarchar(10) = N''; SELECT json_query(N'[' + concat_ws(N',', N'"a"', @e) + N']') AS j;"""));
    }

    // ---- reading it --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Json_query_of_text_that_is_not_json_is_an_error_wherever_it_is_and_text_read_without_it_is_a_string()
    {
        // Not JSON: an error (13609), never NULL, in FOR JSON, in json_object and assigned to a variable.
        Assert.Equal(13609, await Error("SELECT json_query(N'[1,') AS x FOR JSON PATH;"));
        Assert.Equal(13609, await Error("SELECT json_object('a': json_query(N'[1,'));"));
        Assert.Equal(13609, await Error("DECLARE @y nvarchar(max) = json_query(N'[1,'); SELECT @y AS y;"));

        // Without json_query, assembled text is escaped as a string wherever it nests.
        Assert.Equal(
            """j: {"a":"[1,2]","o":{"b":"[1,2]"}}""",
            await Select($"""
                {Tables}
                DECLARE @ids nvarchar(max) = N'[' + (SELECT string_agg(convert(nvarchar(max), id), N',') WITHIN GROUP (ORDER BY id) FROM @t) + N']';
                SELECT (SELECT @ids AS a, json_object('b': @ids) AS o FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j;
                """));
    }
}

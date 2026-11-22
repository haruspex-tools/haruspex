using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with a path into stored JSON, observed and kept as tests: the grammar of a path, and what
/// <c>JSON_VALUE</c>, <c>json_query</c> and <c>OPENJSON</c> give at each kind of step, under lax and strict paths. What a walk of a
/// declared type along a path rests on (docs/decisions/0097-a-path-into-json-of-known-shape-is-walked-step-by-step.md).
/// Observed on SQL Server 2025 RTM-CU10, which has no <c>RETURNING</c> clause for <c>JSON_VALUE</c> (it is a syntax error there).
/// </summary>
public partial class DeclaredColumnObservationTests
{
    private const string Shelf = """{"a":1.0,"a1":11,"_1":12,"h":{"i":1},"k":[10,20,30],"":"empty","q\"q":7,"é":4,"n":null,"e":{}}""";

    /// <summary><c>JSON_VALUE</c> of <see cref="Shelf"/>, stored, at <paramref name="path"/>.</summary>
    private Task<string?> ValueAt(string path) =>
        Stored(Shelf, $"SELECT JSON_VALUE(s.v, N'{path.Replace("'", "''", StringComparison.Ordinal)}') FROM dbo.obs_stored AS s;");

    [Theory]
    // lax and strict, lower case only, with or without a blank before $; blanks around the path and before a step.
    [InlineData("$.a", "1.0")]
    [InlineData("  lax   $.a", "1.0")]
    [InlineData("lax$.a", "1.0")]
    [InlineData("strict  $.a", "1.0")]
    [InlineData("$ . k [1]", "20")]
    [InlineData(" $.h.i ", "1")]
    // An unquoted key is ASCII letters, digits and _, not first a digit; a quoted key anything, \" a quote.
    [InlineData("$.a1", "11")]
    [InlineData("$._1", "12")]
    [InlineData("$.\"é\"", "4")]
    [InlineData("$.\"\"", "empty")]
    [InlineData("$.\"q\\\"q\"", "7")]
    [InlineData("$.\"h\".\"i\"", "1")]
    // An index is digits, leading zeros allowed, blanks inside the brackets allowed.
    [InlineData("$.k[01]", "20")]
    [InlineData("$.k[ 1 ]", "20")]
    public async Task A_path_is_read_as_the_server_reads_it(string path, string expected) =>
        Assert.Equal(expected, await ValueAt(path));

    [Theory]
    [InlineData("LAX $.a")]
    [InlineData("Strict $.a")]
    [InlineData("$.1a")]
    [InlineData("$.é")]
    [InlineData("$.$x")]
    [InlineData("$.a-b")]
    [InlineData("$.k[-1]")]
    [InlineData("$..i")]
    [InlineData("$[\"a\"]")]
    public async Task A_path_the_server_does_not_read_is_an_error_on_every_call(string path)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => ValueAt(path));
        Assert.Equal(13607, e.Number);
    }

    [Theory]
    // A wildcard or a range finds several values: JSON_VALUE gives NULL, json_query text that is not one JSON value. Not read.
    [InlineData("$.k[*]", null)]
    [InlineData("$.k[1 to 2]", null)]
    [InlineData("$.k[1 to 1]", "20")]
    public async Task A_wildcard_or_a_range_is_read_by_the_server_and_finds_several_values(string path, string? expected)
    {
        Assert.Equal(expected, await ValueAt(path));
        Assert.Equal("10,20,30", await Stored(Shelf, "SELECT json_query(s.v, '$.k[*]') FROM dbo.obs_stored AS s;"));
    }

    [Theory]
    [InlineData("$.k[last]")]
    [InlineData("$.k[0,1]")]
    public async Task Last_and_a_list_of_indexes_are_refused(string path)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => ValueAt(path));
        Assert.Equal(13660, e.Number);
    }

    [Theory]
    // Lax: NULL for JSON null, nothing there, an object or array, a key of an array or a scalar, an index of an object.
    [InlineData("$.n", null, null)]
    [InlineData("$.zz", null, 13608)]
    [InlineData("$.k[3]", null, 13608)]
    [InlineData("$.h", null, 13623)]
    [InlineData("$.k", null, 13623)]
    [InlineData("$.k.x", null, 13608)]
    [InlineData("$.a.b", null, 13608)]
    [InlineData("$.h[0]", null, 13608)]
    public async Task Json_value_gives_null_for_what_it_cannot_return_under_lax_and_errs_under_strict(string path, string? lax, int? strictError)
    {
        Assert.Equal(lax, await Stored(Shelf, $"SELECT JSON_VALUE(s.v, '{path}') FROM dbo.obs_stored AS s;"));
        var strict = $"SELECT JSON_VALUE(s.v, 'strict {path}') FROM dbo.obs_stored AS s;";
        if (strictError is { } number)
            Assert.Equal(number, (await Assert.ThrowsAsync<SqlException>(() => Stored(Shelf, strict))).Number);
        else
            Assert.Null(await Stored(Shelf, strict));
    }

    [Theory]
    // A number is its text as written; a string its text, unescaped; true and false their names.
    [InlineData("""{"x":1.0}""", "1.0")]
    [InlineData("""{"x":1e3}""", "1e3")]
    [InlineData("""{"x":-0}""", "-0")]
    [InlineData("""{"x":12345678901234567890}""", "12345678901234567890")]
    [InlineData("""{"x":"a\"bé"}""", "a\"bé")]
    [InlineData("""{"x":true}""", "true")]
    public async Task Json_value_of_a_number_is_its_text_as_written(string text, string expected) =>
        Assert.Equal(expected, await Stored(text, "SELECT JSON_VALUE(s.v, '$.x') FROM dbo.obs_stored AS s;"));

    [Fact]
    public async Task Json_value_counts_its_4000_characters_in_utf_16_code_units_after_unescaping()
    {
        static string Of(string value, int count) => $$"""{"x":"{{string.Concat(Enumerable.Repeat(value, count))}}"}""";
        const string Query = "SELECT DATALENGTH(JSON_VALUE(s.v, '$.x')) FROM dbo.obs_stored AS s;";

        Assert.Equal("8000", await Stored(Of("x", 4000), Query));
        Assert.Null(await Stored(Of("x", 4001), Query));
        Assert.Equal("8000", await Stored(Of("😀", 2000), Query));
        Assert.Null(await Stored(Of("😀", 2001), Query));
        Assert.Equal("6000", await Stored(Of("\\u0041", 3000), Query));

        // A number's text is counted too; under strict, what is too long is an error.
        Assert.Null(await Stored($$"""{"x":1.{{new string('0', 3999)}}}""", Query));
        Assert.Equal(13625, (await Assert.ThrowsAsync<SqlException>(() => Stored(Of("x", 4001), "SELECT JSON_VALUE(s.v, 'strict $.x') FROM dbo.obs_stored AS s;"))).Number);
    }

    [Theory]
    [InlineData("nvarchar(max)")]
    [InlineData("varchar(100)")]
    [InlineData("json")]
    public async Task Json_value_is_nvarchar_4000_over_every_type_of_document(string type) =>
        Assert.Equal("nvarchar(4000)", await Stored("{}", $"""
            SELECT system_type_name FROM sys.dm_exec_describe_first_result_set(N'SELECT JSON_VALUE(s.v, ''$.a'') FROM dbo.obs_stored AS s', NULL, 0);
            """, type));

    [Theory]
    // An object or an array is given as it is there; JSON null is NULL under either mode.
    [InlineData("$.h", """{"i":1}""", null)]
    [InlineData("$.k", "[10,20,30]", null)]
    [InlineData("$.n", null, null)]
    // Nothing there, or a scalar, is NULL under lax and an error under strict.
    [InlineData("$.zz", null, 13608)]
    [InlineData("$.a", null, 13624)]
    [InlineData("$.k[0]", null, 13624)]
    [InlineData("$.a.b", null, 13608)]
    public async Task Json_query_with_a_path_gives_the_object_or_array_there_and_null_under_lax_for_anything_else(string path, string? lax, int? strictError)
    {
        Assert.Equal(lax, await Stored(Shelf, $"SELECT json_query(s.v, '{path}') FROM dbo.obs_stored AS s;"));
        var strict = $"SELECT json_query(s.v, 'strict {path}') FROM dbo.obs_stored AS s;";
        if (strictError is { } number)
            Assert.Equal(number, (await Assert.ThrowsAsync<SqlException>(() => Stored(Shelf, strict))).Number);
        else
            Assert.Equal(lax, await Stored(Shelf, strict));
    }

    [Theory]
    [InlineData("$.k", "3", null)]
    [InlineData("$.h", "1", null)]
    // An object of no keys has no rows, and under strict is refused as a scalar is (json_query gives it, under strict too).
    [InlineData("$.e", "0", 13611)]
    // Nothing there, JSON null or a scalar: no rows under lax, an error under strict.
    [InlineData("$.zz", "0", 13608)]
    [InlineData("$.n", "0", 13611)]
    [InlineData("$.a", "0", 13611)]
    public async Task Openjson_opens_only_an_object_or_array_at_its_path(string path, string laxRows, int? strictError)
    {
        Assert.Equal(laxRows, await Stored(Shelf, $"SELECT count(*) FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v, '{path}') AS j;"));
        var strict = $"SELECT count(*) FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v, 'strict {path}') AS j;";
        if (strictError is { } number)
            Assert.Equal(number, (await Assert.ThrowsAsync<SqlException>(() => Stored(Shelf, strict))).Number);
        else
            Assert.Equal(laxRows, await Stored(Shelf, strict));
    }

    [Theory]
    // JSON null is NULL under strict too. Nothing there is an error, and so is an object for a scalar column, and a scalar for an
    // AS JSON column; under lax each is NULL.
    [InlineData("""{"n":null}""", "n int 'strict $.n'", null, null)]
    [InlineData("""{"n":{}}""", "n int 'strict $.n'", null, 13624)]
    [InlineData("""{"n":{}}""", "n int '$.n'", null, null)]
    [InlineData("""{"m":1}""", "n int 'strict $.n'", null, 13608)]
    [InlineData("""{"m":1}""", "n int '$.n'", null, null)]
    [InlineData("""{"n":null}""", "n nvarchar(max) 'strict $.n' AS JSON", null, null)]
    [InlineData("""{"n":1}""", "n nvarchar(max) 'strict $.n' AS JSON", null, 13624)]
    [InlineData("""{"n":1}""", "n nvarchar(max) '$.n' AS JSON", null, null)]
    public async Task Openjson_with_under_a_strict_path_errs_where_nothing_is_there_but_gives_null_for_null(string text, string column, string? expected, int? error)
    {
        var query = $"SELECT j.n FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v) WITH ({column}) AS j;";
        if (error is { } number)
            Assert.Equal(number, (await Assert.ThrowsAsync<SqlException>(() => Stored(text, query))).Number);
        else
            Assert.Equal(expected, await Stored(text, query));
    }

    [Theory]
    // $ is the row itself, so a row that is JSON null is NULL there, under a strict path too; below $ it is nothing (13608).
    [InlineData("[1,null]", "n int 'strict $'", "1", null)]
    [InlineData("""[{"a":1},null]""", "n nvarchar(max) 'strict $' AS JSON", """{"a":1}""", null)]
    public async Task Openjson_with_at_strict_dollar_gives_a_null_row_as_null(string text, string column, string first, string? second)
    {
        Assert.Equal(first, await Stored(text, $"SELECT j.n FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v) WITH ({column}) AS j ORDER BY CASE WHEN j.n IS NULL THEN 1 ELSE 0 END OFFSET 0 ROWS;"));
        Assert.Equal(second, await Stored(text, $"SELECT j.n FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v) WITH ({column}) AS j ORDER BY CASE WHEN j.n IS NULL THEN 1 ELSE 0 END OFFSET 1 ROWS;"));
        Assert.Equal(13608, (await Assert.ThrowsAsync<SqlException>(() => Stored(text, "SELECT j.n FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v) WITH (n int 'strict $.a') AS j;"))).Number);
    }

    [Theory]
    // A value that doesn't convert is an error, under the callers' ANSI_WARNINGS and ARITHABORT off as well.
    [InlineData("\"abc\"", "int", false, 245)]
    [InlineData("1.5", "int", false, 245)]
    [InlineData("1e3", "bigint", false, 8114)]
    [InlineData("\"abc\"", "bit", false, 245)]
    [InlineData("\"abc\"", "decimal(5,1)", false, 8114)]
    [InlineData("\"2026-13-45\"", "date", false, 241)]
    [InlineData("\"abc\"", "datetime2", false, 241)]
    [InlineData("\"abc\"", "uniqueidentifier", false, 8169)]
    [InlineData("\"abc\"", "int", true, 245)]
    [InlineData("\"2026-13-45\"", "date", true, 241)]
    // Overflow is an error, but NULL under those options; so is a datetime out of range.
    [InlineData("3000000000", "int", false, 248)]
    [InlineData("12345", "decimal(5,1)", false, 8115)]
    [InlineData("\"2026-13-45\"", "datetime", false, 242)]
    public async Task Openjson_with_errs_on_a_value_that_does_not_convert_but_gives_null_for_overflow_under_the_callers_options(string value, string type, bool optionsOff, int error)
    {
        var query = $"{(optionsOff ? "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " : "")}SELECT j.n FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v) WITH (n {type} '$.n') AS j;";
        Assert.Equal(error, (await Assert.ThrowsAsync<SqlException>(() => Stored($$"""{"n":{{value}}}""", query))).Number);
    }

    [Theory]
    [InlineData("3000000000", "int")]
    [InlineData("12345", "decimal(5,1)")]
    [InlineData("1e400", "float")]
    [InlineData("\"99999999999999999\"", "money")]
    [InlineData("\"2026-13-45\"", "datetime")]
    [InlineData("\"1700-01-01\"", "smalldatetime")]
    public async Task Openjson_with_gives_null_for_overflow_under_the_callers_ansi_warnings_and_arithabort_off(string value, string type) =>
        Assert.Null(await Stored($$"""{"n":{{value}}}""", $"SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT j.n FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v) WITH (n {type} '$.n') AS j;"));

    [Theory]
    // A bit takes any number as 1, and a string type any value, cut short: never NULL.
    [InlineData("12345", "bit", "True")]
    [InlineData("true", "nvarchar(2)", "tr")]
    [InlineData("12345", "varchar(2)", "12")]
    public async Task Openjson_with_into_bit_or_a_string_never_gives_null_for_a_scalar(string value, string type, string expected) =>
        Assert.Equal(expected, await Stored($$"""{"n":{{value}}}""", $"SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT j.n FROM dbo.obs_stored AS s CROSS APPLY OPENJSON(s.v) WITH (n {type} '$.n') AS j;"));
}

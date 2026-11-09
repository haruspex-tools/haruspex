using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server's JSON constructors (<c>json_object</c>, <c>json_array</c>, <c>json_arrayagg</c>,
/// <c>json_objectagg</c>) actually write, observed and kept as tests, as <see cref="ForJsonObservationTests"/> does for
/// FOR JSON. The inferencer's claims about the constructors (ProcedureAnalyzer, "JSON constructors") and the IR's
/// README rest on these; none was taken from the documentation alone. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
/// <remarks>
/// <c>json_object</c> and <c>json_array</c> exist from SQL Server 2022, the aggregates from SQL Server 2025 (and Azure
/// SQL). The test server is 2025, so every test here runs there.
/// </remarks>
public class JsonConstructorObservationTests(SqlServerFixture server)
{
    /// <summary>Runs one query and returns the first column of each row of its only result set.</summary>
    private async Task<IReadOnlyList<string?>> Rows(string sql)
    {
        server.Require();
        await using var connection = new SqlConnection(server.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
#pragma warning disable CA2100 // The tests' own literal SQL.
        await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<string?>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            rows.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        Assert.False(await reader.NextResultAsync(TestContext.Current.CancellationToken), "one result set");
        return rows;
    }

    private async Task<string?> One(string sql) => Assert.Single(await Rows(sql));

    private async Task<string?> Error(string sql)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Rows(sql));
        return e.Message;
    }

    // ---- nulls under each option ---------------------------------------------------------------------

    [Theory]
    // NULL ON NULL is json_object's default: the key stays, with null. ABSENT ON NULL leaves it out.
    [InlineData("json_object('a': 1, 'b': NULL)", """{"a":1,"b":null}""")]
    [InlineData("json_object('a': 1, 'b': NULL NULL ON NULL)", """{"a":1,"b":null}""")]
    [InlineData("json_object('a': 1, 'b': NULL ABSENT ON NULL)", """{"a":1}""")]
    [InlineData("json_object()", "{}")]
    // ABSENT ON NULL is json_array's default: a null element is dropped, and the others close up. NULL ON NULL keeps
    // every position.
    [InlineData("json_array(1, NULL, 'x')", """[1,"x"]""")]
    [InlineData("json_array(1, NULL, 'x' ABSENT ON NULL)", """[1,"x"]""")]
    [InlineData("json_array(1, NULL, 'x' NULL ON NULL)", """[1,null,"x"]""")]
    [InlineData("json_array(NULL)", "[]")]
    [InlineData("json_array()", "[]")]
    public async Task A_constructor_keeps_or_drops_nulls_by_its_option(string expression, string expected) =>
        Assert.Equal(expected, await One($"SELECT {expression};"));

    [Theory]
    // json_arrayagg's default is ABSENT ON NULL, like json_array's; json_objectagg's is NULL ON NULL, like json_object's.
    [InlineData("json_arrayagg(v)", "[1,3]")]
    [InlineData("json_arrayagg(v ABSENT ON NULL)", "[1,3]")]
    [InlineData("json_arrayagg(v NULL ON NULL)", "[1,null,3]")]
    [InlineData("json_objectagg(k: v)", """{"a":1,"b":null,"c":3}""")]
    [InlineData("json_objectagg(k: v NULL ON NULL)", """{"a":1,"b":null,"c":3}""")]
    [InlineData("json_objectagg(k: v ABSENT ON NULL)", """{"a":1,"c":3}""")]
    public async Task An_aggregate_keeps_or_drops_nulls_by_its_option(string expression, string expected) =>
        Assert.Equal(expected, await One($"SELECT {expression} FROM (VALUES ('a', 1, 1), ('b', NULL, 2), ('c', 3, 3)) AS t (k, v, o);"));

    [Theory]
    [InlineData("json_arrayagg(v)", "[]")]
    [InlineData("json_arrayagg(v NULL ON NULL)", "[null,null]")]
    [InlineData("json_objectagg(k: v ABSENT ON NULL)", "{}")]
    public async Task An_aggregate_over_only_nulls_is_empty_or_holds_nulls(string expression, string expected) =>
        Assert.Equal(expected, await One($"SELECT {expression} FROM (VALUES ('a', CAST(NULL AS int)), ('b', NULL)) AS t (k, v);"));

    // ---- empty aggregates ----------------------------------------------------------------------------

    [Theory]
    [InlineData("json_arrayagg(v)")]
    [InlineData("json_arrayagg(v NULL ON NULL)")]
    [InlineData("json_arrayagg(v ABSENT ON NULL)")]
    [InlineData("json_objectagg(k: v)")]
    [InlineData("json_objectagg(k: v ABSENT ON NULL)")]
    public async Task An_aggregate_over_no_rows_is_null_not_empty(string expression) =>
        // NULL, not [] or {}: so an aggregate without GROUP BY is nullable, with the reason empty-aggregate.
        Assert.Null(await One($"SELECT {expression} FROM (VALUES ('a', 1)) AS t (k, v) WHERE 1 = 0;"));

    [Theory]
    [InlineData("GROUP BY t.k")]
    [InlineData("GROUP BY ROLLUP (t.k)")]
    [InlineData("GROUP BY ()")]
    public async Task With_group_by_no_rows_give_no_groups_at_all(string groupBy) =>
        // Every group SQL Server returns has at least one row, even with ROLLUP or the empty grouping set, so under
        // GROUP BY an aggregate is never null for want of rows.
        Assert.Empty(await Rows($"SELECT json_arrayagg(t.v) FROM (VALUES ('a', 1)) AS t (k, v) WHERE 1 = 0 {groupBy};"));

    [Fact]
    public async Task A_correlated_aggregate_subquery_with_no_rows_is_null() =>
        Assert.Equal(
            ["""{"id":1,"lines":[10]}""", """{"id":2,"lines":null}"""],
            await Rows("""
                SELECT json_object('id': o.id, 'lines': (SELECT json_arrayagg(l.x) FROM (VALUES (1, 10)) AS l (o, x) WHERE l.o = o.id))
                FROM (VALUES (1), (2)) AS o (id) ORDER BY o.id;
                """));

    [Fact]
    public async Task Json_arrayagg_follows_its_order_by() =>
        Assert.Equal("[3,2,1]", await One("SELECT json_arrayagg(v ORDER BY v DESC) FROM (VALUES (1), (3), (2)) AS t (v);"));

    // ---- keys ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Json_object_writes_a_repeated_literal_key_twice() =>
        // Not an error: two keys of one name, which JSON.parse reads as the last. So duplicate keys are not modelled.
        Assert.Equal("""{"a":1,"a":2}""", await One("SELECT json_object('a': 1, 'a': 2);"));

    [Fact]
    public async Task Keys_that_differ_in_case_are_different_keys() =>
        Assert.Equal("""{"a":1,"A":2}""", await One("SELECT json_object('a': 1, 'A': 2);"));

    [Fact]
    public async Task A_key_can_be_any_expression() =>
        // So a key that is not a literal makes the object's keys data, which a fixed shape cannot state.
        Assert.Equal("""{"kx":1}""", await One("DECLARE @k nvarchar(10) = N'k'; SELECT json_object(@k + N'x': 1);"));

    [Theory]
    [InlineData("SELECT json_object(NULL: 1);")]
    [InlineData("SELECT json_objectagg(k: v) FROM (VALUES (CAST(NULL AS nvarchar(10)), 1)) AS t (k, v);")]
    public async Task A_null_key_is_an_error_not_a_value(string sql) =>
        Assert.Contains("cannot be null", await Error(sql), StringComparison.Ordinal);

    [Fact]
    public async Task Json_objectagg_keeps_every_duplicate_key() =>
        // Neither an error nor the last one only: both are written. JSON.parse keeps the last, so a record of string to
        // the value's type still describes what an application sees.
        Assert.Equal("""{"a":1,"a":2}""", await One("SELECT json_objectagg(k: v) FROM (VALUES ('a', 1), ('a', 2)) AS t (k, v);"));

    [Fact]
    public async Task Json_objectagg_writes_a_non_string_key_as_its_text() =>
        Assert.Equal("""{"10":1}""", await One("SELECT json_objectagg(k: v) FROM (VALUES (10, 1)) AS t (k, v);"));

    // ---- what is embedded as JSON and what is escaped ------------------------------------------------

    [Theory]
    [InlineData("json_object('v': json_object('a': 1))", """{"v":{"a":1}}""")]
    [InlineData("json_object('v': json_array(1, 2))", """{"v":[1,2]}""")]
    [InlineData("json_object('v': json_query(N'[1,2]'))", """{"v":[1,2]}""")]
    [InlineData("json_object('v': (SELECT 1 AS x FOR JSON PATH))", """{"v":[{"x":1}]}""")]
    [InlineData("json_object('v': json_query((SELECT 1 AS x FOR JSON PATH)))", """{"v":[{"x":1}]}""")]
    [InlineData("json_object('v': (SELECT 1 AS x WHERE 1 = 0 FOR JSON PATH))", """{"v":null}""")]
    [InlineData("json_object('v': json_object('a': 1 RETURNING json))", """{"v":{"a":1}}""")]
    [InlineData("json_array(json_object(), json_array(), (SELECT 1 AS x FOR JSON PATH))", """[{},[],[{"x":1}]]""")]
    // Any other string is text, escaped, even when it holds JSON.
    [InlineData("json_object('v': N'[1,2]')", """{"v":"[1,2]"}""")]
    [InlineData("json_array(N'{\"a\":1}')", """["{\"a\":1}"]""")]
    public async Task Constructors_and_json_query_nest_as_json_and_other_strings_as_text(string expression, string expected) =>
        Assert.Equal(expected, await One($"SELECT {expression};"));

    [Fact]
    public async Task A_constructor_through_a_variable_is_text() =>
        // The JSON-ness of a value does not survive a variable: it is an nvarchar(max) like any other.
        Assert.Equal("""{"v":"{\"a\":1}"}""", await One("DECLARE @s nvarchar(max) = json_object('a': 1); SELECT json_object('v': @s);"));

    [Fact]
    public async Task A_json_typed_value_nests_as_json_and_its_cast_to_text_does_not() =>
        Assert.Equal(
            """{"j":{"x":[1,2]},"s":"{\"x\":[1,2]}"}""",
            await One("""DECLARE @j json = '{"x":[1,2]}'; SELECT json_object('j': @j, 's': CAST(@j AS nvarchar(max)));"""));

    [Fact]
    public async Task A_json_typed_column_nests_as_json_in_for_json() =>
        Assert.Equal(
            """[{"j":{"x":[1,2]}},{}]""",
            await One("""
                CREATE TABLE #j (n int NOT NULL, j json NULL);
                INSERT INTO #j VALUES (1, '{"x":[1,2]}'), (2, NULL);
                SELECT j.j FROM #j AS j ORDER BY j.n FOR JSON PATH;
                """));

    [Fact]
    public async Task A_constructor_in_for_json_path_nests_as_json_also_under_a_dotted_alias() =>
        Assert.Equal(
            """[{"o":{"a":1},"arr":[1,2],"q":{"a":1},"p":{"x":{"b":2}}}]""",
            await One("SELECT json_object('a': 1) AS o, json_array(1, 2) AS arr, json_query(json_object('a': 1)) AS q, json_object('b': 2) AS [p.x] FOR JSON PATH;"));

    [Fact]
    public async Task An_aggregate_subquery_in_for_json_path_nests_as_json_and_is_left_out_when_null() =>
        Assert.Equal(
            """[{"id":1,"lines":[10]},{"id":2}]""",
            await One("""
                SELECT o.id, (SELECT json_arrayagg(l.x) FROM (VALUES (1, 10)) AS l (o, x) WHERE l.o = o.id) AS lines
                FROM (VALUES (1), (2)) AS o (id) ORDER BY o.id FOR JSON PATH;
                """));

    [Fact]
    public async Task Constructors_nest_several_levels_deep() =>
        Assert.Equal(
            """[{"a":[{"b":[1,{"c":null}]}]}]""",
            await One("SELECT json_array(json_object('a': json_array(json_object('b': json_array(1, json_object('c': NULL))))));"));

    // ---- what the constructors return ----------------------------------------------------------------

    [Fact]
    public async Task Constructors_return_nvarchar_max_unless_returning_json() =>
        Assert.Equal(
            ["array nvarchar -1", "arrayagg nvarchar -1", "object nvarchar -1", "objectagg nvarchar -1", "returning json -1"],
            await Rows("""
                SELECT json_object('a': 1) AS [object], json_array(1) AS [array], json_arrayagg(1) AS [arrayagg],
                       json_objectagg('k': 1) AS [objectagg], json_object('a': 1 RETURNING json) AS [returning]
                INTO #r;
                SELECT c.name + N' ' + ty.name + N' ' + CAST(c.max_length AS nvarchar(10))
                FROM tempdb.sys.columns AS c JOIN tempdb.sys.types AS ty ON ty.user_type_id = c.user_type_id
                WHERE c.object_id = object_id(N'tempdb..#r') ORDER BY c.name;
                """));

    // ---- scalars inside the constructors -------------------------------------------------------------

    private const string Scalars = """
        DECLARE @d date = '2026-03-04', @t3 time(3) = '05:06:07.1', @t7 time(7) = '05:06:07',
                @dt2 datetime2(7) = '2026-03-04T05:06:07.1', @dt2z datetime2(7) = '2026-03-04T05:06:07', @dt20 datetime2(0) = '2026-03-04T05:06:07',
                @dto datetimeoffset(3) = '2026-03-04T05:06:07.5+00:00', @dto0 datetimeoffset(0) = '2026-03-04T05:06:07-05:30',
                @dt datetime = '2026-03-04T05:06:07.123', @dtz datetime = '2026-03-04T05:06:07', @sdt smalldatetime = '2026-03-04T05:06:00',
                @g uniqueidentifier = '0e984725-c51c-4bf4-9960-e1c80e27aba0', @b1 bit = 1, @b0 bit = 0,
                @dec decimal(9,2) = 12.5, @dec0 decimal(9,2) = 0, @m money = 12.5, @sm smallmoney = -1, @bi bigint = 9223372036854775807,
                @c char(5) = 'ab', @nc nchar(4) = N'é';
        """;

    private const string ScalarList = "@d, @t3, @t7, @dt2, @dt2z, @dt20, @dto, @dto0, @dt, @dtz, @sdt, @g, @b1, @b0, @dec, @dec0, @m, @sm, @bi, @c, @nc";

    /// <summary>
    /// The same strings FOR JSON writes (ForJsonObservationTests, the IR's README "Scalars in JSON"): a fraction of the
    /// type's scale, left out when zero; a zero offset as Z; a GUID in upper case; bit as true or false; decimals with
    /// their scale; bigint in full; char(n) padded.
    /// </summary>
    private const string Expected =
        "\"2026-03-04\",\"05:06:07.100\",\"05:06:07\",\"2026-03-04T05:06:07.1000000\",\"2026-03-04T05:06:07\",\"2026-03-04T05:06:07\","
        + "\"2026-03-04T05:06:07.500Z\",\"2026-03-04T05:06:07-05:30\",\"2026-03-04T05:06:07.123\",\"2026-03-04T05:06:07\",\"2026-03-04T05:06:00\","
        + "\"0E984725-C51C-4BF4-9960-E1C80E27ABA0\",true,false,12.50,0.00,12.5000,-1.0000,9223372036854775807,\"ab   \",\"é   \"";

    [Fact]
    public async Task Json_array_formats_each_scalar_as_for_json_does() =>
        Assert.Equal($"[{Expected}]", await One($"{Scalars} SELECT json_array({ScalarList});"));

    [Fact]
    public async Task Json_object_formats_each_scalar_as_for_json_does()
    {
        var names = ScalarList.Split(", ").Select(n => n[1..]).ToList();
        var pairs = string.Join(", ", names.Select(n => $"'{n}': @{n}"));
        var values = Expected.Split(',');

        Assert.Equal(
            "{" + string.Join(",", names.Select((n, i) => $"\"{n}\":{values[i]}")) + "}",
            await One($"{Scalars} SELECT json_object({pairs});"));
    }

    [Fact]
    public async Task For_json_formats_them_the_same_way()
    {
        var names = ScalarList.Split(", ").Select(n => n[1..]).ToList();
        var values = Expected.Split(',');

        Assert.Equal(
            "{" + string.Join(",", names.Select((n, i) => $"\"{n}\":{values[i]}")) + "}",
            await One($"{Scalars} SELECT {string.Join(", ", names.Select(n => $"@{n} AS [{n}]"))} FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"));
    }

    [Fact]
    public async Task The_aggregates_format_each_scalar_as_for_json_does()
    {
        var names = ScalarList.Split(", ").Select(n => n[1..]).ToList();
        var values = Expected.Split(',');
        for (var i = 0; i < names.Count; i++)
        {
            Assert.Equal($"[{values[i]}]", await One($"{Scalars} SELECT json_arrayagg(v) FROM (VALUES (@{names[i]})) AS t (v);"));
            Assert.Equal($"{{\"k\":{values[i]}}}", await One($"{Scalars} SELECT json_objectagg('k': v) FROM (VALUES (@{names[i]})) AS t (v);"));
        }
    }
}

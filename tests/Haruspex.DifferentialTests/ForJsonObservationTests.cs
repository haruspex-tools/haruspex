using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server's FOR JSON actually writes, observed and kept as tests. Slice 1 took these from the
/// documentation; here the engine settles them, and the inferencer and the IR's README follow what these
/// say. If a SQL Server version changes any of them, this is where it shows.
/// </summary>
public class ForJsonObservationTests(SqlServerFixture server)
{
    /// <summary>Runs one query and returns each result set's rows, the JSON chunks joined.</summary>
    private async Task<IReadOnlyList<IReadOnlyList<string?>>> Query(string sql)
    {
        server.Require();
        await using var session = await server.ReadAsync(TestContext.Current.CancellationToken);
        await using var command = session.Command(sql);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var sets = new List<IReadOnlyList<string?>>();
        do
        {
            var rows = new List<string?>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                rows.Add(await reader.IsDBNullAsync(0, TestContext.Current.CancellationToken) ? null : reader.GetString(0));
            sets.Add(rows);
        }
        while (await reader.NextResultAsync(TestContext.Current.CancellationToken));
        return sets;
    }

    private async Task<string> Json(string sql) => string.Concat(Assert.Single(await Query(sql)));

    // ---- no rows ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("FOR JSON PATH")]
    [InlineData("FOR JSON PATH, WITHOUT_ARRAY_WRAPPER")]
    [InlineData("FOR JSON PATH, INCLUDE_NULL_VALUES")]
    public async Task A_top_level_for_json_query_with_no_rows_returns_a_result_set_with_no_row(string clause)
    {
        // Not "[]", not "{}", not a NULL: one result set, with no row in it. The IR's mayBeAbsent: no-rows.
        var set = Assert.Single(await Query($"SELECT 1 AS a WHERE 1 = 0 {clause};"));

        Assert.Empty(set);
    }

    [Fact]
    public async Task A_for_json_subquery_with_no_rows_is_null() =>
        Assert.Null(Assert.Single(Assert.Single(await Query("SELECT (SELECT 1 AS a WHERE 1 = 0 FOR JSON PATH) AS s;"))));

    [Theory]
    [InlineData("FOR JSON PATH", """[{"x":1}]""")]
    [InlineData("FOR JSON PATH, INCLUDE_NULL_VALUES", """[{"x":1,"s":null}]""")]
    public async Task An_empty_nested_subquery_is_left_out_or_with_include_null_values_null(string clause, string expected) =>
        Assert.Equal(expected, await Json($"SELECT 1 AS x, (SELECT 1 AS a WHERE 1 = 0 FOR JSON PATH) AS s {clause};"));

    // ---- all-null objects ---------------------------------------------------------------------------

    [Theory]
    [InlineData("FOR JSON PATH", """[{"x":1}]""")]
    [InlineData("FOR JSON PATH, INCLUDE_NULL_VALUES", """[{"x":1,"n":{"a":null,"b":null}}]""")]
    public async Task A_nested_object_whose_values_are_all_null_is_left_out_or_with_include_null_values_written_with_nulls(string clause, string expected) =>
        Assert.Equal(expected, await Json($"SELECT 1 AS x, CAST(NULL AS int) AS [n.a], CAST(NULL AS int) AS [n.b] {clause};"));

    [Theory]
    [InlineData("FOR JSON PATH", """[{"x":1}]""")]
    [InlineData("FOR JSON PATH, INCLUDE_NULL_VALUES", """[{"x":1,"n":{"m":{"a":null}}}]""")]
    public async Task So_is_one_nested_two_deep(string clause, string expected) =>
        Assert.Equal(expected, await Json($"SELECT 1 AS x, CAST(NULL AS int) AS [n.m.a] {clause};"));

    [Theory]
    [InlineData("FOR JSON PATH", """[{}]""")]
    [InlineData("FOR JSON PATH, WITHOUT_ARRAY_WRAPPER", """{}""")]
    public async Task A_row_whose_values_are_all_null_is_still_an_object(string clause, string expected) =>
        Assert.Equal(expected, await Json($"SELECT CAST(NULL AS int) AS a, CAST(NULL AS int) AS [n.b] {clause};"));

    // ---- long output --------------------------------------------------------------------------------

    [Fact]
    public async Task Long_output_is_split_over_several_rows_which_join_into_one_document()
    {
        var rows = Assert.Single(await Query("SELECT TOP (500) o.object_id AS id, o.name FROM sys.all_objects AS o FOR JSON PATH;"));

        Assert.True(rows.Count > 1, "Expected the JSON to be split over several rows.");
        using var json = System.Text.Json.JsonDocument.Parse(string.Concat(rows));
        Assert.Equal(500, json.RootElement.GetArrayLength());
    }

    // ---- scalars ------------------------------------------------------------------------------------

    /// <summary>
    /// The exact text FOR JSON writes for each type: (expression, JSON). A fraction of a second is written with
    /// exactly the type's scale in digits (three for datetime), trailing zeros kept, and left out entirely when it
    /// is zero. A zero offset is written Z, any other as +hh:mm. GUIDs are upper case. Numbers are JSON numbers:
    /// decimal and money keep their scale, float and real are written in exponent form. char(n) is padded.
    /// </summary>
    public static TheoryData<string, string> Scalars { get; } = new()
    {
        { "CAST('2026-03-04' AS date)", "\"2026-03-04\"" },
        { "CAST('0001-01-01' AS date)", "\"0001-01-01\"" },

        { "CAST('05:06:07.1234567' AS time(0))", "\"05:06:07\"" },
        { "CAST('05:06:07.1234567' AS time(3))", "\"05:06:07.123\"" },
        { "CAST('05:06:07.1' AS time(3))", "\"05:06:07.100\"" },
        { "CAST('05:06:07' AS time(3))", "\"05:06:07\"" },
        { "CAST('05:06:07.1234567' AS time(7))", "\"05:06:07.1234567\"" },
        { "CAST('05:06:07.0000001' AS time(7))", "\"05:06:07.0000001\"" },
        { "CAST('05:06:07' AS time(7))", "\"05:06:07\"" },
        { "CAST('05:06:07.1' AS time(1))", "\"05:06:07.1\"" },

        { "CAST('2026-03-04T05:06:07.1234567' AS datetime2(0))", "\"2026-03-04T05:06:07\"" },
        { "CAST('2026-03-04T05:06:07.1234567' AS datetime2(3))", "\"2026-03-04T05:06:07.123\"" },
        { "CAST('2026-03-04T05:06:07.1' AS datetime2(3))", "\"2026-03-04T05:06:07.100\"" },
        { "CAST('2026-03-04T05:06:07.1' AS datetime2(7))", "\"2026-03-04T05:06:07.1000000\"" },
        { "CAST('2026-03-04T05:06:07' AS datetime2(7))", "\"2026-03-04T05:06:07\"" },
        { "CAST('9999-12-31T23:59:59.9999999' AS datetime2(7))", "\"9999-12-31T23:59:59.9999999\"" },

        { "CAST('2026-03-04T05:06:07+05:30' AS datetimeoffset(0))", "\"2026-03-04T05:06:07+05:30\"" },
        { "CAST('2026-03-04T05:06:07.5-08:00' AS datetimeoffset(3))", "\"2026-03-04T05:06:07.500-08:00\"" },
        { "CAST('2026-03-04T05:06:07.1234567+00:00' AS datetimeoffset(7))", "\"2026-03-04T05:06:07.1234567Z\"" },
        { "CAST('2026-03-04T05:06:07-00:00' AS datetimeoffset(0))", "\"2026-03-04T05:06:07Z\"" },
        { "CAST('2026-03-04T05:06:07+00:00' AS datetimeoffset(7))", "\"2026-03-04T05:06:07Z\"" },
        { "CAST('0001-01-01T00:00:00-14:00' AS datetimeoffset(0))", "\"0001-01-01T00:00:00-14:00\"" },

        { "CAST('2026-03-04T05:06:29' AS smalldatetime)", "\"2026-03-04T05:06:00\"" },
        { "CAST('2026-03-04T05:06:59.999' AS smalldatetime)", "\"2026-03-04T05:07:00\"" },

        { "CAST('2026-03-04T05:06:07.123' AS datetime)", "\"2026-03-04T05:06:07.123\"" },
        { "CAST('2026-03-04T05:06:07.1' AS datetime)", "\"2026-03-04T05:06:07.100\"" },
        { "CAST('2026-03-04T05:06:07.997' AS datetime)", "\"2026-03-04T05:06:07.997\"" },
        { "CAST('2026-03-04T05:06:07' AS datetime)", "\"2026-03-04T05:06:07\"" },
        { "CAST('1753-01-01' AS datetime)", "\"1753-01-01T00:00:00\"" },

        { "CAST('6f9619ff-8b86-d011-b42d-00c04fc964ff' AS uniqueidentifier)", "\"6F9619FF-8B86-D011-B42D-00C04FC964FF\"" },

        { "CAST(1 AS bit)", "true" },
        { "CAST(0 AS bit)", "false" },

        { "CAST(12.5 AS decimal(19,2))", "12.50" },
        { "CAST(0 AS decimal(9,3))", "0.000" },
        { "CAST(-0.001 AS decimal(9,3))", "-0.001" },
        { "CAST(-0.0 AS decimal(3,1))", "0.0" },
        { "CAST(0.5 AS decimal(5,5))", "0.50000" },
        { "CAST(42 AS decimal(10,0))", "42" },
        { "CAST(99999999999999999999999999999999999999 AS decimal(38,0))", "99999999999999999999999999999999999999" },

        { "CAST(12.5 AS money)", "12.5000" },
        { "CAST(-922337203685477.5808 AS money)", "-922337203685477.5808" },
        { "CAST(1.25 AS smallmoney)", "1.2500" },

        { "CAST(9223372036854775807 AS bigint)", "9223372036854775807" },
        { "CAST(-9223372036854775808 AS bigint)", "-9223372036854775808" },
        { "CAST(9007199254740993 AS bigint)", "9007199254740993" },

        { "CAST(1 AS float)", "1.000000000000000e+000" },
        { "CAST(0.1 AS real)", "1.0000000e-001" },

        { "CAST('AB' AS char(5))", "\"AB   \"" },
        { "CAST(N'AB' AS nchar(5))", "\"AB   \"" },
        { "CAST('' AS char(3))", "\"   \"" },
        { "CAST('AB' AS varchar(5))", "\"AB\"" },
    };

    [Theory]
    [MemberData(nameof(Scalars))]
    public async Task For_json_writes_each_type_exactly_so(string expression, string expected) =>
        Assert.Equal($$"""{"v":{{expected}}}""", await Json($"SELECT {expression} AS v FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"));
}

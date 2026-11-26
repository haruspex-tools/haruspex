using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with JSON stored in a column, observed and kept as tests: what a column declared to hold JSON of a schema
/// rests on when it is read (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md). Each test makes
/// its table in a write session, rolled back, alone in the test database while it lasts. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public partial class DeclaredColumnObservationTests(SqlServerFixture server)
{
    /// <summary>
    /// Stores <paramref name="text"/> in a column of <paramref name="type"/>, then returns the first column of
    /// <paramref name="query"/>'s first row, which reads it as <c>s.v</c> from <c>dbo.obs_stored AS s</c>; rolled back.
    /// </summary>
    private async Task<string?> Stored(string? text, string query, string type = "nvarchar(max)")
    {
        await server.RequireAsync();
        await using var session = await server.WriteAsync(TestContext.Current.CancellationToken);
        await using (var create = session.Command($"CREATE TABLE dbo.obs_stored (v {type} NULL);"))
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await using (var insert = session.Command("INSERT INTO dbo.obs_stored (v) VALUES (@v);"))
        {
            insert.Parameters.AddWithValue("@v", (object?)text ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var select = session.Command(query);
        var value = await select.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull or null ? null : value.ToString();
    }

    [Theory]
    // JSON that is a scalar, null included, is refused, though its path is lax: json_query returns only an object or an array,
    // and errs on anything else rather than giving NULL.
    [InlineData("1")]
    [InlineData("\"a\"")]
    [InlineData("null")]
    [InlineData("true")]
    // So is text that isn't JSON, empty or blank text among it.
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    public async Task Json_query_of_stored_text_that_is_not_an_object_or_array_is_an_error(string text)
    {
        foreach (var path in new[] { "", ", 'lax $'", ", 'strict $'" })
        {
            var e = await Assert.ThrowsAsync<SqlException>(() => Stored(text, $"SELECT json_query(s.v{path}) FROM dbo.obs_stored AS s;"));
            Assert.Equal(13609, e.Number);
        }
    }

    [Theory]
    // An object or an array comes back as the same JSON value, nested as JSON in FOR JSON: its keys in their order, its spaces
    // inside, a key written twice and a number past 2^53 as they were written.
    [InlineData("""{ "b" : 1 ,"a":2}""", """{ "b" : 1 ,"a":2}""")]
    [InlineData("[1, 2]", "[1, 2]")]
    [InlineData("""{"a":1,"a":2}""", """{"a":1,"a":2}""")]
    [InlineData("""{"n":12345678901234567890}""", """{"n":12345678901234567890}""")]
    // Not always the same text: blank text before it is dropped, an escape is read, and text after the value is dropped too.
    [InlineData("""  {"a":1}""", """{"a":1}""")]
    [InlineData("""{"s":"é\/"}""", """{"s":"é\/"}""")]
    [InlineData("""{"a":1} x""", """{"a":1}""")]
    public async Task Json_query_of_stored_text_returns_an_object_or_array_as_the_same_json(string text, string expected)
    {
        Assert.Equal(expected, await Stored(text, "SELECT json_query(s.v) FROM dbo.obs_stored AS s;"));
        Assert.Equal($$"""{"x":{{expected}}}""", await Stored(text, "SELECT json_query(s.v) AS x FROM dbo.obs_stored AS s FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"));
    }

    [Fact]
    public async Task Json_query_of_a_null_column_is_null_and_for_json_leaves_its_key_out()
    {
        Assert.Null(await Stored(null, "SELECT json_query(s.v) FROM dbo.obs_stored AS s;"));
        Assert.Equal("{}", await Stored(null, "SELECT json_query(s.v) AS x FROM dbo.obs_stored AS s FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"));
    }

    [Theory]
    [InlineData("nvarchar(max)")]
    [InlineData("varchar(4000)")]
    public async Task A_string_column_holding_json_nests_as_an_escaped_string_in_for_json_without_json_query(string type) =>
        Assert.Equal("""{"x":"{\"a\":1}"}""", await Stored("""{"a":1}""", "SELECT s.v AS x FROM dbo.obs_stored AS s FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;", type));

    [Theory]
    // The json type itself holds only an object or an array, which is what it proves of its content alone; that is not read
    // (it is out of what a declaration covers), but it is why json_query over one never errs.
    [InlineData("1")]
    [InlineData("\"a\"")]
    [InlineData("null")]
    public async Task The_json_type_refuses_json_that_is_a_scalar(string text)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Stored(text, "SELECT 1;", "json"));
        Assert.Equal(13609, e.Number);
    }
}

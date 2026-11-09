using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What a call of a scalar function returning JSON gives, observed and kept as tests: what FunctionSummaries and the
/// analyzer's "scalar functions" rules rest on. Each test creates its table and functions in a transaction that is
/// rolled back. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class JsonFunctionObservationTests(SqlServerFixture server)
{
    private const string Table = """
        CREATE TABLE dbo.obs_node (id int NOT NULL PRIMARY KEY, parent_id int NULL, name nvarchar(10) NOT NULL);
        INSERT INTO dbo.obs_node VALUES (1, NULL, N'root'), (2, 1, N'a'), (3, 1, N'b'), (4, 2, N'c');
        """;

    private const string Children = """
        CREATE FUNCTION dbo.obs_children(@id int) RETURNS nvarchar(max) AS
        BEGIN
            RETURN (SELECT n.id, n.name FROM dbo.obs_node AS n WHERE n.parent_id = @id FOR JSON PATH);
        END
        """;

    private const string Card = "CREATE FUNCTION dbo.obs_card(@id int) RETURNS nvarchar(max) AS BEGIN RETURN json_object('id': @id); END";

    private const string JsonCard = "CREATE FUNCTION dbo.obs_json_card(@id int) RETURNS json AS BEGIN RETURN json_object('id': @id); END";

    private const string NullCard = "CREATE FUNCTION dbo.obs_null_card(@id int) RETURNS nvarchar(max) WITH RETURNS NULL ON NULL INPUT AS BEGIN RETURN json_object('id': @id); END";

    private const string Short = "CREATE FUNCTION dbo.obs_short() RETURNS nvarchar(10) AS BEGIN RETURN (SELECT n.id, n.name FROM dbo.obs_node AS n FOR JSON PATH); END";

    private const string Variable = """
        CREATE FUNCTION dbo.obs_variable(@id int) RETURNS nvarchar(max) AS
        BEGIN
            DECLARE @r nvarchar(max);
            SET @r = (SELECT n.id FROM dbo.obs_node AS n WHERE n.parent_id = @id FOR JSON PATH);
            RETURN @r;
        END
        """;

    private const string Tree = """
        CREATE FUNCTION dbo.obs_tree(@id int) RETURNS nvarchar(max) AS
        BEGIN
            RETURN (
                SELECT n.id, n.name, json_query(dbo.obs_tree(n.id)) AS children
                FROM dbo.obs_node AS n WHERE n.parent_id = @id
                FOR JSON PATH);
        END
        """;

    private const string Endless = "CREATE FUNCTION dbo.obs_endless(@n int) RETURNS nvarchar(max) AS BEGIN RETURN json_object('n': @n, 'next': json_query(dbo.obs_endless(@n + 1))); END";

    private const string Narrow = "CREATE FUNCTION dbo.obs_narrow(@s nvarchar(3), @d decimal(5,2)) RETURNS nvarchar(max) AS BEGIN RETURN json_object('s': @s, 'd': @d); END";

    private static readonly string[] Functions = [Children, Card, JsonCard, NullCard, Short, Variable, Tree, Endless, Narrow];

    /// <summary>Creates the table and the functions, then returns the first column of the first row of <paramref name="query"/>, as text; rolled back.</summary>
    private async Task<string?> Query(string query)
    {
        server.Require();
        await using var connection = new SqlConnection(server.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        try
        {
            // CREATE FUNCTION must be alone in its batch, so each is a command of its own.
            foreach (var batch in Functions.Prepend(Table))
            {
#pragma warning disable CA2100 // The tests' own literal SQL.
                await using var create = new SqlCommand(batch, connection, transaction);
                await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await using var select = new SqlCommand(query, connection, transaction);
#pragma warning restore CA2100
            var value = await select.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return value is DBNull or null ? null : value.ToString();
        }
        finally
        {
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    // A plain call is a string, escaped wherever it nests, though it holds JSON.
    [InlineData("SELECT dbo.obs_children(1) AS x FOR JSON PATH;", """[{"x":"[{\"id\":2,\"name\":\"a\"},{\"id\":3,\"name\":\"b\"}]"}]""")]
    [InlineData("SELECT json_object('x': dbo.obs_card(1));", """{"x":"{\"id\":1}"}""")]
    [InlineData("SELECT json_array(dbo.obs_card(1));", """["{\"id\":1}"]""")]
    // json_query over it makes it JSON again.
    [InlineData("SELECT json_query(dbo.obs_children(1)) AS x FOR JSON PATH;", """[{"x":[{"id":2,"name":"a"},{"id":3,"name":"b"}]}]""")]
    [InlineData("SELECT json_object('x': json_query(dbo.obs_card(1)));", """{"x":{"id":1}}""")]
    // A json return type nests as JSON with or without json_query.
    [InlineData("SELECT dbo.obs_json_card(1) AS x FOR JSON PATH;", """[{"x":{"id":1}}]""")]
    [InlineData("SELECT json_object('x': dbo.obs_json_card(1));", """{"x":{"id":1}}""")]
    [InlineData("SELECT json_query(dbo.obs_json_card(1)) AS x FOR JSON PATH;", """[{"x":{"id":1}}]""")]
    // A variable assigned and returned gives the same text.
    [InlineData("SELECT json_query(dbo.obs_variable(1)) AS x FOR JSON PATH;", """[{"x":[{"id":2},{"id":3}]}]""")]
    public async Task A_call_is_text_unless_json_query_or_a_json_return_type_makes_it_json(string query, string expected) =>
        Assert.Equal(expected, await Query(query));

    [Theory]
    // FOR JSON over no rows returns NULL from the function, not [] ...
    [InlineData("SELECT ISNULL(dbo.obs_children(4), N'NULL');", "NULL")]
    // ... so in FOR JSON PATH the key is left out, through json_query or not, unless INCLUDE_NULL_VALUES keeps it.
    [InlineData("SELECT 1 AS a, json_query(dbo.obs_children(4)) AS x FOR JSON PATH;", """[{"a":1}]""")]
    [InlineData("SELECT 1 AS a, dbo.obs_children(4) AS x FOR JSON PATH;", """[{"a":1}]""")]
    [InlineData("SELECT 1 AS a, json_query(dbo.obs_children(4)) AS x FOR JSON PATH, INCLUDE_NULL_VALUES;", """[{"a":1,"x":null}]""")]
    // A constructor keeps it as null under its default NULL ON NULL.
    [InlineData("SELECT json_object('x': json_query(dbo.obs_children(4)));", """{"x":null}""")]
    // RETURNS NULL ON NULL INPUT: a null argument gives NULL without running the body, which would return {"id":null}.
    [InlineData("SELECT ISNULL(dbo.obs_null_card(NULL), N'NULL');", "NULL")]
    [InlineData("SELECT dbo.obs_card(NULL);", """{"id":null}""")]
    public async Task A_function_returns_null_when_for_json_finds_no_rows_or_an_argument_is_null(string query, string expected) =>
        Assert.Equal(expected, await Query(query));

    [Fact]
    public async Task A_return_type_too_short_cuts_the_json_short_without_an_error() =>
        // Which is why only nvarchar(max) and json returns are read as JSON.
        Assert.Equal("[{\"id\":1,\"", await Query("SELECT dbo.obs_short();"));

    [Fact]
    public async Task An_argument_is_converted_to_the_parameters_type_so_the_types_facts_hold_inside() =>
        // A string is cut short and a decimal rounded, without an error.
        Assert.Equal("""{"s":"abc","d":1.24}""", await Query("SELECT dbo.obs_narrow(N'abcdef', 1.239);"));

    [Fact]
    public async Task A_recursive_function_builds_a_tree_whose_leaves_have_no_children_key() =>
        Assert.Equal(
            """{"tree":[{"id":2,"name":"a","children":[{"id":4,"name":"c"}]},{"id":3,"name":"b"}]}""",
            await Query("SELECT json_query(dbo.obs_tree(1)) AS tree FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"));

    [Fact]
    public async Task Recursion_stops_at_32_nested_calls_with_an_error()
    {
        // An error, not a value: the IR's type says nothing about depth, and needs not to.
        var e = await Assert.ThrowsAsync<SqlException>(() => Query("SELECT dbo.obs_endless(1);"));
        Assert.Equal(217, e.Number);
        Assert.Contains("limit 32", e.Message, StringComparison.Ordinal);
    }
}

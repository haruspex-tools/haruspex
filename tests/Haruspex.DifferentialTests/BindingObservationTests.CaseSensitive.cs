using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

// Names that differ only in case, on a database of Latin1_General_100_CS_AS on this server, whose collation is case-insensitive:
// the pairs reading refuses (HAR1022, decision 0119), and which collation decides each. A table's columns and a schema's objects
// follow the database's, so there such a pair is two names; a body's variables, temporary tables and table variables follow the
// server's, so there it is refused as on any database of this server.
public partial class BindingObservationTests
{
    private const string CaseSensitive = "Latin1_General_100_CS_AS";

    /// <summary>The rows <paramref name="query"/> returns, run after <paramref name="setup"/>'s batches, in a new database of <see cref="CaseSensitive"/>.</summary>
    private async Task<List<string>> OnCaseSensitive(string query, params string[] setup)
    {
        var token = TestContext.Current.CancellationToken;
        await using var scratch = await server.ScratchAsync(CaseSensitive);
        foreach (var batch in setup)
            await scratch.ExecuteAsync(batch, token);
        await using var connection = await scratch.OpenAsync(token);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var command = new SqlCommand(query, connection);
#pragma warning restore CA2100
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<string>();
        while (await reader.ReadAsync(token))
            rows.Add(string.Join(",", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return rows;
    }

    /// <summary>The error <paramref name="batch"/> raises after <paramref name="setup"/>'s, in a new database of <see cref="CaseSensitive"/>, or 0 for none.</summary>
    private async Task<int> ErrorOnCaseSensitive(string batch, params string[] setup)
    {
        try
        {
            await OnCaseSensitive(batch, setup);
            return 0;
        }
        catch (SqlException error)
        {
            return error.Number;
        }
    }

    [Fact]
    public async Task On_a_case_sensitive_database_two_columns_that_differ_only_in_case_are_two()
    {
        // The table of decision 0119: code is its own int that may be null, and Qty its own int that is never null, with no CHECK. Matched
        // without case they would be Code's varchar(10) and qty's CHECK and nullability, so reading refuses the pair.
        var described = await OnCaseSensitive(
            "SELECT name, system_type_name, is_nullable FROM sys.dm_exec_describe_first_result_set(N'EXEC dbo.read_columns', NULL, 0);",
            "CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, Code varchar(10) NOT NULL, code int NULL, qty int NULL CHECK (qty BETWEEN 1 AND 5), Qty int NOT NULL);",
            "CREATE PROCEDURE dbo.read_columns AS SELECT code, Qty FROM dbo.item;");

        Assert.Equal(["code,int,True", "Qty,int,False"], described);
    }

    [Fact]
    public async Task On_a_case_sensitive_database_a_table_and_a_view_may_differ_only_in_case() =>
        Assert.Equal(
            ["Thing,U ", "THING,V "],
            await OnCaseSensitive(
                "SELECT name, type FROM sys.objects WHERE name IN (N'Thing', N'THING') ORDER BY type;",
                "CREATE TABLE dbo.Thing (a int);",
                "CREATE VIEW dbo.THING AS SELECT 'v' AS a;"));

    [Fact]
    public async Task On_a_case_sensitive_database_a_table_type_s_columns_may_differ_only_in_case_but_no_variable_of_it_can_be_declared()
    {
        // The type follows the database's collation; a variable of it is a table in tempdb, which follows the server's.
        Assert.Equal(["a", "A"], await OnCaseSensitive(
            "SELECT c.name FROM sys.columns AS c JOIN sys.table_types AS t ON t.type_table_object_id = c.object_id WHERE t.name = N'lines' ORDER BY c.column_id;",
            "CREATE TYPE dbo.lines AS TABLE (a int NOT NULL, A varchar(5) NULL);"));
        Assert.Equal(2705, await ErrorOnCaseSensitive("DECLARE @x dbo.lines;", "CREATE TYPE dbo.lines AS TABLE (a int NOT NULL, A varchar(5) NULL);"));
    }

    [Theory]
    [InlineData("DECLARE @Total int; DECLARE @total varchar(10);", 134)]
    [InlineData("DECLARE @rows TABLE (a int); DECLARE @Rows int;", 134)]
    [InlineData("CREATE TABLE #t (a int); CREATE TABLE #T (b int);", 2714)]
    [InlineData("CREATE TABLE #t (a int NOT NULL, A varchar(5) NULL);", 2705)]
    [InlineData("DECLARE @t TABLE (a int NOT NULL, A varchar(5) NULL);", 2705)]
    [InlineData("SELECT 1 AS a, 2 AS A INTO #s;", 2705)]
    public async Task On_a_case_sensitive_database_a_body_s_names_follow_the_server_s_collation(string batch, int error) =>
        Assert.Equal(error, await ErrorOnCaseSensitive(batch));
}

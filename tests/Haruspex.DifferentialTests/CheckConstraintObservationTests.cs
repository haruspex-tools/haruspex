using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server enforces for types, alias types and CHECK constraints, observed and kept as tests. Semantics'
/// reading of the schema (SchemaRules, CheckReader) and the Zod emitter's mappings follow what these say; several
/// are why a fact is weaker than it first looks. Each runs in a transaction that is rolled back, against the test
/// database (whose collation is the server's default, SQL_Latin1_General_CP1_CI_AS on the pinned image).
/// </summary>
public class CheckConstraintObservationTests(SqlServerFixture server)
{
    /// <summary>Runs <paramref name="setup"/>, then returns the first column of the first row of <paramref name="query"/>; rolled back.</summary>
    private async Task<object?> Query(string setup, string query)
    {
        server.Require();
        await using var connection = new SqlConnection(server.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        try
        {
#pragma warning disable CA2100 // The tests' own literal SQL.
            await using (var command = new SqlCommand(setup, connection, transaction))
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using var select = new SqlCommand(query, connection, transaction);
#pragma warning restore CA2100
            var value = await select.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return value is DBNull ? null : value;
        }
        finally
        {
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Whether <paramref name="statement"/> succeeds after <paramref name="setup"/>; rolled back.</summary>
    private async Task<bool> Succeeds(string setup, string statement)
    {
        try
        {
            await Query(setup, statement + "; SELECT 1;");
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    // ---- strings ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_string_check_admits_another_case_and_trailing_spaces()
    {
        // So a CHECK's strings are not an exact set: the IR says "collation", and the Zod is not an enum.
        var json = await Query(
            "CREATE TABLE dbo.obs (s varchar(10) NULL CHECK (s IN ('open', 'closed'))); INSERT INTO dbo.obs VALUES ('OPEN'), ('open   '), ('Closed');",
            "SELECT s FROM dbo.obs FOR JSON PATH;");

        Assert.Equal("""[{"s":"OPEN"},{"s":"open   "},{"s":"Closed"}]""", json);
    }

    [Fact]
    public async Task A_binary_collation_still_ignores_trailing_spaces()
    {
        const string table = "CREATE TABLE dbo.obs (s varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (s IN ('open')));";

        Assert.True(await Succeeds(table, "INSERT INTO dbo.obs VALUES ('open  ')"));
        Assert.False(await Succeeds(table, "INSERT INTO dbo.obs VALUES ('OPEN')"));
    }

    [Fact]
    public async Task Len_and_not_empty_ignore_trailing_spaces_so_they_refuse_a_value_of_spaces()
    {
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (s nvarchar(10) NULL CHECK (len(s) > 0));", "INSERT INTO dbo.obs VALUES (N'   ')"));
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (s nvarchar(10) NULL CHECK (s <> ''));", "INSERT INTO dbo.obs VALUES (N'   ')"));
        Assert.True(await Succeeds("CREATE TABLE dbo.obs (s nvarchar(10) NULL CHECK (s <> ''));", "INSERT INTO dbo.obs VALUES (N' x')"));
    }

    [Fact]
    public async Task Char_in_a_utf8_collation_pads_to_n_bytes_not_n_characters()
    {
        // So char(n)'s .length(n) holds only in a single-byte collation.
        var json = await Query(
            "CREATE TABLE dbo.obs (c char(3) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL); INSERT INTO dbo.obs VALUES (N'é');",
            "SELECT c FROM dbo.obs FOR JSON PATH;");

        Assert.Equal("""[{"c":"é "}]""", json);
    }

    [Fact]
    public async Task Varchar_in_a_utf8_collation_counts_bytes()
    {
        const string table = "CREATE TABLE dbo.obs (c varchar(3) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL);";

        Assert.True(await Succeeds(table, "INSERT INTO dbo.obs VALUES (N'éa')"));
        Assert.False(await Succeeds(table, "INSERT INTO dbo.obs VALUES (N'éé')"));
    }

    // ---- numbers ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_money_column_compares_with_a_literal_of_more_decimal_places_exactly()
    {
        // The literal is not rounded to money first: >= 1.00004 refuses 1.0000, and <= 1.00006 refuses 1.0001.
        // CheckReader still reads only literals that fit the column (see its remarks); this pins what the engine
        // does today, so that the caution there is known to be caution.
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (m money NULL CHECK (m >= 1.00004));", "INSERT INTO dbo.obs VALUES (1.0000)"));
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (m money NULL CHECK (m <= 1.00006));", "INSERT INTO dbo.obs VALUES (1.0001)"));
    }

    // ---- null ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_check_passes_null()
    {
        Assert.True(await Succeeds("CREATE TABLE dbo.obs (i int NULL CHECK (i IN (1, 2)), s varchar(5) NULL CHECK (len(s) > 0));", "INSERT INTO dbo.obs VALUES (NULL, NULL)"));
    }

    [Fact]
    public async Task A_null_in_an_in_list_makes_the_check_pass_anything()
    {
        Assert.True(await Succeeds("CREATE TABLE dbo.obs (i int NULL CHECK (i IN (1, NULL)));", "INSERT INTO dbo.obs VALUES (5)"));
    }

    // ---- alias types --------------------------------------------------------------------------------

    [Theory]
    [InlineData("ON", "c chk.short_code", false)]
    [InlineData("OFF", "c chk.short_code", false)]
    [InlineData("ON", "c chk.short_code NULL", true)]
    [InlineData("ON", "c sysname", false)]
    [InlineData("ON", "c sysname NULL", true)]
    [InlineData("ON", "c chk.pct", true)]
    public async Task A_column_that_declares_no_nullability_takes_its_alias_types_whatever_the_session_default(string ansiNullDefault, string column, bool nullable)
    {
        var isNullable = await Query(
            $"SET ANSI_NULL_DFLT_ON {ansiNullDefault}; CREATE TABLE dbo.obs ({column});",
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.obs');");

        Assert.Equal(nullable, isNullable);
    }
}

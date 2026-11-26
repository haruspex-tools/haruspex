using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A SQL Server with full-text search, for the observations only such a server can make (<c>FullTextObservationTests</c>): the
/// one <see cref="SqlServerFixture"/> starts has none, as its image doesn't. It is found in
/// <c>HARUSPEX_TEST_SQLSERVER_FULLTEXT</c>, a connection string to a server of the developer's own, such as one run from the image
/// <c>Server/FullText/Dockerfile</c> builds (its README says how). Without one, or when it has no full-text search, each test that
/// needs it is skipped, saying why, unless <c>HARUSPEX_REQUIRE_FULLTEXT=1</c> turns the skip into a failure. Each test has a
/// database of its own (<see cref="ScratchAsync"/>), dropped when it is done.
/// </summary>
internal static class FullTextServer
{
    public const string ServerVariable = "HARUSPEX_TEST_SQLSERVER_FULLTEXT";
    public const string RequireVariable = "HARUSPEX_REQUIRE_FULLTEXT";

    /// <summary>The prefix of every database the tests create, as <see cref="SqlServerFixture"/>'s are named.</summary>
    private const string DatabasePrefix = "haruspex_test_";

    // Why there is no server to use, checked once; null when there is one.
    private static readonly Lazy<Task<string?>> Unavailable = new(CheckAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string? Server => Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } server ? server : null;

    /// <summary>Skips the calling test if there is no server with full-text search, or fails it if one is required.</summary>
    public static async Task RequireAsync()
    {
        if (await Unavailable.Value.ConfigureAwait(false) is not { } why)
            return;
        if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
            Assert.Fail($"{RequireVariable}=1, but there is no SQL Server with full-text search: {why}");
        Assert.Skip($"No SQL Server with full-text search, so this observation did not run: {why} Build and run tests/Haruspex.DifferentialTests/Server/FullText/Dockerfile and set {ServerVariable}; its README says how.");
    }

    /// <summary>A database of the test's own on the server, dropped when it is disposed.</summary>
    public static async Task<ScratchDatabase> ScratchAsync()
    {
        await RequireAsync().ConfigureAwait(false);
        var name = DatabasePrefix + Guid.NewGuid().ToString("N");
        await ExecuteOnMasterAsync($"CREATE DATABASE [{name}];").ConfigureAwait(false);
        var connection = new SqlConnectionStringBuilder(Server) { InitialCatalog = name, Pooling = false, ApplicationName = "haruspex-tests-fulltext" };
        return new ScratchDatabase(connection.ConnectionString, () => DropAsync(name));
    }

    private static async Task<string?> CheckAsync()
    {
        if (Server is null)
            return $"{ServerVariable} is not set.";
        try
        {
            await using var connection = new SqlConnection(Server);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new SqlCommand("SELECT CAST(SERVERPROPERTY('IsFullTextInstalled') AS int);", connection);
            return await command.ExecuteScalarAsync().ConfigureAwait(false) is 1
                ? null
                : $"the server {ServerVariable} names has no full-text search installed.";
        }
        catch (SqlException e)
        {
            return $"the server {ServerVariable} names can't be reached: {e.Message}";
        }
    }

    private static Task DropAsync(string name) =>
        ExecuteOnMasterAsync($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END;");

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Server) { InitialCatalog = "master", Pooling = false }.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
#pragma warning disable CA2100 // A database name this class made, from a GUID.
        await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A SQL Server that may call a REST endpoint, and the HTTPS endpoint it calls, for what only such a pair can show
/// (<c>RestEndpointObservationTests</c>): the server <see cref="SqlServerFixture"/> starts has <c>external rest endpoint enabled</c>
/// off, as SQL Server 2025 has by default, and nothing to call. It is found in <c>HARUSPEX_TEST_SQLSERVER_REST</c>, a connection
/// string to a server of the developer's own, such as the one <c>Server/Rest/start.sh</c> starts beside its endpoint (its README
/// says how). Without one, or when it can't reach the endpoint, each test that needs it is skipped, saying why, unless
/// <c>HARUSPEX_REQUIRE_REST=1</c> turns the skip into a failure. A test creates nothing, so it needs no database of its own.
/// </summary>
internal static class RestServer
{
    public const string ServerVariable = "HARUSPEX_TEST_SQLSERVER_REST";
    public const string RequireVariable = "HARUSPEX_REQUIRE_REST";

    /// <summary>The endpoint the tests call, as the server reaches it: a name under <c>.test</c>, which is never a real host.</summary>
    public const string Endpoint = "https://rest.haruspex.test";

    /// <summary>The same endpoint's port of replies written byte by byte (<c>Server/Rest/endpoint.mjs</c>).</summary>
    public const string RawEndpoint = "https://rest.haruspex.test:8443";

    // Why there is no server to use, checked once; null when there is one.
    private static readonly Lazy<Task<string?>> Unavailable = new(CheckAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string? Server => Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } server ? server : null;

    /// <summary>Skips the calling test if there is no server that can call the endpoint, or fails it if one is required.</summary>
    public static async Task RequireAsync()
    {
        if (await Unavailable.Value.ConfigureAwait(false) is not { } why)
            return;
        if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
            Assert.Fail($"{RequireVariable}=1, but there is no SQL Server that can call the test endpoint: {why}");
        Assert.Skip($"No SQL Server that can call a REST endpoint, so this observation did not run: {why} Run tests/Haruspex.DifferentialTests/Server/Rest/start.sh and set {ServerVariable}; its README says how.");
    }

    /// <summary>Runs <paramref name="batch"/>, reading every result set as a client does (<see cref="ObservedCall.Read"/>).</summary>
    public static async Task<CallOutcome> RunAsync(string batch, bool errorsAsMessages = false)
    {
        await RequireAsync().ConfigureAwait(false);
        var cancel = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Server) { Pooling = false, ApplicationName = "haruspex-tests-rest" }.ConnectionString);
        await connection.OpenAsync(cancel).ConfigureAwait(false);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var command = new SqlCommand(batch, connection);
#pragma warning restore CA2100
        return await ObservedCall.Read(command, errorsAsMessages, cancel).ConfigureAwait(false);
    }

    private static async Task<string?> CheckAsync()
    {
        if (Server is null)
            return $"{ServerVariable} is not set.";
        try
        {
            await using var connection = new SqlConnection(Server);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new SqlCommand(
                $"DECLARE @r nvarchar(max), @rc int; EXEC @rc = sys.sp_invoke_external_rest_endpoint @url = N'{Endpoint}/', @method = N'GET', @response = @r OUTPUT; SELECT @rc;",
                connection);
            return await command.ExecuteScalarAsync().ConfigureAwait(false) is 0
                ? null
                : $"the server {ServerVariable} names called {Endpoint} and received a status other than 2xx.";
        }
        catch (SqlException e)
        {
            return $"the server {ServerVariable} names can't call {Endpoint} (error {e.Number}): {e.Message}";
        }
    }
}

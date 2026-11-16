using System.Text.Json;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// Calls the test database through the mssql driver, in Node, as an application does: the script
/// <c>tests/emit-check/src/driver.mjs</c>, with the driver version the package pins. The rest of the differential tests read
/// results with SqlClient and convert them as the driver would; this is how they see what the driver itself hands over.
/// </summary>
internal static class MssqlDriver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Runs the setup, then each call, in one transaction that is rolled back, holding the database alone meanwhile, and
    /// returns what the driver handed over for each call.
    /// </summary>
    /// <param name="module">A generated module, written beside the script, whose schemas the calls' checks name; or null.</param>
    public static async Task<IReadOnlyList<Response>> RunAsync(
        SqlServerFixture server, string? module, IReadOnlyList<string> setup, IReadOnlyList<Call> calls, CancellationToken token)
    {
        server.Require();
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the mssql driver was not run");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            if (module is not null)
                await File.WriteAllTextAsync(Path.Combine(folder, "module.zod.ts"), module, token).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(folder, "driver.json"),
                JsonSerializer.Serialize(new { module = module is null ? null : "module.zod.ts", setup, calls }, Json),
                token).ConfigureAwait(false);

            using (var session = await server.DriverAsync(token).ConfigureAwait(false))
            {
                Assert.True(
                    EmitCheckPackage.TryRun(
                        "node",
                        [Path.Combine("src", "driver.mjs"), folder],
                        RepositoryPaths.EmitCheck,
                        new Dictionary<string, string> { ["HARUSPEX_DRIVER_CONFIG"] = session.Config },
                        out var output,
                        out var why),
                    $"The mssql driver script failed ({why}):\n{output}");
            }

            var observed = await File.ReadAllTextAsync(Path.Combine(folder, "observed.json"), token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Observed>(observed, Json)!.Calls;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>One call: a batch, or a procedure with its arguments, with the driver's parseJSON on or off.</summary>
    /// <param name="Check">Whether to read its result sets as an application would and parse them with a schema of the module.</param>
    public sealed record Call(
        string Name,
        bool ParseJson,
        string? Batch = null,
        string? Procedure = null,
        IReadOnlyDictionary<string, object?>? Parameters = null,
        Check? Check = null);

    /// <summary>The schema a call's results are parsed with, and each result set's encoding, <c>json</c> or <c>rowset</c>.</summary>
    public sealed record Check(string Schema, IReadOnlyList<string> Results);

#pragma warning disable CA1812 // Made by the JSON reader, from what the script wrote, never by code.

    /// <summary>What the driver handed over for a call: its result sets, or the error it threw, and the check's outcome.</summary>
    public sealed record Response(string Name, IReadOnlyList<Recordset> Recordsets, Error? Error, CheckOutcome? Check);

    /// <summary>One result set as the driver hands it over: its columns' names and its rows (an object, or what the driver parsed).</summary>
    public sealed record Recordset(IReadOnlyList<string> Columns, IReadOnlyList<JsonElement> Rows);

    /// <summary>An error the driver threw, with its code (<c>EJSON</c> for JSON it could not parse).</summary>
    public sealed record Error(string? Code, string Message);

    /// <summary>Whether the schema accepted what the application read, and its first issues when not.</summary>
    public sealed record CheckOutcome(bool Success, IReadOnlyList<string> Issues);

    private sealed record Observed(IReadOnlyList<Response> Calls);

#pragma warning restore CA1812
}

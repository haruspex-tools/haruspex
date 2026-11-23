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
    /// <param name="committed">
    /// When given, the calls run in this database of the test's own, with what it already holds committed, as a table type a
    /// table-valued parameter is sent as must be (see <see cref="GeneratedFunctions.RunAsync"/>).
    /// </param>
    public static async Task<IReadOnlyList<Response>> RunAsync(
        SqlServerFixture server, string? module, IReadOnlyList<string> setup, IReadOnlyList<Call> calls, CancellationToken token, ScratchDatabase? committed = null)
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

            if (committed is not null)
            {
                Run(committed.DriverConfig);
            }
            else
            {
                using var session = await server.DriverAsync(token).ConfigureAwait(false);
                Run(session.Config);
            }

            void Run(string config) => Assert.True(
                EmitCheckPackage.TryRun(
                    "node",
                    [Path.Combine("src", "driver.mjs"), folder],
                    RepositoryPaths.EmitCheck,
                    new Dictionary<string, string> { ["HARUSPEX_DRIVER_CONFIG"] = config },
                    out var output,
                    out var why),
                $"The mssql driver script failed ({why}):\n{output}");

            var observed = await File.ReadAllTextAsync(Path.Combine(folder, "observed.json"), token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Observed>(observed, Json)!.Calls;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>One call: a batch, or a procedure with its arguments, with the driver's parseJSON on or off.</summary>
    /// <param name="Parameters">The arguments: each a value the driver chooses a type for, or a <see cref="Typed"/> one.</param>
    /// <param name="Check">Whether to read its result sets as an application would and parse them with a schema of the module.</param>
    /// <param name="TimeZone">The time zone the driver runs in for this call (its process's <c>TZ</c>), or null for the machine's.</param>
    /// <param name="Accepts">A schema of the module to parse the parameters' values with, as an object by name, or null.</param>
    public sealed record Call(
        string Name,
        bool ParseJson,
        string? Batch = null,
        string? Procedure = null,
        IReadOnlyDictionary<string, object?>? Parameters = null,
        Check? Check = null,
        string? TimeZone = null,
        string? Accepts = null);

    /// <summary>
    /// An argument sent with one of the driver's types, <c>sql[Type](...Args)</c>, as a generated function sends it: an argument
    /// <c>"MAX"</c> is <c>sql.MAX</c>. A value JSON can't hold is a <see cref="JsValue"/>.
    /// </summary>
    public sealed record Typed(string Type, object? Value, IReadOnlyList<object>? Args = null);

    /// <summary>
    /// An <c>OUTPUT</c> parameter, bound with <c>request.output</c> as the driver's type <c>sql[Type](...Args)</c> and no value; what
    /// it holds when the procedure ends is in <see cref="Response.Output"/>. The script reads it as <c>{ "output": … }</c>.
    /// </summary>
    public sealed record OutputValue(OutputType Output);

    /// <summary>The driver's type an <see cref="OutputValue"/> is bound as.</summary>
    public sealed record OutputType(string Type, IReadOnlyList<object>? Args = null);

    /// <summary>A table-valued parameter: the table type by its schema and name, its columns in order, and its rows' values.</summary>
    public sealed record Table(string Schema, string Name, IReadOnlyList<TableColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

    /// <summary>A column of a <see cref="Table"/>: its name and the driver's type it is declared as, <c>sql[Type](...Args)</c>.</summary>
    public sealed record TableColumn(string Name, string Type, IReadOnlyList<object>? Args = null);

    /// <summary>How a <see cref="Table"/> is written for the script: <c>{ "table": … }</c>.</summary>
    public sealed record TableValue(Table Table);

    /// <summary>A JavaScript value JSON can't hold, by name (<c>NaN</c> or <c>Infinity</c>), which the script reads as <c>{ "js": … }</c>.</summary>
    public sealed record JsValue(string Js);

    /// <summary>A JavaScript <c>bigint</c>, from its decimal digits, which the script reads as <c>{ "bigInt": … }</c>.</summary>
    public sealed record BigIntValue(string BigInt);

    /// <summary>A Node <c>Buffer</c>, from its bytes in base64, which the scripts read as <c>{ "bytes": … }</c>.</summary>
    public sealed record BytesValue(string Bytes);

    /// <summary>A plain <c>Uint8Array</c>, not a <c>Buffer</c>, from its bytes in base64, which the driver's script reads as <c>{ "uint8Array": … }</c>.</summary>
    public sealed record Uint8ArrayValue(string Uint8Array);

    /// <summary>
    /// The schema a call's results are parsed with, each result set's encoding, <c>json</c> or <c>rowset</c>, and how the
    /// application reads a JSON result set: the body of a JavaScript function of <c>result</c>, an object whose
    /// <c>recordset</c> is that result set, or undefined when it did not arrive.
    /// </summary>
    public sealed record Check(string Schema, IReadOnlyList<string> Results, string ReadJson);

#pragma warning disable CA1812 // Made by the JSON reader, from what the script wrote, never by code.

    /// <summary>
    /// What the driver handed over for a call: its result sets, or the error it threw, the check's outcome, and whether the
    /// schema the call names accepted its parameters.
    /// </summary>
    public sealed record Response(string Name, IReadOnlyList<Recordset> Recordsets, Error? Error, CheckOutcome? Check, bool? Accepted = null)
    {
        /// <summary>The call's return value as the driver reports it (<c>returnValue</c>); null for an error, or a batch.</summary>
        public int? ReturnValue { get; init; }

        /// <summary>The <c>OUTPUT</c> parameters' values by name, as the driver hands them over (<c>output</c>); null for an error.</summary>
        public JsonElement? Output { get; init; }
    }

    /// <summary>One result set as the driver hands it over: its columns' names and its rows (an object, or what the driver parsed).</summary>
    public sealed record Recordset(IReadOnlyList<string> Columns, IReadOnlyList<JsonElement> Rows);

    /// <summary>
    /// An error the driver threw, with its code (<c>EJSON</c> for JSON it could not parse, <c>EPARAM</c> for a value its own
    /// check of a type refused, <c>EREQUEST</c> for the server's error) and, for the server's, its number.
    /// </summary>
    public sealed record Error(string? Code, int? Number, string Message, IReadOnlyList<int> Preceding);

    /// <summary>Whether the schema accepted what the application read, and its first issues when not.</summary>
    public sealed record CheckOutcome(bool Success, IReadOnlyList<string> Issues);

    private sealed record Observed(IReadOnlyList<Response> Calls);

#pragma warning restore CA1812
}

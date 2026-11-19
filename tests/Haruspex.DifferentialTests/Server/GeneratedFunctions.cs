using System.Text.Json;
using Haruspex.Emit.Mssql;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// Calls the test database through the functions haruspex generates for a document, in Node, as an application does: the
/// script <c>tests/emit-check/src/functions.mjs</c>, with the Zod module and the functions written beside it as the CLI writes
/// them, and the driver version the package pins.
/// </summary>
internal static class GeneratedFunctions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Writes <paramref name="document"/>'s database module, runs the setup, then each call, in one transaction that is rolled
    /// back, holding the database alone meanwhile, and returns what each call gave or threw. With <paramref name="strict"/>, the
    /// module is written as <c>"objects": "strict"</c> writes it.
    /// </summary>
    /// <param name="committed">
    /// When given, the calls run in this database of the test's own rather than the shared one, with what it already holds
    /// committed: a table type created in the transaction the calls run in deadlocks the call that sends it (observed, 1205), so
    /// a test of table-valued parameters creates its types here first. The setup still runs in the transaction, rolled back.
    /// </param>
    /// <param name="modules">
    /// When given, the module is written split into these modules, each a Zod file and a functions file named after it with the
    /// shared Zod file beside them, as a config file's <c>split</c> writes them; every module's functions are called.
    /// </param>
    public static async Task<IReadOnlyList<Response>> RunAsync(
        SqlServerFixture server, IrDocument document, IReadOnlyList<string> setup, IReadOnlyList<Call> calls, CancellationToken token, bool strict = false, IrModules? modules = null, ScratchDatabase? committed = null)
    {
        server.Require();
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the generated functions were not run");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var zod = new ZodOptions { Objects = strict ? ZodObjects.Strict : ZodObjects.Strip };
            var mssql = new MssqlOptions { RefusesExtraResultSets = strict };
            string[] functions;
            if (modules is null)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, "haruspex.zod.ts"), ZodEmitter.Emit(document, zod).Expected(), token).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(folder, "haruspex.mssql.ts"), MssqlEmitter.Emit(document, mssql).Expected(), token).ConfigureAwait(false);
                functions = ["haruspex.mssql.ts"];
            }
            else
            {
                foreach (var (module, text) in ZodEmitter.Emit(document, zod, modules).Expected())
                    await File.WriteAllTextAsync(Path.Combine(folder, $"{module}.zod.ts"), text, token).ConfigureAwait(false);
                foreach (var (module, text) in MssqlEmitter.Emit(document, mssql, modules, m => $"./{m}.zod.js").Expected())
                    await File.WriteAllTextAsync(Path.Combine(folder, $"{module}.mssql.ts"), text, token).ConfigureAwait(false);
                functions = [.. modules.Names.Select(m => $"{m}.mssql.ts")];
            }

            await File.WriteAllTextAsync(Path.Combine(folder, "functions.json"), JsonSerializer.Serialize(new { setup, calls, functions }, Json), token).ConfigureAwait(false);

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
                    [Path.Combine("src", "functions.mjs"), folder],
                    RepositoryPaths.EmitCheck,
                    new Dictionary<string, string> { ["HARUSPEX_DRIVER_CONFIG"] = config },
                    out var output,
                    out var why),
                $"The generated functions' script failed ({why}):\n{output}");

            var observed = await File.ReadAllTextAsync(Path.Combine(folder, "observed.json"), token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Observed>(observed, Json)!.Calls;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>One call of a generated function.</summary>
    /// <param name="Name">What the call is, for messages.</param>
    /// <param name="Function">The function's name, such as <c>getOrder</c>.</param>
    /// <param name="Params">Its parameters' object, or null for a function that takes none; a <see cref="MssqlDriver.BigIntValue"/> is a JavaScript bigint.</param>
    /// <param name="ParseJson">The pool's parseJSON setting for the call.</param>
    /// <param name="Db"><c>transaction</c>, or <c>untouchable</c> for a connection whose <c>request()</c> throws.</param>
    public sealed record Call(string Name, string Function, IReadOnlyDictionary<string, object?>? Params = null, bool ParseJson = false, string Db = "transaction");

#pragma warning disable CA1812 // Made by the JSON reader, from what the script wrote, never by code.

    /// <summary>What a call returned, as JSON holds it, or what it threw.</summary>
    public sealed record Response(string Name, JsonElement? Value, Failure? Error);

    /// <summary>
    /// An error a function threw: its name and kind, the procedure and part a haruspex error names with its issues, and the
    /// number and procedure the driver's own error carries, and the numbers of the server's errors it carries before it.
    /// </summary>
    public sealed record Failure(
        string Name, string? Kind, string Message, string? Procedure, string? Part, IReadOnlyList<Issue> Issues, int? Number, string? ProcName, bool IsRequestError, IReadOnlyList<int> Preceding);

    /// <summary>One issue a schema found: its message, and where, as keys.</summary>
    public sealed record Issue(string Message, IReadOnlyList<string> Path);

    private sealed record Observed(IReadOnlyList<Response> Calls);

#pragma warning restore CA1812
}

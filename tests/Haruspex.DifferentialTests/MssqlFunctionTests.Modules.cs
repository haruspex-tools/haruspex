using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The functions of a module split by schema, on the server: each module's functions import its own Zod file, whose shapes
/// shared with another module come from the shared file (docs/decisions/0074-a-config-may-split-its-output-into-modules.md).
/// </summary>
public partial class MssqlFunctionTests
{
    private const string Outpost = "IF SCHEMA_ID(N'outpost') IS NULL EXEC (N'CREATE SCHEMA outpost');";

    // One shape in two schemas, so in two modules: declared once, in the shared file, and imported by both.
    private const string OrderTotal = "CREATE PROCEDURE outlet.order_total @n int AS SELECT CAST(@n AS int) AS n, CAST(@n * 2 AS int) AS twice;";
    private const string ParcelTotal = "CREATE PROCEDURE outpost.parcel_total @n int AS SELECT CAST(@n AS int) AS n, CAST(@n * 2 AS int) AS twice;";

    [Fact]
    public async Task The_functions_of_each_module_of_a_split_return_through_the_shared_file_s_shapes()
    {
        var document = InferProcedure.Infer(
            "*.*",
            ("schemas.sql", "CREATE SCHEMA outlet;\nGO\nCREATE SCHEMA outpost;"),
            ("order_total.sql", OrderTotal),
            ("parcel_total.sql", ParcelTotal));
        var modules = IrModules.Of(document, document.Objects.ToDictionary(o => o.Id, o => o.Id[..o.Id.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal));

        var responses = await GeneratedFunctions.RunAsync(
            server,
            document,
            [Schema, Outpost, OrderTotal, ParcelTotal],
            [new GeneratedFunctions.Call("outlet", "orderTotal", Params(("n", 2))), new GeneratedFunctions.Call("outpost", "parcelTotal", Params(("n", 3)))],
            TestContext.Current.CancellationToken,
            modules: modules);

        Assert.Equal(
            ["""[{"n":2,"twice":4}]""", """[{"n":3,"twice":6}]"""],
            responses.Select(r => r.Error is null ? JsonSerializer.Serialize(r.Value) : r.Error.Message));
    }
}

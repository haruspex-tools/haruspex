using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Mssql;

/// <summary>
/// The functions split into a file per module, each importing the schemas of its own module's Zod file
/// (docs/decisions/0074-a-config-may-split-its-output-into-modules.md).
/// </summary>
public class MssqlModulesTests
{
    private static readonly IrDocument Document = InferProcedure.Infer(
        "*.*",
        ("schemas.sql", "CREATE SCHEMA sales;\nGO\nCREATE SCHEMA shop;"),
        ("get_order.sql", "CREATE PROCEDURE sales.get_order @order_id int AS SELECT @order_id AS order_id;"),
        ("get_product.sql", "CREATE PROCEDURE shop.get_product AS SELECT CAST(1 AS int) AS product_id;"));

    private static IrModules BySchema { get; } =
        IrModules.Of(Document, Document.Objects.ToDictionary(o => o.Id, o => o.Id[..o.Id.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal));

    [Fact]
    public void Each_module_s_functions_are_its_own_and_import_its_own_schemas()
    {
        var modules = MssqlEmitter.Emit(Document, new MssqlOptions(), BySchema, module => $"./{module}.zod.js").Expected();

        Assert.Equal(["sales", "shop"], modules.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("import type * as types from './sales.zod.js'\n", modules["sales"], StringComparison.Ordinal);
        Assert.Contains("export async function getOrder(", modules["sales"], StringComparison.Ordinal);
        Assert.DoesNotContain("getProduct", modules["sales"], StringComparison.Ordinal);
        Assert.Contains("import type * as types from './shop.zod.js'\n", modules["shop"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_s_functions_are_those_the_unsplit_file_writes_for_its_procedures()
    {
        var whole = MssqlEmitter.Emit(Document).Expected();

        var sales = MssqlEmitter.Emit(Document, new MssqlOptions(), BySchema, module => $"./{module}.zod.js").Expected()["sales"];

        var function = sales[sales.IndexOf("export async function getOrder", StringComparison.Ordinal)..];
        Assert.Contains(function, whole, StringComparison.Ordinal);
    }
}

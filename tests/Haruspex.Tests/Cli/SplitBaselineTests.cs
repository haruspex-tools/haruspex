using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex generate --baseline</c> on a config file that splits its output: the combined document
/// (docs/decisions/0073-generate-with-a-baseline-writes-types-that-hold-against-both-databases.md) is split as any document is
/// (docs/decisions/0074-a-config-may-split-its-output-into-modules.md), so a combined type lands in its procedure's module, a
/// combined shape two modules share lands in the shared file, and every file says it was combined.
/// </summary>
public class SplitBaselineTests
{
    private const string Tables = """
        CREATE SCHEMA sales;
        GO
        CREATE SCHEMA shop;
        GO
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            status varchar(10) NOT NULL CHECK (status IN ('open', 'closed')),
            channel varchar(10) NULL);
        """;

    // Released: each procedure reads the order and its status. Now: each reads its channel too, and a status may be 'held'.
    private const string Released = """
        CREATE PROCEDURE shop.get_order @order_id int AS
        SELECT o.order_id, o.status FROM shop.orders AS o WHERE o.order_id = @order_id;
        GO
        CREATE PROCEDURE shop.list_orders AS
        SELECT o.order_id, o.status FROM shop.orders AS o;
        GO
        CREATE PROCEDURE sales.open_orders AS
        SELECT o.order_id, o.status FROM shop.orders AS o WHERE o.status = 'open';
        """;

    private const string Config = """{ "source": "db", "out": "generated", "emit": ["zod", "json-schema", "mssql"], "split": "schema", "schemas": { "sales": { "include": ["*"] }, "shop": { "include": ["*"] } } }""";

    private static TempDirectory Project()
    {
        var folder = new TempDirectory();
        folder.File("haruspex.json", Config);
        folder.File("release/db/tables.sql", Tables);
        folder.File("release/db/procedures.sql", Released);
        folder.File("db/tables.sql", Tables.Replace("('open', 'closed')", "('open', 'closed', 'held')", StringComparison.Ordinal));
        folder.File("db/procedures.sql", Released
            .Replace("@order_id int AS\nSELECT o.order_id, o.status FROM", "@order_id int AS\nSELECT o.order_id, o.status, o.channel, 1 AS one FROM", StringComparison.Ordinal)
            .Replace("SELECT o.order_id, o.status FROM shop.orders AS o;", "SELECT o.order_id, o.status, o.channel FROM shop.orders AS o;", StringComparison.Ordinal)
            .Replace("SELECT o.order_id, o.status FROM shop.orders AS o WHERE o.status", "SELECT o.order_id, o.status, o.channel FROM shop.orders AS o WHERE o.status", StringComparison.Ordinal));
        return folder;
    }

    private static string Written(TempDirectory folder, string file) => File.ReadAllText(Path.Combine(folder.Path, "generated", file));

    private const string Combined = "Combined with a baseline: results accept what either database returns, and parameters take only what both accept.";

    [Fact]
    public void A_combined_type_lands_in_its_module_and_a_combined_shape_two_modules_share_in_the_shared_file()
    {
        using var folder = Project();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HaruspexCli.Run(["generate", "--baseline", "release/db"], output, error, folder.Path);

        Assert.Equal("", error.ToString());
        Assert.Equal(0, exitCode);
        Assert.Contains(
            "Wrote 8 files to generated: the IR and the modules sales and shop, with what they share. Its types work against the baseline release/db too.\n",
            CliOutput.Shown(output.ToString(), folder.Path),
            StringComparison.Ordinal);

        // shop.get_order's row is its own module's alone: the channel only the new database returns is optional, and the status
        // takes the value only the new database allows.
        var shop = Written(folder, "shop.zod.ts");
        Assert.Contains("export const GetOrderRowSchema = z.object({", shop, StringComparison.Ordinal);
        Assert.Contains("channel: z.string().max(10).nullable().optional()", shop, StringComparison.Ordinal);
        Assert.Contains("One of \"open\", \"closed\", \"held\", as the database compares strings.", shop, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrderRow", Written(folder, "sales.zod.ts"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrderRow", Written(folder, "shared.zod.ts"), StringComparison.Ordinal);

        // shop.list_orders and sales.open_orders return the same combined row, so its shape is written once, in the shared file,
        // with the same widening, and each module names it.
        var shared = Written(folder, "shared.zod.ts");
        Assert.Contains("@internal", shared, StringComparison.Ordinal);
        Assert.Contains("channel: z.string().max(10).nullable().optional()", shared, StringComparison.Ordinal);
        Assert.Contains("\"held\"", shared, StringComparison.Ordinal);
        Assert.Matches(@"export const ListOrdersRowSchema = shape[0-9a-f]{6}\n", shop);
        Assert.Matches(@"export const OpenOrdersRowSchema = shape[0-9a-f]{6}\n", Written(folder, "sales.zod.ts"));

        // Every file says it was combined, the shared one included.
        foreach (var file in new[] { "shop.zod.ts", "sales.zod.ts", "shared.zod.ts", "shop.mssql.ts", "sales.mssql.ts", "shop.jsonschema.json", "sales.jsonschema.json" })
            Assert.Contains(Combined, Written(folder, file), StringComparison.Ordinal);

        // And check with the same baseline finds every file up to date. It fails only on the deploy window
        // (docs/decisions/0069-check-with-a-baseline-judges-the-deploy-window.md), as it does unsplit: the application already
        // running refuses "held", once for each of the three procedures.
        using var checkOutput = new StringWriter();
        using var checkError = new StringWriter();
        Assert.Equal(1, HaruspexCli.Run(["check", "--baseline", "release/db"], checkOutput, checkError, folder.Path));
        var errors = checkError.ToString().ReplaceLineEndings("\n").Split('\n').Where(l => l.Contains(": error ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, errors.Count);
        Assert.All(errors, e => Assert.Contains("error HAR6026: ", e, StringComparison.Ordinal));
        Assert.DoesNotContain("generated", checkError.ToString(), StringComparison.Ordinal);
    }
}

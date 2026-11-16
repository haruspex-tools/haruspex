using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The generated Zod meets the mssql driver: procedures returning each form of JSON are created, inferred and emitted, then
/// called through the driver as an application calls them, with <c>parseJSON</c> on and off, and what the application reads
/// is parsed with the schema emitted for each. What it reads is what the project's README shows: with <c>parseJSON</c>, a
/// JSON result's one row (<c>recordset[0]</c>, null when no rows matched); without it, that row's text, parsed, or null when
/// it is empty. <c>MssqlDriverObservationTests</c> holds what the driver hands over; this holds the schemas to it.
/// </summary>
public class MssqlDriverAcceptanceTests(SqlServerFixture server)
{
    private const string Schema = "CREATE SCHEMA shop;";

    private const string StockItem = """
        CREATE TABLE shop.stock_item
        (
            item_id int          NOT NULL PRIMARY KEY,
            name       nvarchar(40) NOT NULL,
            colour     nvarchar(20) NULL
        );
        """;

    /// <summary>The procedures, each one form a procedure's JSON can take, by name.</summary>
    private static readonly Dictionary<string, string> Procedures = new(StringComparer.Ordinal)
    {
        ["list_stock"] = """
            CREATE PROCEDURE shop.list_stock @from int
            AS
            SELECT p.item_id AS id, p.name, p.colour FROM shop.stock_item AS p WHERE p.item_id >= @from ORDER BY p.item_id
            FOR JSON PATH;
            """,
        ["get_stock_item"] = """
            CREATE PROCEDURE shop.get_stock_item @id int
            AS
            SELECT p.item_id AS id, p.name, p.colour FROM shop.stock_item AS p WHERE p.item_id = @id
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            """,
        ["find_stock_item"] = """
            CREATE PROCEDURE shop.find_stock_item @id int
            AS
            SELECT p.item_id AS id, p.name FROM shop.stock_item AS p WHERE p.item_id = @id FOR JSON PATH;
            """,
        ["list_stock_auto"] = """
            CREATE PROCEDURE shop.list_stock_auto
            AS
            SELECT p.item_id, p.name FROM shop.stock_item AS p FOR JSON AUTO;
            """,
        ["stock_catalogue"] = """
            CREATE PROCEDURE shop.stock_catalogue
            AS
            SELECT p.item_id AS id, p.name FROM shop.stock_item AS p FOR JSON PATH, ROOT('items');
            """,
        ["stock_page"] = """
            CREATE PROCEDURE shop.stock_page @id int
            AS
            SELECT p.item_id AS id, p.name FROM shop.stock_item AS p WHERE p.item_id = @id;
            SELECT p.item_id AS id, p.colour FROM shop.stock_item AS p WHERE p.item_id > @id ORDER BY p.item_id FOR JSON PATH;
            """,
        ["maybe_stock"] = """
            CREATE PROCEDURE shop.maybe_stock @all bit
            AS
            IF @all = 1
                SELECT p.item_id AS id FROM shop.stock_item AS p FOR JSON PATH;
            """,
        ["stock_docs"] = """
            CREATE PROCEDURE shop.stock_docs @id int
            AS
            SELECT p.item_id AS id,
                   (SELECT q.name FROM shop.stock_item AS q WHERE q.item_id = p.item_id FOR JSON PATH) AS doc,
                   json_object('name': p.name) AS summary
            FROM shop.stock_item AS p
            WHERE p.item_id = @id;
            """,
    };

    /// <summary>
    /// Each procedure with the arguments of each case: some rows, none, and enough to be split over many rows (3,000
    /// items, about 150,000 characters).
    /// </summary>
    private static readonly (string Case, string Procedure, Dictionary<string, object?> Arguments)[] Cases =
    [
        ("many", "list_stock", new() { ["from"] = 1 }),
        ("none", "list_stock", new() { ["from"] = 100_000 }),
        ("one", "get_stock_item", new() { ["id"] = 2 }),
        ("none", "get_stock_item", new() { ["id"] = 0 }),
        ("one", "find_stock_item", new() { ["id"] = 2 }),
        ("none", "find_stock_item", new() { ["id"] = 0 }),
        ("many", "list_stock_auto", []),
        ("many", "stock_catalogue", []),
        ("both", "stock_page", new() { ["id"] = 1 }),
        ("no json", "stock_page", new() { ["id"] = 3000 }),
        ("neither", "stock_page", new() { ["id"] = 100_000 }),
        ("runs", "maybe_stock", new() { ["all"] = 1 }),
        ("does not run", "maybe_stock", new() { ["all"] = 0 }),
        ("one", "stock_docs", new() { ["id"] = 2 }),
    ];

    private static readonly bool[] ParseJsonModes = [true, false];

    [Fact]
    public async Task The_generated_zod_accepts_what_the_driver_hands_over_with_parse_json_on_and_off()
    {
        var document = InferProcedure.Infer(
            "shop.*",
            [("schema.sql", Schema), ("stock_item.sql", StockItem), .. Procedures.Select(p => ($"{p.Key}.sql", p.Value))]);
        var module = ZodEmitter.Emit(document).Expected();

        // Every second item has no colour, which FOR JSON leaves out. Another fixture may have made the schema already.
        string[] setup =
        [
            "IF SCHEMA_ID(N'shop') IS NULL EXEC (N'CREATE SCHEMA shop');",
            StockItem,
            .. Procedures.Values,
            "INSERT INTO shop.stock_item (item_id, name, colour) SELECT TOP (3000) n, CONCAT(N'item ', n), IIF(n % 2 = 0, NULL, N'green') " +
            "FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS v;",
        ];
        var calls = Cases.SelectMany(c => ParseJsonModes.Select(parseJson =>
        {
            var procedure = document.Objects.Single(o => o.Id == $"shop.{c.Procedure}");
            return new MssqlDriver.Call(
                $"{c.Procedure}, {c.Case}, parseJSON {(parseJson ? "on" : "off")}",
                parseJson,
                Procedure: procedure.Id,
                Parameters: c.Arguments,
                Check: new(procedure.Name + "Schema", [.. procedure.Results!.Select(r => r.Encoding == IrEncoding.Json ? "json" : "rowset")]));
        })).ToList();

        var responses = await MssqlDriver.RunAsync(server, module, setup, calls, TestContext.Current.CancellationToken);

        var failures = responses
            .Where(r => r.Error is not null || r.Check is not { Success: true })
            .Select(r => $"{r.Name}: {r.Error?.Message ?? string.Join("; ", r.Check?.Issues ?? [])}");
        Assert.Empty(failures);
    }
}

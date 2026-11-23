using System.Text.RegularExpressions;
using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The generated Zod meets the mssql driver: procedures returning each form of JSON are created, inferred and emitted, then
/// called through the driver as an application calls them, with <c>parseJSON</c> on and off, and what the application reads
/// is parsed with the schema emitted for each. A JSON result is read with the code the documentation gives an application
/// that calls the driver itself, taken from its page (<see cref="PageReads"/>), so that the code a reader pastes is what is
/// proved: with <c>parseJSON</c>, the result set's
/// one row, null when no rows matched or when the result set didn't arrive; without it, that row's text, parsed, or null.
/// <c>MssqlDriverObservationTests</c> holds what the driver hands over; this holds the schemas, and the page, to it.
/// </summary>
public partial class MssqlDriverAcceptanceTests(SqlServerFixture server)
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

    /// <summary>
    /// A level for each item: a <c>bigint</c> under a CHECK, from its least value, -5, to past 2^53, which a rowset hands over as
    /// the text of its digits.
    /// </summary>
    private const string StockLevel = """
        CREATE TABLE shop.stock_level
        (
            item_id int    NOT NULL PRIMARY KEY,
            qty     bigint NOT NULL CHECK (qty >= -5 AND qty < 9223372036854775807)
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
        ["stock_levels"] = """
            CREATE PROCEDURE shop.stock_levels
            AS
            SELECT l.item_id AS id, l.qty, -l.qty AS negated FROM shop.stock_level AS l;
            """,
        // Every date and time type, its fraction a different number of digits on each row and none on some (a FOR JSON date
        // leaves a zero fraction out), and every kind of offset: the schemas hold each to the form its type is written in.
        ["stock_dates"] = """
            CREATE PROCEDURE shop.stock_dates
            AS
            SELECT p.item_id AS id,
                   CAST(DATEADD(millisecond, p.item_id * 7, CAST('2026-03-04T05:06:07' AS datetime2(7))) AS datetime2(3)) AS d3,
                   DATEADD(microsecond, p.item_id * 100, CAST('2026-03-04T05:06:07' AS datetime2(7))) AS d7,
                   CAST(DATEADD(millisecond, p.item_id * 7, CAST('2026-03-04T05:06:07' AS datetime2(7))) AS datetime2(0)) AS d0,
                   DATEADD(millisecond, p.item_id * 7, CAST('2026-03-04T05:06:07' AS datetime)) AS dt,
                   DATEADD(second, p.item_id * 7, CAST('2026-03-04T05:06:00' AS smalldatetime)) AS sdt,
                   CAST(DATEADD(millisecond, p.item_id * 100, CAST('2026-03-04T05:06:07' AS datetime2(7))) AS time(1)) AS t1,
                   CAST(DATEADD(millisecond, p.item_id * 7, CAST('2026-03-04T05:06:07' AS datetime2(7))) AS time(0)) AS t0,
                   TODATETIMEOFFSET(CAST(DATEADD(millisecond, p.item_id * 10, CAST('2026-03-04T05:06:07' AS datetime2(7))) AS datetime2(2)), (p.item_id % 3 - 1) * 330) AS o2,
                   CAST(DATEADD(day, p.item_id, CAST('2026-03-04' AS date)) AS date) AS d
            FROM shop.stock_item AS p
            ORDER BY p.item_id
            FOR JSON PATH;
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
        ("many", "stock_levels", []),
        ("many", "stock_dates", []),
    ];

    private static readonly bool[] ParseJsonModes = [true, false];

    /// <summary>The page that shows an application calling the driver itself, with the reads this test runs.</summary>
    private const string Page = "docs/database-module.md";

    /// <summary>
    /// The page's reads of <c>Website.GetOrder</c>'s FOR JSON result, in its section on calling the driver yourself, as
    /// bodies of a JavaScript function of <c>result</c>: with <c>parseJSON</c>, what it hands the schema; without, its line
    /// taking the text (its type annotation dropped), then what it hands the schema.
    /// </summary>
    private static (string ParseJson, string Text) PageReads()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryPaths.Root, Page));
        var parsed = ParsedOnPage().Matches(page);
        var text = TextOnPage().Match(page);
        Assert.True(
            parsed.Count == 2 && text.Success,
            $"{Page} doesn't read GetOrder's result in the two forms this test runs: a line `const text: string = …` and two lines `return GetOrderSchema.parse(…)`.");
        return ($"return {parsed[0].Groups[1].Value}", $"const text = {text.Groups[1].Value}\nreturn {parsed[1].Groups[1].Value}");
    }

    [GeneratedRegex(@"^\s*return GetOrderSchema\.parse\((.+)\)$", RegexOptions.Multiline)]
    private static partial Regex ParsedOnPage();

    [GeneratedRegex(@"^const text: string = (.+)$", RegexOptions.Multiline)]
    private static partial Regex TextOnPage();

    [Fact]
    public async Task The_generated_zod_accepts_what_the_driver_hands_over_with_parse_json_on_and_off()
    {
        var document = InferProcedure.Infer(
            "shop.*",
            [("schema.sql", Schema), ("stock_item.sql", StockItem), ("stock_level.sql", StockLevel), .. Procedures.Select(p => ($"{p.Key}.sql", p.Value))]);
        var module = ZodEmitter.Emit(document).Expected();
        var (readParsed, readText) = PageReads();

        // Every second item has no colour, which FOR JSON leaves out. Another fixture may have made the schema already.
        string[] setup =
        [
            "IF SCHEMA_ID(N'shop') IS NULL EXEC (N'CREATE SCHEMA shop');",
            StockItem,
            StockLevel,
            .. Procedures.Values,
            "INSERT INTO shop.stock_item (item_id, name, colour) SELECT TOP (3000) n, CONCAT(N'item ', n), IIF(n % 2 = 0, NULL, N'green') " +
            "FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS v;",

            // From the CHECK's least value, -5, through 0 to past 2^53, a level for every item.
            "INSERT INTO shop.stock_level (item_id, qty) SELECT p.item_id, IIF(p.item_id <= 10, p.item_id - 6, (CAST(p.item_id AS bigint) - 1) * 3074457345618258) FROM shop.stock_item AS p;",
        ];
        var calls = Cases.SelectMany(c => ParseJsonModes.Select(parseJson =>
        {
            var procedure = document.Objects.Single(o => o.Id == $"shop.{c.Procedure}");
            return new MssqlDriver.Call(
                $"{c.Procedure}, {c.Case}, parseJSON {(parseJson ? "on" : "off")}",
                parseJson,
                Procedure: procedure.Id,
                Parameters: c.Arguments,
                Check: new(
                    procedure.Name + "Schema",
                    [.. procedure.Results!.Select(r => r.Encoding == IrEncoding.Json ? "json" : "rowset")],
                    parseJson ? readParsed : readText));
        })).ToList();

        var responses = await MssqlDriver.RunAsync(server, module, setup, calls, TestContext.Current.CancellationToken);

        var failures = responses
            .Where(r => r.Error is not null || r.Check is not { Success: true })
            .Select(r => $"{r.Name}: {r.Error?.Message ?? string.Join("; ", r.Check?.Issues ?? [])}");
        Assert.Empty(failures);
    }
}

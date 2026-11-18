using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The fit of one database within another (<see cref="IrFit"/>), held to the server. Each pair is a procedure as an
/// application was built against it (the baseline) and as the database now defines it (the candidate). Both versions are
/// deployed, each in a transaction of its own that is rolled back, and the application's generated functions, written from the
/// baseline, call each: against the baseline every call must return, and against the candidate a call must return exactly
/// when the operation finds nothing refused. A pair that doesn't fit makes the candidate return, or be sent, a real value the
/// application refuses or the procedure does: each such call must fail, and for the reason the finding gives, or, where the
/// server changes a value without an error, return something other than what was sent.
/// </summary>
/// <remarks>
/// The procedures are invented here, in schema <c>drift</c>, which no fixture uses. Every call of a run is made in the same
/// transaction, so the pairs are deployed together, one database for the baselines and one for the candidates.
/// </remarks>
public class BaselineFitTests(SqlServerFixture server)
{
    private const string Schema = "IF SCHEMA_ID(N'drift') IS NULL EXEC (N'CREATE SCHEMA drift');";

    /// <summary>How a call through the baseline's function ends against the candidate.</summary>
    /// <param name="Error">Null when it returns; <c>HaruspexResultError</c> when a schema refuses what came back; <c>RequestError</c> when the server refuses the call.</param>
    /// <param name="Number">For a <c>RequestError</c>, the server's error number.</param>
    private sealed record Ending(string? Error, int? Number = null)
    {
        public static Ending Returns { get; } = new(Error: null);

        public static Ending ResultRefused { get; } = new("HaruspexResultError");

        public static Ending Server(int number) => new("RequestError", number);
    }

    /// <summary>One pair: the procedure the application was built against, the one the database now has, a call, and what to expect.</summary>
    /// <param name="Name">What the pair shows, and the call's name.</param>
    /// <param name="Baseline">The procedure as the application knew it.</param>
    /// <param name="Candidate">The procedure as the database now defines it; null when it is gone.</param>
    /// <param name="Findings">What the operation finds, as <see cref="IrFitFinding.ToString"/> words each.</param>
    /// <param name="Ends">How the call ends against the candidate.</param>
    private sealed record Pair(string Name, string Baseline, string? Candidate, string[] Findings, Ending Ends)
    {
        /// <summary>The call's arguments, by name without the <c>@</c>; null for a function that takes none.</summary>
        public Dictionary<string, object?>? Params { get; init; }

        /// <summary>
        /// For a value the server may change without an error, what the call returns when the value sent arrived as sent: the
        /// first column of the first row. A call that returns anything else returned, but not what was sent.
        /// </summary>
        public string? Echoed { get; init; }

        /// <summary>The findings under <c>"objects": "strict"</c>, when they differ.</summary>
        public string[]? StrictFindings { get; init; }

        /// <summary>How the call ends under <c>"objects": "strict"</c>, when it differs.</summary>
        public Ending? StrictEnds { get; init; }

        /// <summary>The procedure's id.</summary>
        public string Id => Baseline.Split(' ', 4)[2];
    }

    /// <summary>A table both versions read, whose CHECK grows in the candidate, with a row only the candidate allows. An exact set of numbers, which every schema checks: a set of strings matched under a case-insensitive collation is a comment in the Zod module, which a value outside it passes.</summary>
    private static readonly (string Baseline, string Candidate) Tickets = (
        "CREATE TABLE drift.ticket (grade int NOT NULL CHECK (grade IN (1, 2)));",
        "CREATE TABLE drift.ticket (grade int NOT NULL CHECK (grade IN (1, 2, 3)));");

    private static readonly (string Baseline, string Candidate) TicketRows = (
        "INSERT INTO drift.ticket (grade) VALUES (1);",
        "INSERT INTO drift.ticket (grade) VALUES (3);");

    private static readonly Pair[] Pairs =
    [
        new("a column that becomes nullable",
            "CREATE PROCEDURE drift.get_note AS SELECT CAST(1 AS int) AS id, CAST(N'a' AS nvarchar(10)) AS note;",
            "CREATE PROCEDURE drift.get_note AS SELECT CAST(1 AS int) AS id, CAST(NULL AS nvarchar(10)) AS note;",
            ["refused: drift.get_note results[0][].note: became nullable"],
            Ending.ResultRefused),
        new("a column that becomes non-null",
            "CREATE PROCEDURE drift.get_memo AS SELECT CAST(1 AS int) AS id, CAST(NULL AS nvarchar(10)) AS memo;",
            "CREATE PROCEDURE drift.get_memo AS SELECT CAST(1 AS int) AS id, CAST(N'a' AS nvarchar(10)) AS memo;",
            [],
            Ending.Returns),
        new("a new column",
            "CREATE PROCEDURE drift.get_label AS SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE drift.get_label AS SELECT CAST(1 AS int) AS id, CAST(2 AS int) AS extra;",
            ["ignored: drift.get_label results[0][].extra: added (number (int32), int)"],
            Ending.Returns)
        {
            StrictFindings = ["refused: drift.get_label results[0][].extra: added (number (int32), int)"],
            StrictEnds = Ending.ResultRefused,
        },
        new("a column removed",
            "CREATE PROCEDURE drift.get_tag AS SELECT CAST(1 AS int) AS id, CAST(N'a' AS nvarchar(10)) AS tag;",
            "CREATE PROCEDURE drift.get_tag AS SELECT CAST(1 AS int) AS id;",
            ["refused: drift.get_tag results[0][].tag: removed (string, nvarchar(10))"],
            Ending.ResultRefused),
        new("a longer string",
            "CREATE PROCEDURE drift.get_code AS SELECT CAST(N'abc' AS nvarchar(3)) AS code;",
            "CREATE PROCEDURE drift.get_code AS SELECT CAST(N'abcdef' AS nvarchar(6)) AS code;",
            ["refused: drift.get_code results[0][].code: length changed from at most 3 to at most 6"],
            Ending.ResultRefused),
        new("a shorter string",
            "CREATE PROCEDURE drift.get_word AS SELECT CAST(N'abcdef' AS nvarchar(6)) AS word;",
            "CREATE PROCEDURE drift.get_word AS SELECT CAST(N'abc' AS nvarchar(3)) AS word;",
            [],
            Ending.Returns),
        new("a value set that grows",
            "CREATE PROCEDURE drift.list_tickets AS SELECT t.grade FROM drift.ticket AS t;",
            "CREATE PROCEDURE drift.list_tickets AS SELECT t.grade FROM drift.ticket AS t;",
            ["refused: drift.list_tickets results[0][].grade: value set grew: added 3"],
            Ending.ResultRefused),
        new("a wider integer",
            "CREATE PROCEDURE drift.get_qty AS SELECT CAST(200 AS tinyint) AS qty;",
            "CREATE PROCEDURE drift.get_qty AS SELECT CAST(300 AS smallint) AS qty;",
            ["refused: drift.get_qty results[0][].qty: type changed from number (uint8) to number (int16)"],
            Ending.ResultRefused),
        new("a narrower integer",
            "CREATE PROCEDURE drift.get_count AS SELECT CAST(300 AS smallint) AS n;",
            "CREATE PROCEDURE drift.get_count AS SELECT CAST(200 AS tinyint) AS n;",
            [],
            Ending.Returns),
        new("a new trailing result set",
            "CREATE PROCEDURE drift.get_sheet AS SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE drift.get_sheet AS SELECT CAST(1 AS int) AS id; SELECT CAST(2 AS int) AS more;",
            ["ignored: drift.get_sheet results[1]: added"],
            Ending.Returns)
        {
            StrictFindings = ["refused: drift.get_sheet results[1]: added"],
            StrictEnds = Ending.ResultRefused,
        },
        new("a result set removed",
            "CREATE PROCEDURE drift.get_pair AS SELECT CAST(1 AS int) AS id; SELECT CAST(2 AS int) AS more;",
            "CREATE PROCEDURE drift.get_pair AS SELECT CAST(1 AS int) AS id;",
            ["refused: drift.get_pair results[1]: removed"],
            Ending.ResultRefused),
        new("a result set that may now be absent",
            "CREATE PROCEDURE drift.get_both @show int AS SELECT CAST(1 AS int) AS id; SELECT CAST(2 AS int) AS more;",
            "CREATE PROCEDURE drift.get_both @show int AS SELECT CAST(1 AS int) AS id; IF @show = 1 SELECT CAST(2 AS int) AS more;",
            [
                "refused: drift.get_both results[1]: may now be absent",
                "refused: drift.get_both results[1]: number of items changed from exactly 1 to at most 1",
            ],
            Ending.ResultRefused)
        {
            Params = new() { ["show"] = 0 },
        },
        new("a key that may now be left out",
            "CREATE PROCEDURE drift.get_card AS SELECT CAST(1 AS int) AS id, CAST(N'a' AS nvarchar(10)) AS note FOR JSON PATH;",
            "CREATE PROCEDURE drift.get_card AS SELECT CAST(1 AS int) AS id, CAST(NULL AS nvarchar(10)) AS note FOR JSON PATH;",
            [
                "refused: drift.get_card results[0][].note: became nullable",
                "refused: drift.get_card results[0][].note: presence changed from always to omittedWhenNull",
            ],
            Ending.ResultRefused),
        new("a new parameter without a default",
            "CREATE PROCEDURE drift.find_order @id int AS SELECT @id AS id;",
            "CREATE PROCEDURE drift.find_order @id int, @channel varchar(10) AS SELECT @id AS id;",
            ["refused: drift.find_order parameters[\"@channel\"]: added (string, varchar(10))"],
            Ending.Server(201))
        {
            Params = new() { ["id"] = 1 },
        },
        new("a new parameter with a default",
            "CREATE PROCEDURE drift.find_line @id int AS SELECT @id AS id;",
            "CREATE PROCEDURE drift.find_line @id int, @channel varchar(10) = 'web' AS SELECT @id AS id;",
            ["ignored: drift.find_line parameters[\"@channel\"]: added (string, varchar(10))"],
            Ending.Returns)
        {
            Params = new() { ["id"] = 1 },
        },
        new("a parameter removed",
            "CREATE PROCEDURE drift.find_item @id int, @channel varchar(10) = 'web' AS SELECT @id AS id;",
            "CREATE PROCEDURE drift.find_item @id int AS SELECT @id AS id;",
            ["refused: drift.find_item parameters[\"@channel\"]: removed (string, varchar(10))"],
            Ending.Server(8144))
        {
            Params = new() { ["id"] = 1, ["channel"] = "shop" },
        },
        new("a parameter that loses its default",
            "CREATE PROCEDURE drift.find_batch @id int = 0 AS SELECT @id AS id;",
            "CREATE PROCEDURE drift.find_batch @id int AS SELECT @id AS id;",
            ["refused: drift.find_batch parameters[\"@id\"]: no longer has a default"],
            Ending.Server(201))
        {
            Params = [],
        },
        new("a narrower parameter",
            "CREATE PROCEDURE drift.find_size @qty int AS SELECT CAST(@qty AS int) AS qty;",
            "CREATE PROCEDURE drift.find_size @qty tinyint AS SELECT CAST(@qty AS int) AS qty;",
            ["refused: drift.find_size parameters[\"@qty\"]: type changed from number (int32) to number (uint8)"],
            Ending.Server(8114))
        {
            Params = new() { ["qty"] = 300 },
        },
        new("a wider parameter",
            "CREATE PROCEDURE drift.find_lot @qty int AS SELECT CAST(@qty AS int) AS qty;",
            "CREATE PROCEDURE drift.find_lot @qty bigint AS SELECT CAST(@qty AS int) AS qty;",
            [],
            Ending.Returns)
        {
            Params = new() { ["qty"] = 300 },
        },
        new("a parameter no longer output",
            "CREATE PROCEDURE drift.count_lines @n int = NULL OUTPUT AS SET @n = 3; SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE drift.count_lines @n int = NULL AS SELECT CAST(1 AS int) AS id;",
            ["refused: drift.count_lines parameters[\"@n\"]: is no longer an output parameter"],
            Ending.Server(8162))
        {
            Params = new() { ["n"] = 0 },
        },
        new("a key left out when null that now arrives as null",
            "CREATE PROCEDURE drift.get_slip AS SELECT CAST(1 AS int) AS id, CAST(NULL AS nvarchar(10)) AS note FOR JSON PATH;",
            "CREATE PROCEDURE drift.get_slip AS SELECT CAST(1 AS int) AS id, CAST(NULL AS nvarchar(10)) AS note FOR JSON PATH, INCLUDE_NULL_VALUES;",
            ["refused: drift.get_slip results[0][].note: presence changed from omittedWhenNull to always"],
            Ending.ResultRefused),
        new("a JSON result that may now not be sent",
            "CREATE PROCEDURE drift.get_stub @show int AS SELECT CAST(1 AS int) AS id WHERE @show = 1 FOR JSON PATH;",
            "CREATE PROCEDURE drift.get_stub @show int AS IF @show = 1 SELECT CAST(1 AS int) AS id FOR JSON PATH;",
            ["refused: drift.get_stub results[0]: may now be absent"],
            Ending.ResultRefused)
        {
            Params = new() { ["show"] = 0 },
        },
        new("a decimal with more places",
            "CREATE PROCEDURE drift.get_price AS SELECT CAST(1.25 AS decimal(10, 2)) AS price;",
            "CREATE PROCEDURE drift.get_price AS SELECT CAST(1.255 AS decimal(12, 3)) AS price;",
            [
                "refused: drift.get_price results[0][].price: lower bound changed from at least -99999999.99 to at least -999999999.999",
                "refused: drift.get_price results[0][].price: upper bound changed from at most 99999999.99 to at most 999999999.999",
                "refused: drift.get_price results[0][].price: digits changed from precision 10, scale 2 to precision 12, scale 3",
            ],
            Ending.ResultRefused),
        new("a date and time parameter of a narrower range",
            "CREATE PROCEDURE drift.find_slot @d datetime2(7) AS SELECT CONVERT(varchar(27), @d, 126) AS d;",
            "CREATE PROCEDURE drift.find_slot @d smalldatetime AS SELECT CONVERT(varchar(27), @d, 126) AS d;",
            ["refused: drift.find_slot parameters[\"@d\"]: SQL type changed from datetime2(7) to smalldatetime"],
            Ending.Server(8114))
        {
            Params = new() { ["d"] = "2100-01-01T00:00:00" },
        },
        new("a date and time parameter of a coarser fraction",
            "CREATE PROCEDURE drift.find_tick @d datetime2(7) AS SELECT CONVERT(varchar(27), @d, 126) AS d;",
            "CREATE PROCEDURE drift.find_tick @d datetime2(0) AS SELECT CONVERT(varchar(27), @d, 126) AS d;",
            ["refused: drift.find_tick parameters[\"@d\"]: SQL type changed from datetime2(7) to datetime2(0)"],
            Ending.Returns)
        {
            Params = new() { ["d"] = "2026-11-16T08:30:00.1234567" },
            Echoed = "2026-11-16T08:30:00.1234567",
        },
        new("a parameter whose type is unknown",
            "CREATE PROCEDURE drift.find_node @b int AS SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE drift.find_node @b hierarchyid AS SELECT CAST(1 AS int) AS id;",
            ["refused: drift.find_node parameters[\"@b\"]: type changed from number (int32) to unknown (unsupported-sql-type)"],
            Ending.Server(206))
        {
            Params = new() { ["b"] = 1 },
        },
        new("a procedure that is gone",
            "CREATE PROCEDURE drift.retired AS SELECT CAST(1 AS int) AS id;",
            null,
            ["refused: drift.retired: removed"],
            Ending.Server(2812)),
    ];

    private static IrDocument Module(Func<Pair, string?> procedure, string tickets) =>
        InferProcedure.Infer(
            "drift.*",
            [("schema.sql", "CREATE SCHEMA drift;"), ("ticket.sql", tickets), .. Pairs.Select(procedure).OfType<string>().Select((p, i) => ($"p{i}.sql", p))]);

    private static IrDocument Baselines => Module(p => p.Baseline, Tickets.Baseline);

    private static IrDocument Candidates => Module(p => p.Candidate, Tickets.Candidate);

    private static IReadOnlyList<string> Deployed(Func<Pair, string?> procedure, string tickets, string rows) =>
        [Schema, tickets, rows, .. Pairs.Select(procedure).OfType<string>()];

    private static List<GeneratedFunctions.Call> Calls(IrDocument baselines) =>
        [.. Pairs.Select(p => new GeneratedFunctions.Call(p.Name, MssqlEmitter.FunctionName(baselines.Objects.Single(o => o.Id == p.Id).Name), p.Params))];

    [Fact]
    public async Task Every_call_returns_from_the_database_the_application_was_built_against()
    {
        var baselines = Baselines;

        var responses = await GeneratedFunctions.RunAsync(
            server, baselines, Deployed(p => p.Baseline, Tickets.Baseline, TicketRows.Baseline), Calls(baselines), TestContext.Current.CancellationToken);

        Assert.All(responses, r => Assert.True(r.Error is null, $"{r.Name}: {r.Error}"));
        Assert.All(Pairs.Zip(responses).Where(p => p.First.Echoed is not null), p => Assert.Equal(p.First.Echoed, First(p.Second)));
    }

    /// <summary>The first column of the first row a call returned, as text; null when there is none.</summary>
    private static string? First(GeneratedFunctions.Response response) =>
        response.Value is { ValueKind: System.Text.Json.JsonValueKind.Array } rows && rows.GetArrayLength() > 0
            ? rows[0].EnumerateObject().Select(c => c.Value.ToString()).FirstOrDefault()
            : null;

    [Fact]
    public void Each_pair_has_the_findings_it_is_written_with()
    {
        var findings = IrFit.Of(Baselines, Candidates, new IrFitOptions());
        var strict = IrFit.Of(Baselines, Candidates, new IrFitOptions { StrictObjects = true });

        Assert.All(Pairs, p =>
        {
            Assert.Equal(p.Findings, findings.Where(f => f.Change.Object == p.Id).Select(f => f.ToString()));
            Assert.Equal(p.StrictFindings ?? p.Findings, strict.Where(f => f.Change.Object == p.Id).Select(f => f.ToString()));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_returns_from_the_candidate_exactly_when_nothing_is_refused_and_otherwise_fails_as_the_finding_says(bool strict)
    {
        var baselines = Baselines;
        var findings = IrFit.Of(baselines, Candidates, new IrFitOptions { StrictObjects = strict });

        var responses = await GeneratedFunctions.RunAsync(
            server, baselines, Deployed(p => p.Candidate, Tickets.Candidate, TicketRows.Candidate), Calls(baselines), TestContext.Current.CancellationToken, strict);

        Assert.All(Pairs.Zip(responses), pair =>
        {
            var (p, response) = pair;
            var fits = findings.Where(f => f.Change.Object == p.Id).All(f => f.Verdict == IrFitVerdict.Ignored);
            var ends = (strict ? p.StrictEnds : null) ?? p.Ends;
            var asSent = response.Error is null && (p.Echoed is null || p.Echoed == First(response));
            Assert.True(fits == asSent, $"{p.Name}: the operation says it {(fits ? "fits" : "doesn't")}, and the call {(response.Error is null ? $"returned {First(response)}" : $"failed: {response.Error}")}");
            Assert.Equal(ends, new Ending(response.Error?.Name, response.Error is { IsRequestError: true } ? response.Error.Number : null));
        });
    }
}

using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Combined types (<see cref="IrCombination"/>), held to the server. Each pair is a procedure as the database had it before a
/// change (the baseline) and as it has it after (the candidate). Both versions are deployed, each in a transaction of its own that
/// is rolled back, and the functions generated from the combined document call each: every call must return, with every value the
/// combined schemas check accepted, against the database before the change and after it. A pair whose change can't be bridged is
/// left out of the combined document, with the finding it is written with; for each, the candidate's own function, the one a single
/// release would ship, is called against the baseline's database and fails as the finding says.
/// </summary>
/// <remarks>
/// The procedures are invented here, in schema <c>blend</c>, which no fixture uses. Every call of a run is made in the same
/// transaction, so the pairs are deployed together, one database for the baselines and one for the candidates.
/// </remarks>
public class CombinedTypesTests(SqlServerFixture server)
{
    private const string Schema = "IF SCHEMA_ID(N'blend') IS NULL EXEC (N'CREATE SCHEMA blend');";

    /// <summary>One pair: the procedure before the change and after, a call, and what combining them finds.</summary>
    /// <param name="Name">What the pair shows, and the call's name.</param>
    /// <param name="Baseline">The procedure before the change.</param>
    /// <param name="Candidate">The procedure after it.</param>
    /// <param name="Findings">What combining finds, as <see cref="IrCombinationFinding.ToString"/> words each; an unbridgeable one leaves the pair out.</param>
    private sealed record Pair(string Name, string Baseline, string Candidate, string[] Findings)
    {
        /// <summary>The call's arguments, by name without the <c>@</c>; null for a function that takes none.</summary>
        public Dictionary<string, object?>? Params { get; init; }

        /// <summary>
        /// For a pair left out, how the candidate's own function fails against the baseline's database: a server error's number, or
        /// null when its schema refuses what came back.
        /// </summary>
        public int? Fails { get; init; }

        /// <summary>
        /// For a call that sends a value the server may change without an error, the first row the call returns when each arrived
        /// as sent, as JSON: a narrower type's largest values must be taken exactly by both procedures.
        /// </summary>
        public string? Echoed { get; init; }

        /// <summary>The procedure's id.</summary>
        public string Id => Baseline.Split(' ', 4)[2];

        /// <summary>Whether combining bridges the change, so that the combined document has the procedure.</summary>
        public bool Bridged => !Findings.Any(f => f.StartsWith("unbridgeable:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Tables both versions read: a ticket's CHECK grows in the candidate, and a tier's shrinks, each with a row only its own
    /// version allows, so that the combined value set must hold both's values.
    /// </summary>
    private static readonly (string Baseline, string Candidate) Tickets = (
        "CREATE TABLE blend.ticket (grade int NOT NULL CHECK (grade IN (1, 2)), note nvarchar(10) NOT NULL, tier int NOT NULL CHECK (tier IN (1, 2)));",
        "CREATE TABLE blend.ticket (grade int NOT NULL CHECK (grade IN (1, 2, 3)), note nvarchar(10) NULL, tier int NOT NULL CHECK (tier IN (1)));");

    private static readonly (string Baseline, string Candidate) TicketRows = (
        "INSERT INTO blend.ticket (grade, note, tier) VALUES (1, N'a', 2);",
        "INSERT INTO blend.ticket (grade, note, tier) VALUES (3, NULL, 1);");

    /// <summary>The procedure whose sets the config file names, and the names each version's reading gives them.</summary>
    private const string Named = "blend.get_named";

    private static readonly Pair[] Pairs =
    [
        new("a key only the candidate returns, nullable",
            "CREATE PROCEDURE blend.get_order AS SELECT CAST(1 AS int) AS order_id, CAST('open' AS varchar(10)) AS status;",
            "CREATE PROCEDURE blend.get_order AS SELECT CAST(1 AS int) AS order_id, CAST('open' AS varchar(10)) AS status, CAST(NULL AS varchar(10)) AS channel;",
            []),
        new("a key only the baseline returns",
            "CREATE PROCEDURE blend.get_tag AS SELECT CAST(1 AS int) AS id, CAST(N'a' AS nvarchar(10)) AS tag;",
            "CREATE PROCEDURE blend.get_tag AS SELECT CAST(1 AS int) AS id;",
            []),
        new("a column nullable in the candidate, a value set that grew, and one that shrank",
            "CREATE PROCEDURE blend.list_tickets AS SELECT t.grade, t.note, t.tier FROM blend.ticket AS t;",
            "CREATE PROCEDURE blend.list_tickets AS SELECT t.grade, t.note, t.tier FROM blend.ticket AS t;",
            []),
        new("a bigint where there was an int",
            "CREATE PROCEDURE blend.get_big AS SELECT CAST(7 AS int) AS n;",
            "CREATE PROCEDURE blend.get_big AS SELECT CAST(3000000000 AS bigint) AS n;",
            []),
        new("named sets, a third added",
            "CREATE PROCEDURE blend.get_named AS SELECT CAST(1 AS int) AS id; SELECT CAST(2 AS int) AS line;",
            "CREATE PROCEDURE blend.get_named AS SELECT CAST(1 AS int) AS id; SELECT CAST(2 AS int) AS line; SELECT CAST(3 AS int) AS note;",
            []),
        new("a longer string",
            "CREATE PROCEDURE blend.get_code AS SELECT CAST(N'abc' AS nvarchar(3)) AS code;",
            "CREATE PROCEDURE blend.get_code AS SELECT CAST(N'abcdef' AS nvarchar(6)) AS code;",
            []),
        new("a wider integer",
            "CREATE PROCEDURE blend.get_qty AS SELECT CAST(200 AS tinyint) AS qty;",
            "CREATE PROCEDURE blend.get_qty AS SELECT CAST(300 AS smallint) AS qty;",
            []),
        new("a value of another kind",
            "CREATE PROCEDURE blend.get_amount AS SELECT CAST(N'ten' AS nvarchar(5)) AS amount;",
            "CREATE PROCEDURE blend.get_amount AS SELECT CAST(10 AS int) AS amount;",
            []),
        new("a trailing result set only the candidate returns",
            "CREATE PROCEDURE blend.get_sheet AS SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE blend.get_sheet AS SELECT CAST(1 AS int) AS id; SELECT CAST(2 AS int) AS more;",
            []),
        new("a trailing result set only the baseline returns",
            "CREATE PROCEDURE blend.get_pair AS SELECT CAST(1 AS int) AS id; SELECT CAST(2 AS int) AS more;",
            "CREATE PROCEDURE blend.get_pair AS SELECT CAST(1 AS int) AS id;",
            []),
        new("a JSON key left out when null in one and sent as null in the other",
            "CREATE PROCEDURE blend.get_card AS SELECT CAST(1 AS int) AS id, CAST(NULL AS nvarchar(10)) AS note FOR JSON PATH;",
            "CREATE PROCEDURE blend.get_card AS SELECT CAST(1 AS int) AS id, CAST(NULL AS nvarchar(10)) AS note FOR JSON PATH, INCLUDE_NULL_VALUES;",
            []),
        new("a key renamed",
            "CREATE PROCEDURE blend.get_person AS SELECT CAST(1 AS int) AS id, CAST(N'Ann' AS nvarchar(10)) AS customerName;",
            "CREATE PROCEDURE blend.get_person AS SELECT CAST(1 AS int) AS id, CAST(N'Ann' AS nvarchar(10)) AS customer_name;",
            ["possible rename: blend.get_person results[0][]: customerName went and customer_name appeared; if it was renamed, read both until the baseline moves on"]),
        new("a parameter only the candidate has, with a default",
            "CREATE PROCEDURE blend.find_line @id int AS SELECT @id AS id;",
            "CREATE PROCEDURE blend.find_line @id int, @channel varchar(10) = 'web' AS SELECT @id AS id;",
            [])
        {
            Params = new() { ["id"] = 1 },
        },
        new("a parameter only the baseline has, with a default",
            "CREATE PROCEDURE blend.find_item @id int, @channel varchar(10) = 'web' AS SELECT @id AS id;",
            "CREATE PROCEDURE blend.find_item @id int AS SELECT @id AS id;",
            [])
        {
            Params = new() { ["id"] = 1 },
        },
        new("a parameter narrowed, sent at the narrower type's largest",
            "CREATE PROCEDURE blend.find_size @qty int, @code nvarchar(20) AS SELECT CAST(@qty AS int) AS qty, @code AS code;",
            "CREATE PROCEDURE blend.find_size @qty tinyint, @code nvarchar(10) AS SELECT CAST(@qty AS int) AS qty, @code AS code;",
            [])
        {
            Params = new() { ["qty"] = 255, ["code"] = "abcdefghij" },
            Echoed = """{"qty":255,"code":"abcdefghij"}""",
        },
        new("a date narrowed, sent at the narrower type's latest",
            "CREATE PROCEDURE blend.find_day @at datetime2(7) AS SELECT CONVERT(varchar(19), @at, 126) AS at;",
            "CREATE PROCEDURE blend.find_day @at smalldatetime AS SELECT CONVERT(varchar(19), @at, 126) AS at;",
            [])
        {
            Params = new() { ["at"] = "2079-06-06T23:59:00" },
            Echoed = """{"at":"2079-06-06T23:59:00"}""",
        },
        new("an OUTPUT parameter of the same type in both, its procedure otherwise changed",
            "CREATE PROCEDURE blend.count_lines @n int OUTPUT AS SET @n = 5; SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE blend.count_lines @n int OUTPUT AS SET @n = 6; SELECT CAST(1 AS int) AS id, CAST(NULL AS int) AS more;",
            [])
        {
            Params = new() { ["n"] = 0 },
        },
        new("an OUTPUT parameter in one only, sent plain to both",
            "CREATE PROCEDURE blend.count_rows @n int AS SELECT @n AS n;",
            "CREATE PROCEDURE blend.count_rows @n int OUTPUT AS SELECT @n AS n;",
            [])
        {
            Params = new() { ["n"] = 4 },
        },
        new("a new required parameter",
            "CREATE PROCEDURE blend.place_order @id int AS SELECT @id AS id;",
            "CREATE PROCEDURE blend.place_order @id int, @channel varchar(10) AS SELECT @id AS id;",
            ["unbridgeable: blend.place_order parameters[\"@channel\"]: gained the required parameter @channel, which the baseline's procedure doesn't take: give it a default first, or ship the change in two releases"])
        {
            Params = new() { ["id"] = 1, ["channel"] = "web" },
            Fails = 8144,
        },
        new("a parameter the baseline requires, removed",
            "CREATE PROCEDURE blend.find_batch @id int, @batch int AS SELECT @id AS id;",
            "CREATE PROCEDURE blend.find_batch @id int AS SELECT @id AS id;",
            ["unbridgeable: blend.find_batch parameters[\"@batch\"]: lost the parameter @batch, which the baseline's procedure requires: give it a default first, or ship the change in two releases"])
        {
            Params = new() { ["id"] = 1 },
            Fails = 201,
        },
        new("a parameter of another kind",
            "CREATE PROCEDURE blend.find_code @code int AS SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE blend.find_code @code nvarchar(10) AS SELECT CAST(1 AS int) AS id;",
            ["unbridgeable: blend.find_code parameters[\"@code\"]: changed type from int to nvarchar(10), which have no value in common the function could send: ship the change in two releases"])
        {
            Params = new() { ["code"] = "abc" },
            Fails = 8114,
        },
        new("rows that become JSON",
            "CREATE PROCEDURE blend.get_doc AS SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE blend.get_doc AS SELECT CAST(1 AS int) AS id FOR JSON PATH;",
            ["unbridgeable: blend.get_doc results[0]: changed from rows to JSON, which one function can't read both ways: ship the change in two releases"])
        {
            Fails = null,
        },

        // Last: a value returned to an OUTPUT parameter that its declared type can't hold fails the call (8114) and rolls back the
        // transaction the calls share, which a call after it would find gone.
        new("an OUTPUT parameter narrowed",
            "CREATE PROCEDURE blend.count_all @n bigint OUTPUT AS SET @n = 3000000000; SELECT CAST(1 AS int) AS id;",
            "CREATE PROCEDURE blend.count_all @n int OUTPUT AS SET @n = 5; SELECT CAST(1 AS int) AS id;",
            ["unbridgeable: blend.count_all parameters[\"@n\"]: changed the type of the OUTPUT parameter @n from bigint to int: it is declared as one type, which can't both send only what both take and hold what both give back, so ship the change in two releases"])
        {
            Params = new() { ["n"] = 0 },
            Fails = 8114,
        },
    ];

    /// <summary>The pairs' module, its <see cref="Named"/> procedure's sets named as a config file's <c>sets</c> list names them.</summary>
    private static IrDocument Module(Func<Pair, string> procedure, string tickets, string[] names)
    {
        var module = InferProcedure.Infer(
            "blend.*",
            [("schema.sql", "CREATE SCHEMA blend;"), ("ticket.sql", tickets), .. Pairs.Select(procedure).Select((p, i) => ($"p{i}.sql", p))]);
        return module with
        {
            Objects = [.. module.Objects.Select(o => o.Id != Named ? o : o with { Results = [.. o.Results!.Select((r, i) => r with { Name = names[i] })] })],
        };
    }

    // The config file names the candidate's three sets; read as a baseline, it names the two the baseline returns by its first
    // two names (HaruspexConfig.AppliedToBaseline).
    private static IrDocument Baselines => Module(p => p.Baseline, Tickets.Baseline, ["head", "lines"]);

    private static IrDocument Candidates => Module(p => p.Candidate, Tickets.Candidate, ["head", "lines", "notes"]);

    private static IrCombined Combined(bool strict = false) => IrCombination.Of(Baselines, Candidates, new IrFitOptions { StrictObjects = strict });

    private static IReadOnlyList<string> Deployed(Func<Pair, string> procedure, string tickets, string rows) =>
        [Schema, tickets, rows, .. Pairs.Select(procedure)];

    private static IReadOnlyList<string> Baseline => Deployed(p => p.Baseline, Tickets.Baseline, TicketRows.Baseline);

    private static IReadOnlyList<string> Candidate => Deployed(p => p.Candidate, Tickets.Candidate, TicketRows.Candidate);

    private static List<GeneratedFunctions.Call> Calls(IrDocument document, IEnumerable<Pair> pairs) =>
        [.. pairs.Select(p => new GeneratedFunctions.Call(p.Name, MssqlEmitter.FunctionName(document.Objects.Single(o => o.Id == p.Id).Name), p.Params))];

    [Fact]
    public void Each_pair_has_the_findings_it_is_written_with_and_is_combined_exactly_when_it_is_bridged()
    {
        var combined = Combined();

        Assert.All(Pairs, p =>
        {
            Assert.Equal(p.Findings, combined.Findings.Where(f => f.Object == p.Id).Select(f => f.ToString()));
            Assert.Equal(p.Bridged, combined.Document.Objects.Any(o => o.Id == p.Id));
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Every_combined_call_returns_from_the_database_before_the_change_and_after_it(bool strict, bool after)
    {
        var combined = Combined(strict).Document;
        var bridged = Pairs.Where(p => p.Bridged).ToList();

        var responses = await GeneratedFunctions.RunAsync(server, combined, after ? Candidate : Baseline, Calls(combined, bridged), TestContext.Current.CancellationToken, strict);

        Assert.All(bridged.Zip(responses), pair =>
        {
            var (p, response) = pair;
            Assert.True(response.Error is null, $"{p.Name}, against the {(after ? "candidate" : "baseline")}: {response.Error}");
            if (p.Echoed is not null)
                Assert.Equal(p.Echoed, System.Text.Json.JsonSerializer.Serialize(response.Value?[0]));
        });
    }

    [Fact]
    public async Task A_change_that_can_t_be_bridged_fails_the_candidate_s_own_call_against_the_baseline()
    {
        var candidates = Candidates;
        var stuck = Pairs.Where(p => !p.Bridged).ToList();

        var responses = await GeneratedFunctions.RunAsync(server, candidates, Baseline, Calls(candidates, stuck), TestContext.Current.CancellationToken);

        Assert.All(stuck.Zip(responses), pair =>
        {
            var (p, response) = pair;
            Assert.True(response.Error is not null, $"{p.Name}: the candidate's own call returned from the baseline");
            Assert.Equal(p.Fails is null ? "HaruspexResultError" : "RequestError", response.Error.Name);
            Assert.Equal(p.Fails, response.Error.IsRequestError ? response.Error.Number : null);
        });
    }

    /// <summary>
    /// Query files, read against each database as a source baseline is read, with their text as it is now: one that reads what
    /// both have is combined and runs against both; one that reads a column only the candidate's table has is left out, and its
    /// own function fails against the baseline's database (207), which the unknown its reading there gave would have hidden.
    /// </summary>
    [Fact]
    public async Task A_query_file_runs_against_both_unless_it_reads_what_the_baseline_lacks()
    {
        const string Orders = "CREATE TABLE stall.orders (id int NOT NULL PRIMARY KEY, status varchar(10) NOT NULL{0});";
        var (before, after) = (string.Format(System.Globalization.CultureInfo.InvariantCulture, Orders, ""), string.Format(System.Globalization.CultureInfo.InvariantCulture, Orders, ", channel varchar(10) NULL"));
        (string Path, string Sql)[] queries =
        [
            ("sql/find_order.sql", "SELECT o.id, o.status FROM stall.orders AS o WHERE o.id = @id;"),
            ("sql/find_channel.sql", "SELECT o.id, o.channel FROM stall.orders AS o WHERE o.id = @id;"),
        ];
        var baseline = InferQuery.Module("CREATE SCHEMA stall;\nGO\n" + before, queries);
        var candidate = InferQuery.Module("CREATE SCHEMA stall;\nGO\n" + after, queries);

        var combined = IrCombination.Of(baseline, candidate, new IrFitOptions());

        var finding = Assert.Single(combined.Findings);
        Assert.StartsWith("unbridgeable: sql/find_channel.sql results[0][].channel: its text doesn't run against the baseline's database (", finding.ToString(), StringComparison.Ordinal);
        Assert.Equal(["sql/find_order.sql"], combined.Document.Objects.Select(o => o.Id));

        var token = TestContext.Current.CancellationToken;
        const string Schema = "IF SCHEMA_ID(N'stall') IS NULL EXEC (N'CREATE SCHEMA stall');";
        GeneratedFunctions.Call[] find = [new("find_order", "findOrder", new Dictionary<string, object?> { ["id"] = 1 })];
        foreach (var table in new[] { before, after })
        {
            var response = Assert.Single(await GeneratedFunctions.RunAsync(server, combined.Document, [Schema, table, "INSERT INTO stall.orders (id, status) VALUES (1, 'open');"], find, token));
            Assert.True(response.Error is null, $"{table}: {response.Error}");
        }

        var own = Assert.Single(await GeneratedFunctions.RunAsync(
            server, candidate, [Schema, before], [new GeneratedFunctions.Call("find_channel", "findChannel", new Dictionary<string, object?> { ["id"] = 1 })], token));
        Assert.Equal(207, own.Error?.Number);
    }
}

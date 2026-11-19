using System.Text.Json;
using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Table-valued parameters through the generated functions: each row the schema accepts is one the server takes, in the type's
/// order, and each it refuses is refused before anything is sent; what the schema can't see (a key shared) is the driver's own
/// error. Decision 0076 says why each is so. Each runs in a database of its own, whose types are created first and committed: a
/// type created in the transaction the calls run in deadlocks the call that sends it (observed, 1205).
/// </summary>
public partial class MssqlFunctionTests
{
    /// <summary>Runs <paramref name="calls"/> against <paramref name="procedures"/>, its table types <paramref name="types"/> created first and committed.</summary>
    private async Task<IReadOnlyList<GeneratedFunctions.Response>> RunWithTypes(string[] types, string[] procedures, params GeneratedFunctions.Call[] calls)
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await server.ScratchAsync();
        await database.ExecuteAsync("CREATE SCHEMA outlet;", token);
        foreach (var type in types)
            await database.ExecuteAsync(type, token);
        return await GeneratedFunctions.RunAsync(server, Module([.. types, .. procedures]), procedures, calls, token, committed: database);
    }

    /// <summary>JSON as written, a <c>+</c> left as it is.</summary>
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const string LineList = """
        CREATE TYPE outlet.line_list AS TABLE (
            product_id int NOT NULL,
            qty smallint NOT NULL CHECK (qty > 0),
            note nvarchar(20) NULL,
            PRIMARY KEY (product_id)
        );
        """;

    // The columns are read back in an order of their own, so a row sent in another order than the type's shows.
    private const string AddLines = """
        CREATE PROCEDURE outlet.add_lines @order_id int, @lines outlet.line_list READONLY
        AS
        SELECT @order_id AS order_id, l.note, l.qty, l.product_id FROM @lines AS l ORDER BY l.product_id;
        """;

    private static Dictionary<string, object?> TillLine(int product, int quantity, string? note) =>
        new() { ["product_id"] = product, ["quantity"] = quantity, ["note"] = note };

    private static Dictionary<string, object?> Line(int product, int qty, string? note) =>
        new() { ["product_id"] = product, ["qty"] = qty, ["note"] = note };

    [Fact]
    public async Task A_table_valued_parameter_is_sent_as_rows_of_its_type_in_the_type_s_order()
    {
        var responses = await RunWithTypes(
            [LineList],
            [AddLines],
            new GeneratedFunctions.Call("rows", "addLines", Params(("order_id", 7), ("lines", new[] { Line(2, 5, null), Line(1, 3, "gift") }))),
            new GeneratedFunctions.Call("empty", "addLines", Params(("order_id", 7), ("lines", Array.Empty<object>()))),
            new GeneratedFunctions.Call("left out", "addLines", Params(("order_id", 7))));

        Assert.All(responses, r => Assert.Null(r.Error));
        Assert.Equal(
            """[{"order_id":7,"note":"gift","qty":3,"product_id":1},{"order_id":7,"note":null,"qty":5,"product_id":2}]""",
            JsonSerializer.Serialize(responses[0].Value));
        Assert.Equal("[]", JsonSerializer.Serialize(responses[1].Value));
        Assert.Equal("[]", JsonSerializer.Serialize(responses[2].Value));
    }

    [Fact]
    public async Task A_row_the_type_refuses_is_refused_before_anything_is_sent_naming_the_row_and_column()
    {
        // Each would fail on the server: a column left out (500), NULL in a NOT NULL column (515), a CHECK broken (547), a longer
        // string (8037), a column the type lacks; and null for the parameter itself (206).
        var missing = new Dictionary<string, object?> { ["product_id"] = 1, ["qty"] = 1 };
        var extra = new Dictionary<string, object?>(Line(1, 1, null)) { ["colour"] = "red" };
        var responses = await RunWithTypes(
            [LineList],
            [AddLines],
            new GeneratedFunctions.Call("missing", "addLines", Params(("order_id", 1), ("lines", new[] { missing })), Db: "untouchable"),
            new GeneratedFunctions.Call("null qty", "addLines", Params(("order_id", 1), ("lines", new[] { new Dictionary<string, object?> { ["product_id"] = 1, ["qty"] = null, ["note"] = null } })), Db: "untouchable"),
            new GeneratedFunctions.Call("qty 0", "addLines", Params(("order_id", 1), ("lines", new[] { Line(1, 0, null) })), Db: "untouchable"),
            new GeneratedFunctions.Call("long note", "addLines", Params(("order_id", 1), ("lines", new[] { Line(1, 1, new string('x', 21)) })), Db: "untouchable"),
            new GeneratedFunctions.Call("extra", "addLines", Params(("order_id", 1), ("lines", new[] { extra })), Db: "untouchable"),
            new GeneratedFunctions.Call("null", "addLines", Params(("order_id", 1), ("lines", null)), Db: "untouchable"));

        Assert.All(responses, r => Assert.Equal("HaruspexParameterError", r.Error?.Name));
        Assert.Equal(
            ["lines.0.note", "lines.0.qty", "lines.0.qty", "lines.0.note", "lines.0", "lines"],
            responses.Select(r => string.Join(".", Assert.Single(r.Error!.Issues).Path)));
    }

    [Fact]
    public async Task A_row_that_shares_a_key_with_another_is_the_driver_s_error_carrying_the_server_s()
    {
        // The schema checks each row alone; the key is the server's to enforce, and its error passes through as it is.
        var error = Assert.Single(await RunWithTypes(
            [LineList],
            [AddLines],
            new GeneratedFunctions.Call("twice", "addLines", Params(("order_id", 1), ("lines", new[] { Line(1, 1, null), Line(1, 2, null) }))))).Error!;

        // Observed: the driver's error itself has no number and no message; the server's, the key's 2627 and then 8061 (the table
        // doesn't conform to its type), come before it.
        Assert.True(error.IsRequestError, error.ToString());
        Assert.Equal((null, ""), (error.Number, error.Message));
        Assert.Equal([2627, 8061], error.Preceding);
    }

    [Fact]
    public async Task A_table_type_whose_names_need_quoting_and_a_column_of_each_kind_are_sent_as_they_are()
    {
        const string Readings = """
            CREATE TYPE outlet.[reading list] AS TABLE (
                [taken on] date NOT NULL,
                [select] datetime2(0) NULL,
                amount decimal(5,2) NULL,
                token uniqueidentifier NULL,
                flagged bit NULL,
                total bigint NULL,
                code char(3) NULL
            );
            """;
        const string Echo = """
            CREATE PROCEDURE outlet.echo_readings @readings outlet.[reading list] READONLY
            AS
            SELECT CONVERT(varchar(30), r.[taken on], 126) AS taken_on, CONVERT(varchar(30), r.[select], 126) AS taken_at,
                r.amount, r.token, r.flagged, CAST(r.total AS varchar(20)) AS total, r.code
            FROM @readings AS r;
            """;
        var row = new Dictionary<string, object?>
        {
            ["taken on"] = "2026-11-19",
            ["select"] = "2026-11-19T10:20:30",
            ["amount"] = 123.45,
            ["token"] = "0f8fad5b-d9cb-469f-a165-70867728950e",
            ["flagged"] = true,
            ["total"] = "9007199254740993",
            ["code"] = "abc",
        };

        var response = Assert.Single(await RunWithTypes([Readings], [Echo], new GeneratedFunctions.Call("row", "echoReadings", Params(("readings", new[] { row })))));

        Assert.Null(response.Error);
        Assert.Equal(
            """[{"taken_on":"2026-11-19","taken_at":"2026-11-19T10:20:30","amount":123.45,"token":"0F8FAD5B-D9CB-469F-A165-70867728950E","flagged":true,"total":"9007199254740993","code":"abc"}]""",
            JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task The_till_fixture_s_table_valued_parameters_return_what_their_schemas_accept()
    {
        // The fixture is deployed with its types, so its module is called as it is. Each function checks what comes back with its
        // schema, so a call that returns proves the IR's claims of those rows: the key that makes grade_of one row at most, the
        // value set its CHECK closes, the columns of each type as declared.
        static Dictionary<string, object?> Tag(string tag, int grade) => new() { ["tag"] = tag, ["grade"] = grade };
        var reading = new Dictionary<string, object?>
        {
            ["taken_on"] = "2026-11-19",
            ["taken_at"] = "2026-11-19T10:20:30",
            ["amount"] = 1.25,
            ["token"] = null,
            ["flagged"] = false,
            ["total"] = new MssqlDriver.BigIntValue("9007199254740993"),
            ["code"] = "abc",
        };
        var responses = await GeneratedFunctions.RunAsync(
            server,
            Fixture.Named("till").Ir,
            [],
            [
                new GeneratedFunctions.Call("add_lines", "addLines", Params(("lines", new[] { TillLine(3, 1, null), TillLine(1, 2, "loose") }))),
                new GeneratedFunctions.Call("grade_of", "gradeOf", Params(("tags", new[] { Tag("tea", 2), Tag("cup", 3) }), ("tag", "tea"))),
                new GeneratedFunctions.Call("grade_of, none", "gradeOf", Params(("tag", "tea"))),
                new GeneratedFunctions.Call("echo_readings", "echoReadings", Params(("readings", new[] { reading }))),
                new GeneratedFunctions.Call("echo_echoes", "echoEchoes", Params(
                    ("echoes", new[] { new Dictionary<string, object?> { ["k"] = 1, ["v"] = 1 }, new Dictionary<string, object?> { ["k"] = 1, ["v"] = 2 } }),
                    ("odd", new[] { new Dictionary<string, object?> { ["first col"] = 1, ["select"] = "x" } }))),
                new GeneratedFunctions.Call("count_serials", "countSerials"),
            ],
            TestContext.Current.CancellationToken);

        Assert.All(responses, r => Assert.True(r.Error is null, $"{r.Name}: {r.Error}"));
        Assert.Equal(
            [
                """[{"product_id":1,"quantity":2,"note":"loose","price":4.5},{"product_id":3,"quantity":1,"note":null,"price":6}]""",
                """[{"grade":2}]""",
                "[]",
            ],
            responses.Take(3).Select(r => JsonSerializer.Serialize(r.Value)));
        Assert.Equal("""{"result1":[{"k":1,"v":1}],"result2":[{"first col":1,"select":"x"}]}""", JsonSerializer.Serialize(responses[4].Value));
        Assert.Equal("""[{"serials":0}]""", JsonSerializer.Serialize(responses[5].Value));
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Tokyo")]
    public async Task A_table_type_s_dates_and_times_arrive_as_sent_whatever_the_process_s_time_zone(string zone)
    {
        // Declared to the driver as one of its date types, a value would be read in the process's time zone and arrive shifted
        // (observed: 10:20:30 as 15:20:30 under America/New_York); as text the server converts it exactly (decision 0055).
        const string Times = """
            CREATE TYPE outlet.time_list AS TABLE (
                d date NOT NULL, t time(3) NOT NULL, dt datetime2(0) NOT NULL, old datetime NOT NULL, small smalldatetime NOT NULL,
                stamped datetimeoffset(0) NOT NULL);
            """;
        const string Echo = """
            CREATE PROCEDURE outlet.echo_times @times outlet.time_list READONLY AS
            SELECT CONVERT(varchar(40), d, 126) AS d, CONVERT(varchar(40), t, 126) AS t, CONVERT(varchar(40), dt, 126) AS dt,
                CONVERT(varchar(40), old, 126) AS old, CONVERT(varchar(40), small, 126) AS small, CONVERT(varchar(40), stamped, 126) AS stamped
            FROM @times;
            """;
        var row = new Dictionary<string, object?>
        {
            ["d"] = "2026-11-19",
            ["t"] = "10:20:30.125",
            ["dt"] = "2026-11-19T10:20:30",
            ["old"] = "2026-11-19T10:20:30.997",
            ["small"] = "2026-11-19T10:20:00",
            ["stamped"] = "2026-11-19T10:20:30+05:30",
        };

        var response = Assert.Single(await RunWithTypes([Times], [Echo], new GeneratedFunctions.Call(zone, "echoTimes", Params(("times", new[] { row })), TimeZone: zone)));

        Assert.Null(response.Error);
        Assert.Equal(
            """[{"d":"2026-11-19","t":"10:20:30.125","dt":"2026-11-19T10:20:30","old":"2026-11-19T10:20:30.997","small":"2026-11-19T10:20:00","stamped":"2026-11-19T10:20:30+05:30"}]""",
            JsonSerializer.Serialize(response.Value, Relaxed));
    }

    [Fact]
    public async Task A_row_s_datetime_arrives_as_sent_and_a_fraction_the_server_would_round_is_refused_before_sending()
    {
        // A_datetime_column_rounds_a_row_s_thousandths_to_0_3_or_7_without_an_error: the schema takes a fraction of one or two
        // digits, or of three ending in 0, 3 or 7, which the server keeps; any other would be moved without an error.
        const string Stamps = "CREATE TYPE outlet.stamp_list AS TABLE (k int NOT NULL, at datetime NOT NULL);";
        const string Echo = "CREATE PROCEDURE outlet.echo_stamps @stamps outlet.stamp_list READONLY AS SELECT CONVERT(varchar(30), at, 126) AS at FROM @stamps ORDER BY k;";
        static Dictionary<string, object?> Stamp(int k, string fraction) => new() { ["k"] = k, ["at"] = "2026-11-19T10:20:30" + fraction };
        string[] kept = ["", ".1", ".12", ".120", ".123", ".127", ".997"];
        string[] moved = [".001", ".002", ".005", ".998", ".999"];

        var responses = await RunWithTypes(
            [Stamps],
            [Echo],
            [
                new GeneratedFunctions.Call("kept", "echoStamps", Params(("stamps", kept.Select((f, i) => Stamp(i, f)).ToArray()))),
                .. moved.Select(f => new GeneratedFunctions.Call(f, "echoStamps", Params(("stamps", new[] { Stamp(0, f) })), Db: "untouchable")),
            ]);

        Assert.Null(responses[0].Error);
        Assert.Equal(
            """[{"at":"2026-11-19T10:20:30"},{"at":"2026-11-19T10:20:30.100"},{"at":"2026-11-19T10:20:30.120"},{"at":"2026-11-19T10:20:30.120"},{"at":"2026-11-19T10:20:30.123"},{"at":"2026-11-19T10:20:30.127"},{"at":"2026-11-19T10:20:30.997"}]""",
            JsonSerializer.Serialize(responses[0].Value));
        Assert.All(responses.Skip(1), r => Assert.Equal("stamps.0.at", string.Join(".", Assert.Single(r.Error!.Issues).Path)));
    }

    [Fact]
    public async Task A_call_without_a_time_zone_runs_in_the_machine_s_before_and_after_one_with_a_zone()
    {
        // The harness, not haruspex: it once set TZ to the string "undefined" when it was unset, which Node reads as UTC, so the
        // tests of a call without a zone ran in UTC wherever the machine was.
        const string Ping = "CREATE PROCEDURE outlet.ping AS SELECT 1 AS one;";
        var machine = (int)TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;

        var responses = await RunWithTypes(
            [],
            [Ping],
            new GeneratedFunctions.Call("before", "ping"),
            new GeneratedFunctions.Call("Tokyo", "ping", TimeZone: "Asia/Tokyo"),
            new GeneratedFunctions.Call("after", "ping"));

        Assert.Equal([machine, 540, machine], responses.Select(r => r.MinutesEast));
    }

    [Fact]
    public async Task A_bigint_past_2_to_the_53_in_a_row_arrives_exact()
    {
        // The row's bigint is a JavaScript bigint, which the function sends as the string of its digits.
        const string Totals = "CREATE TYPE outlet.total_list AS TABLE (total bigint NOT NULL);";
        const string Echo = "CREATE PROCEDURE outlet.echo_totals @totals outlet.total_list READONLY AS SELECT CAST(total AS varchar(20)) AS total FROM @totals;";
        var row = new Dictionary<string, object?> { ["total"] = new MssqlDriver.BigIntValue("9223372036854775807") };

        var response = Assert.Single(await RunWithTypes([Totals], [Echo], new GeneratedFunctions.Call("max", "echoTotals", Params(("totals", new[] { row })))));

        Assert.Null(response.Error);
        Assert.Equal("""[{"total":"9223372036854775807"}]""", JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task A_table_type_no_row_can_be_sent_in_is_a_parameter_only_left_out()
    {
        const string Serials = "CREATE TYPE outlet.serial_list AS TABLE (serial int IDENTITY PRIMARY KEY, label nvarchar(20) NOT NULL);";
        const string Count = "CREATE PROCEDURE outlet.count_serials @serials outlet.serial_list READONLY AS SELECT count(*) AS n FROM @serials;";

        var response = Assert.Single(await RunWithTypes([Serials], [Count], new GeneratedFunctions.Call("left out", "countSerials")));

        Assert.Null(response.Error);
        Assert.Equal("""[{"n":0}]""", JsonSerializer.Serialize(response.Value));
    }
}

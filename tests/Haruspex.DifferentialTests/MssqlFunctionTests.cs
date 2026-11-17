using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The generated functions on the server, through the mssql driver: what they send, what they return, and the three kinds of
/// failure a caller tells apart (decision 0056). Each procedure is invented here and created in the run's own transaction.
/// </summary>
public partial class MssqlFunctionTests(SqlServerFixture server)
{
    private const string Schema = "IF SCHEMA_ID(N'outlet') IS NULL EXEC (N'CREATE SCHEMA outlet');";

    /// <summary>The database module of <paramref name="procedures"/>, each a <c>CREATE PROCEDURE</c> in schema <c>outlet</c>, which no fixture uses.</summary>
    private static IrDocument Module(params string[] procedures) =>
        InferProcedure.Infer("outlet.*", [("schema.sql", "CREATE SCHEMA outlet;"), .. procedures.Select((p, i) => ($"p{i}.sql", p))]);

    /// <summary>Runs <paramref name="calls"/> against <paramref name="procedures"/> as deployed, whose module is <paramref name="module"/>.</summary>
    private async Task<IReadOnlyList<GeneratedFunctions.Response>> Run(IrDocument module, string[] procedures, params GeneratedFunctions.Call[] calls) =>
        await GeneratedFunctions.RunAsync(server, module, [Schema, .. procedures], calls, TestContext.Current.CancellationToken);

    /// <summary>As <see cref="Run(IrDocument, string[], GeneratedFunctions.Call[])"/>, with the module written as <c>"objects": "strict"</c> writes it when <paramref name="strict"/>.</summary>
    private async Task<IReadOnlyList<GeneratedFunctions.Response>> Run(bool strict, IrDocument module, string[] procedures, params GeneratedFunctions.Call[] calls) =>
        await GeneratedFunctions.RunAsync(server, module, [Schema, .. procedures], calls, TestContext.Current.CancellationToken, strict);

    private static Dictionary<string, object?> Params(params (string Key, object? Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    private const string GetOrder = """
        CREATE PROCEDURE outlet.get_order @order_id int, @note nvarchar(10) = N'none', @total int = NULL OUTPUT, @lines int OUTPUT
        AS
        SET @total = @order_id * 2;
        SET @lines = 3;
        SELECT @order_id AS order_id, @note AS note;
        RETURN 7;
        """;

    [Fact]
    public async Task A_function_returns_its_result_its_output_parameters_and_its_return_code()
    {
        var response = Assert.Single(await Run(Module(GetOrder), [GetOrder], new GeneratedFunctions.Call("call", "getOrder", Params(("order_id", 21), ("total", null)))));

        Assert.Null(response.Error);
        Assert.Equal(
            """{"result":[{"order_id":21,"note":"none"}],"output":{"total":42,"lines":3},"returnValue":7}""",
            JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task A_parameter_left_out_keeps_its_default_and_an_output_one_left_out_does_not_come_back()
    {
        // Bound as undefined, the driver would send NULL and override the default; so a key left out is not bound.
        var response = Assert.Single(await Run(Module(GetOrder), [GetOrder], new GeneratedFunctions.Call("call", "getOrder", Params(("order_id", 1)))));

        Assert.Null(response.Error);
        Assert.Equal("none", response.Value!.Value.GetProperty("result")[0].GetProperty("note").GetString());
        Assert.False(response.Value!.Value.GetProperty("output").TryGetProperty("total", out _));
    }

    [Fact]
    public async Task Wrong_parameters_are_refused_before_the_connection_is_touched_naming_the_procedure_and_each_parameter()
    {
        var response = Assert.Single(await Run(
            Module(GetOrder),
            [GetOrder],
            new GeneratedFunctions.Call("wrong", "getOrder", Params(("order_id", "21"), ("note", "far too long a note"), ("colour", "red")), Db: "untouchable")));

        var error = response.Error!;
        Assert.Equal(("HaruspexParameterError", "parameters", "outlet.get_order"), (error.Name, error.Kind, error.Procedure));
        Assert.Equal(["", "note", "order_id"], error.Issues.Select(i => string.Join(".", i.Path)).Order(StringComparer.Ordinal));
        Assert.StartsWith("outlet.get_order: @", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_database_s_error_is_the_driver_s_own_unwrapped_even_after_a_result_set()
    {
        const string Fails = """
            CREATE PROCEDURE outlet.fail_late @n int = 0 OUTPUT
            AS
            SET @n = 1;
            SELECT 1 AS a;
            THROW 50001, N'the order is closed', 1;
            """;

        var error = Assert.Single(await Run(Module(Fails), [Fails], new GeneratedFunctions.Call("call", "failLate"))).Error!;

        Assert.True(error.IsRequestError, error.ToString());
        Assert.Equal(("RequestError", 50001, "outlet.fail_late"), (error.Name, error.Number, error.ProcName));
    }

    [Fact]
    public async Task A_raiserror_that_lets_the_procedure_go_on_still_fails_the_call_with_no_results()
    {
        const string Raises = """
            CREATE PROCEDURE outlet.raise_mid
            AS
            SELECT 1 AS a;
            RAISERROR(N'stock is low', 16, 1);
            SELECT 2 AS b;
            RETURN 9;
            """;

        var error = Assert.Single(await Run(Module(Raises), [Raises], new GeneratedFunctions.Call("call", "raiseMid"))).Error!;

        Assert.True(error.IsRequestError);
        Assert.Equal(50000, error.Number);
    }

    [Fact]
    public async Task A_result_that_breaks_its_schema_is_a_result_error_naming_the_procedure_the_part_and_the_path()
    {
        // The module is generated from one definition and the database runs another: deployed out of step.
        const string Generated = "CREATE PROCEDURE outlet.order_count AS SELECT COUNT_BIG(*) AS n, CAST(1 AS int) AS one;";
        const string Deployed = "CREATE PROCEDURE outlet.order_count AS SELECT COUNT_BIG(*) AS n, CAST(NULL AS int) AS one;";

        var error = Assert.Single(await Run(Module(Generated), [Deployed], new GeneratedFunctions.Call("call", "orderCount"))).Error!;

        Assert.Equal(("HaruspexResultError", "result", "outlet.order_count", "the results"), (error.Name, error.Kind, error.Procedure, error.Part));
        Assert.Equal("0.one", string.Join(".", Assert.Single(error.Issues).Path));
        Assert.StartsWith("outlet.order_count, the results: [0].one: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_in_one_of_several_result_sets_names_the_set_and_a_row_count_in_words()
    {
        // The second set is one row (an aggregate without GROUP BY); the deployed procedure returns it with none, or returns a
        // null the first set's column can't hold. The message names the set as a person counts them, and the count in rows.
        const string Generated = "CREATE PROCEDURE outlet.order_sheet @fail int AS SELECT CAST(1 AS int) AS a; SELECT COUNT_BIG(*) AS n;";
        const string Deployed = """
            CREATE PROCEDURE outlet.order_sheet @fail int AS
            IF @fail = 1 SELECT CAST(1 AS int) AS a;
            IF @fail = 1 SELECT CAST(1 AS bigint) AS n WHERE 1 = 0;
            IF @fail = 2 SELECT CAST(NULL AS int) AS a;
            IF @fail = 2 SELECT COUNT_BIG(*) AS n;
            """;

        var responses = await Run(
            Module(Generated),
            [Deployed],
            new GeneratedFunctions.Call("missing", "orderSheet", Params(("fail", 1))),
            new GeneratedFunctions.Call("null", "orderSheet", Params(("fail", 2))));

        Assert.Equal("outlet.order_sheet, the results: result set 2: Expected exactly one row", responses[0].Error!.Message);
        Assert.StartsWith("outlet.order_sheet, the results: result set 1, [0].a: ", responses[1].Error!.Message, StringComparison.Ordinal);
        Assert.Equal(["0", "0", "a"], Assert.Single(responses[1].Error!.Issues).Path);
    }

    [Theory]
    [InlineData(false, "two_sets", """{"result1":[{"a":1}],"result2":[{"b":"x"}]}""")]
    [InlineData(true, "two_sets", "outlet.two_sets, the results: Expected exactly 2 result sets, received 3")]
    [InlineData(false, "one_set", """[{"a":1}]""")]
    [InlineData(true, "one_set", "outlet.one_set, the results: Expected exactly one result set, received 2")]
    [InlineData(false, "no_set", "null")]
    [InlineData(true, "no_set", "outlet.no_set, the results: Expected no result sets, received 1")]
    public async Task A_result_set_after_those_a_function_knows_is_ignored_unless_objects_are_strict(bool strict, string procedure, string expected)
    {
        // The database gained a trailing result set the deployed module doesn't know. By decision 0008's reasoning a deployed
        // application survives it, as it survives an added key, unless "objects": "strict" asks to refuse both (decision 0063).
        var generated = new Dictionary<string, string>
        {
            ["two_sets"] = "CREATE PROCEDURE outlet.two_sets AS SELECT CAST(1 AS int) AS a; SELECT N'x' AS b;",
            ["one_set"] = "CREATE PROCEDURE outlet.one_set AS SELECT CAST(1 AS int) AS a;",
            ["no_set"] = "CREATE PROCEDURE outlet.no_set AS SET NOCOUNT ON;",
        };
        var deployed = new Dictionary<string, string>
        {
            ["two_sets"] = "CREATE PROCEDURE outlet.two_sets AS SELECT CAST(1 AS int) AS a; SELECT N'x' AS b; SELECT 2 AS c;",
            ["one_set"] = "CREATE PROCEDURE outlet.one_set AS SELECT CAST(1 AS int) AS a; SELECT 2 AS c;",
            ["no_set"] = "CREATE PROCEDURE outlet.no_set AS SELECT 2 AS c;",
        };
        var function = procedure switch { "two_sets" => "twoSets", "one_set" => "oneSet", _ => "noSet" };

        var response = Assert.Single(await Run(strict, Module(generated[procedure]), [deployed[procedure]], new GeneratedFunctions.Call("call", function)));

        Assert.Equal(expected, strict ? response.Error!.Message : JsonSerializer.Serialize(response.Value));
        if (strict)
            Assert.Equal(("HaruspexResultError", "the results"), (response.Error!.Name, response.Error.Part));
    }

    [Theory]
    [InlineData(false, "outlet.two_sets, the results: Expected at least 2 result sets, received 1")]
    [InlineData(true, "outlet.two_sets, the results: Expected exactly 2 result sets, received 1")]
    public async Task A_result_set_every_call_returns_that_is_missing_fails_under_either_setting(bool strict, string expected)
    {
        // Read as [] it would pass the schema of a set of any number of rows: a set the IR says always arrives must arrive.
        const string Generated = "CREATE PROCEDURE outlet.two_sets AS SELECT CAST(1 AS int) AS a; SELECT N'x' AS b;";
        const string Deployed = "CREATE PROCEDURE outlet.two_sets AS SELECT CAST(1 AS int) AS a;";

        var response = Assert.Single(await Run(strict, Module(Generated), [Deployed], new GeneratedFunctions.Call("call", "twoSets")));

        Assert.Equal(expected, response.Error!.Message);
    }

    [Theory]
    [InlineData(false, "outlet.json_then_rows, the results: Expected at least 2 result sets, received 1")]
    [InlineData(true, "outlet.json_then_rows, the results: Expected exactly 2 result sets, received 1")]
    public async Task A_missing_set_after_a_for_json_set_that_matched_no_rows_fails(bool strict, string expected)
    {
        // FOR JSON over no rows still sends a set, so the rowset after it always arrives: dropped, the call fails.
        const string Generated = "CREATE PROCEDURE outlet.json_then_rows AS SELECT v.id FROM (VALUES (1)) AS v(id) WHERE v.id > 1 FOR JSON PATH; SELECT N'x' AS b;";
        const string Deployed = "CREATE PROCEDURE outlet.json_then_rows AS SELECT v.id FROM (VALUES (1)) AS v(id) WHERE v.id > 1 FOR JSON PATH;";

        var response = Assert.Single(await Run(strict, Module(Generated), [Deployed], new GeneratedFunctions.Call("call", "jsonThenRows")));

        Assert.Equal(expected, response.Error!.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_for_json_result_reads_the_same_whether_the_pool_parses_json_or_not(bool parseJson)
    {
        const string Json = """
            CREATE PROCEDURE outlet.order_json @order_id int
            AS
            SELECT v.id, v.name FROM (VALUES (1, N'tea'), (2, N'cup')) AS v(id, name) WHERE v.id >= @order_id FOR JSON PATH;
            """;

        var responses = await Run(
            Module(Json),
            [Json],
            new GeneratedFunctions.Call("found", "orderJson", Params(("order_id", 1)), ParseJson: parseJson),
            new GeneratedFunctions.Call("none", "orderJson", Params(("order_id", 9)), ParseJson: parseJson));

        Assert.Equal("""[{"id":1,"name":"tea"},{"id":2,"name":"cup"}]""", JsonSerializer.Serialize(responses[0].Value));
        Assert.Equal("null", JsonSerializer.Serialize(responses[1].Value));
    }

    [Fact]
    public async Task A_function_returns_its_one_result_itself_and_names_its_parts_when_a_code_may_come_back()
    {
        // Decision 0060: one result set and nothing else is the result; a RETURN of another value than 0 adds the code beside it;
        // nothing to hand back is nothing.
        const string Plain = "CREATE PROCEDURE outlet.plain AS SELECT 1 AS one; RETURN 0;";
        const string Coded = "CREATE PROCEDURE outlet.coded @fail bit AS SELECT 1 AS one; IF @fail = 1 RETURN 3;";
        const string Quiet = "CREATE PROCEDURE outlet.quiet AS SET NOCOUNT ON;";

        var responses = await Run(
            Module(Plain, Coded, Quiet),
            [Plain, Coded, Quiet],
            new GeneratedFunctions.Call("plain", "plain"),
            new GeneratedFunctions.Call("coded", "coded", Params(("fail", true))),
            new GeneratedFunctions.Call("uncoded", "coded", Params(("fail", false))),
            new GeneratedFunctions.Call("quiet", "quiet"));

        Assert.Equal(
            ["""[{"one":1}]""", """{"result":[{"one":1}],"returnValue":3}""", """{"result":[{"one":1}],"returnValue":0}""", "null"],
            responses.Select(r => JsonSerializer.Serialize(r.Value)));
    }

    [Fact]
    public async Task Several_result_sets_come_back_named_in_order()
    {
        const string Two = "CREATE PROCEDURE outlet.two_sets AS SELECT 1 AS a; SELECT N'x' AS b;";

        var response = Assert.Single(await Run(Module(Two), [Two], new GeneratedFunctions.Call("call", "twoSets")));

        Assert.Equal("""{"result1":[{"a":1}],"result2":[{"b":"x"}]}""", JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task A_bigint_and_a_date_are_sent_as_the_schema_takes_them_and_kept()
    {
        const string Echo = "CREATE PROCEDURE outlet.echo @id bigint, @at datetime2(3) AS SELECT @id AS id, CONVERT(nvarchar(30), @at, 126) AS at;";

        var response = Assert.Single(await Run(
            Module(Echo),
            [Echo],
            new GeneratedFunctions.Call("call", "echo", Params(("id", new MssqlDriver.BigIntValue("9223372036854775807")), ("at", "2026-11-16T08:30:00.125")))));

        Assert.Equal("""[{"id":"9223372036854775807","at":"2026-11-16T08:30:00.125"}]""", JsonSerializer.Serialize(response.Value));
    }
}

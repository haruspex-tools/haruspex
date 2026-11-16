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

        Assert.Equal("""[{"id":1,"name":"tea"},{"id":2,"name":"cup"}]""", JsonSerializer.Serialize(responses[0].Value!.Value.GetProperty("result")));
        Assert.Equal(JsonValueKind.Null, responses[1].Value!.Value.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Several_result_sets_come_back_named_in_order()
    {
        const string Two = "CREATE PROCEDURE outlet.two_sets AS SELECT 1 AS a; SELECT N'x' AS b;";

        var response = Assert.Single(await Run(Module(Two), [Two], new GeneratedFunctions.Call("call", "twoSets")));

        Assert.Equal("""{"result1":[{"a":1}],"result2":[{"b":"x"}],"returnValue":0}""", JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task A_bigint_and_a_date_are_sent_as_the_schema_takes_them_and_kept()
    {
        const string Echo = "CREATE PROCEDURE outlet.echo @id bigint, @at datetime2(3) AS SELECT @id AS id, CONVERT(nvarchar(30), @at, 126) AS at;";

        var response = Assert.Single(await Run(
            Module(Echo),
            [Echo],
            new GeneratedFunctions.Call("call", "echo", Params(("id", new MssqlDriver.BigIntValue("9223372036854775807")), ("at", "2026-11-16T08:30:00.125")))));

        Assert.Equal("""[{"id":"9223372036854775807","at":"2026-11-16T08:30:00.125"}]""", JsonSerializer.Serialize(response.Value!.Value.GetProperty("result")));
    }
}

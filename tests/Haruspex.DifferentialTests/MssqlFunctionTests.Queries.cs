using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A query file's function on the server: its text sent through <c>sp_executesql</c> with each parameter declared by the type
/// inferred for it, with the procedures' checks, reads and failures. The stock table is invented here and created in the run's
/// transaction, in the schema <c>outlet</c>, which no fixture uses.
/// </summary>
public partial class MssqlFunctionTests
{
    private const string Stock = """
        CREATE TABLE outlet.stock (sku char(4) NOT NULL PRIMARY KEY, qty int NOT NULL, note nvarchar(20) NULL);
        """;

    private const string Stocked = "INSERT outlet.stock (sku, qty, note) VALUES ('AB12', 3, N'shelf'), ('CD34', 0, NULL);";

    private static IrDocument Queries(params (string Path, string Sql)[] queries) => InferQuery.Module("CREATE SCHEMA outlet;\nGO\n" + Stock, queries);

    private async Task<IReadOnlyList<GeneratedFunctions.Response>> RunQueries(IrDocument module, params GeneratedFunctions.Call[] calls) =>
        await GeneratedFunctions.RunAsync(server, module, [Schema, Stock, Stocked], calls, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_query_file_s_function_runs_its_text_with_its_parameters_and_returns_its_rows()
    {
        var module = Queries(("sql/stock_of.sql", "SELECT s.sku, s.qty FROM outlet.stock AS s WHERE s.sku = @sku OR @sku IS NULL ORDER BY s.sku;"));

        var responses = await RunQueries(
            module,
            new GeneratedFunctions.Call("one", "stockOf", Params(("sku", "AB12"))),
            new GeneratedFunctions.Call("every", "stockOf", Params(("sku", null))));

        Assert.Equal("char(4)", Assert.IsType<IrScalar>(Assert.Single(module.Objects).Parameters[0].Type).Sql);
        Assert.Equal("""[{"sku":"AB12","qty":3}]""", JsonSerializer.Serialize(responses[0].Value));
        Assert.Equal("""[{"sku":"AB12","qty":3},{"sku":"CD34","qty":0}]""", JsonSerializer.Serialize(responses[1].Value));
    }

    [Fact]
    public async Task A_query_file_s_function_sends_a_form_feed_nel_and_the_unicode_separators_as_the_file_holds_them()
    {
        // The reader once turned each into LF, so the function sent another literal than the file's; the server keeps each
        // (QueryTextObservationTests).
        var module = Queries(("sql/marks.sql", "SELECT UNICODE(SUBSTRING(N'a\f\u0085\u2028\u2029', 2, 1)) AS ff, UNICODE(SUBSTRING(N'a\f\u0085\u2028\u2029', 3, 1)) AS nel,\r\n"
            + "  UNICODE(SUBSTRING(N'a\f\u0085\u2028\u2029', 4, 1)) AS ls, UNICODE(SUBSTRING(N'a\f\u0085\u2028\u2029', 5, 1)) AS ps;"));

        var response = Assert.Single(await RunQueries(module, new GeneratedFunctions.Call("call", "marks")));

        Assert.Null(response.Error);
        Assert.Equal("""[{"ff":12,"nel":133,"ls":8232,"ps":8233}]""", JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task A_parameter_sent_as_text_is_declared_as_its_own_type_so_the_query_sees_that_type()
    {
        // A date is sent as an ISO 8601 string (decision 0055); declared date, the server converts it, so it holds 3 bytes, not 10.
        var module = Queries(("sql/date_bytes.sql", "-- @param @since date\nSELECT DATALENGTH(@since) AS bytes, DATEADD(day, 1, @since) AS next_day;"));

        var response = Assert.Single(await RunQueries(module, new GeneratedFunctions.Call("call", "dateBytes", Params(("since", "2026-11-16")))));

        Assert.Null(response.Error);
        Assert.Equal(3, response.Value!.Value[0].GetProperty("bytes").GetInt32());
    }

    [Fact]
    public async Task A_query_file_of_several_statements_writes_a_temporary_table_and_returns_its_rows()
    {
        var module = Queries(("sql/restock.sql", """
            CREATE TABLE #low (sku char(4) NOT NULL);
            INSERT #low (sku) SELECT s.sku FROM outlet.stock AS s WHERE s.qty <= @under;
            UPDATE outlet.stock SET qty = @restocked WHERE sku IN (SELECT l.sku FROM #low AS l);
            SELECT s.sku, s.qty FROM outlet.stock AS s JOIN #low AS l ON l.sku = s.sku;
            """));

        var response = Assert.Single(await RunQueries(module, new GeneratedFunctions.Call("call", "restock", Params(("under", 1), ("restocked", 10)))));

        Assert.Null(response.Error);
        Assert.Equal(["@under int", "@restocked int"], module.Parameters());
        Assert.Equal("""[{"sku":"CD34","qty":10}]""", JsonSerializer.Serialize(response.Value));
    }

    [Fact]
    public async Task A_query_file_can_not_return_a_value_so_it_hands_back_no_return_code()
    {
        // A batch run through sp_executesql may RETURN, but not with a value (error 178), so the code is always 0; the reader
        // refuses a file with one (HAR1017), so the server's refusal is observed through the driver directly.
        var module = Queries(("sql/stop.sql", "SELECT 1 AS one; RETURN;"));
        var response = Assert.Single(await RunQueries(module, new GeneratedFunctions.Call("bare", "stop")));
        var valued = Assert.Single(await MssqlDriver.RunAsync(
            server,
            null,
            [],
            [new("valued", ParseJson: false, Procedure: "sys.sp_executesql", Parameters: new Dictionary<string, object?> { ["stmt"] = new MssqlDriver.Typed("NVarChar", "SELECT 1 AS one; RETURN 5;", ["MAX"]) })],
            TestContext.Current.CancellationToken));

        Assert.Equal("""[{"one":1}]""", JsonSerializer.Serialize(response.Value));
        Assert.Equal(178, valued.Error!.Number);
    }

    [Fact]
    public async Task Wrong_parameters_to_a_query_file_are_refused_before_the_connection_is_touched_naming_the_file()
    {
        var module = Queries(("sql/stock_of.sql", "SELECT s.qty FROM outlet.stock AS s WHERE s.sku = @sku;"));

        var error = Assert.Single(await RunQueries(module, new GeneratedFunctions.Call("wrong", "stockOf", Params(("sku", "TOO-LONG")), Db: "untouchable"))).Error!;

        Assert.Equal(("HaruspexParameterError", "parameters", "sql/stock_of.sql"), (error.Name, error.Kind, error.Procedure));
        Assert.Equal("sku", string.Join(".", Assert.Single(error.Issues).Path));
    }

    [Fact]
    public async Task The_database_s_error_in_a_query_file_is_the_driver_s_own()
    {
        var module = Queries(("sql/per_unit.sql", "SELECT s.sku, s.qty / @units AS each FROM outlet.stock AS s WHERE s.qty > @units;"));

        var error = Assert.Single(await RunQueries(module, new GeneratedFunctions.Call("call", "perUnit", Params(("units", 0))))).Error!;

        Assert.True(error.IsRequestError, error.ToString());
        Assert.Equal(8134, error.Number);
    }
}

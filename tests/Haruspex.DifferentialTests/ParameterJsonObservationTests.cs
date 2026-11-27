using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What a procedure's JSON reads of a parameter give for what a caller may send, which says what a declaration of the parameter's
/// JSON can claim (docs/decisions/0143-a-parameter-s-json-may-be-declared.md). A caller may send anything: text that isn't JSON
/// fails the call (13609, in lax mode too), so the IR, which describes calls that succeed, need not allow for it; JSON of another
/// shape than the declared one gives NULL under a lax path, which is the assumption a declaration makes, as a column's does; and
/// NULL gives NULL. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class ParameterJsonObservationTests(SqlServerFixture server)
{
    private static readonly string[] Procedure =
    [
        """
        CREATE PROCEDURE dbo.obs_read_order @order nvarchar(max) AS
        SELECT json_value(@order, '$.mode') AS mode, json_query(@order, '$.lines') AS lines,
            (SELECT COUNT(*) FROM OPENJSON(@order, '$.lines')) AS line_count;
        """,
    ];

    /// <summary>Every result set of a call of the procedure with <paramref name="order"/> (a T-SQL literal), joined by <c> | </c>, or the error it fails with.</summary>
    private async Task<(string? Sets, int? Error)> Read(string order)
    {
        var outcome = await ObservedCall.Run(server, Procedure, $"EXEC dbo.obs_read_order @order = {order};");
        return outcome.Error is { } error ? (null, error) : (string.Join(" | ", outcome.Sets), null);
    }

    [Theory]
    [InlineData("N'not json'")]
    [InlineData("N''")]
    [InlineData("N'42'")]
    [InlineData("N'null'")]
    [InlineData("N'{\"mode\":\"fast\"'")]
    public async Task Text_that_isn_t_a_json_object_or_array_fails_the_call_in_lax_mode(string order) =>
        Assert.Equal(13609, (await Read(order)).Error);

    [Fact]
    public async Task Json_of_the_declared_shape_is_read_as_declared() =>
        Assert.Equal(
            """mode,lines,line_count: fast,[{"sku":"a"}],1""",
            (await Read("""N'{"mode":"fast","lines":[{"sku":"a"}]}'""")).Sets);

    [Theory]
    [InlineData("""N'{"other":1}'""")]
    [InlineData("""N'[1,2]'""")]
    [InlineData("""N'{"mode":["fast"],"lines":"x"}'""")]
    public async Task Json_of_another_shape_gives_null_under_a_lax_path(string order) =>
        Assert.Equal("mode,lines,line_count: null,null,0", (await Read(order)).Sets);

    [Fact]
    public async Task Null_gives_null() =>
        Assert.Equal("mode,lines,line_count: null,null,0", (await Read("NULL")).Sets);
}

using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Model;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A run file's argument reaches its parameter as an <c>EXEC</c> with the same literal would send it (<see cref="RunArgument"/>).
/// </summary>
public class RunArgumentTests(SqlServerFixture server)
{
    /// <summary>
    /// A fractional second past the declared scale is rounded by the server, as an <c>EXEC</c>'s literal is, so the argument is
    /// sent with all seven digits: SqlClient given the declared scale cuts the extra digits off on the client instead
    /// (<c>time(3)</c> given <c>.1239</c>: <c>.123</c>). The generated functions refuse such a value, so no run file can give
    /// one; the conversion is checked here, against an <c>EXEC</c> of the same procedure.
    /// </summary>
    [Theory]
    [InlineData("time", "3", "08:30:00.1239")]
    [InlineData("time", "2", "08:30:00.129")]
    [InlineData("datetime2", "2", "2026-11-16T08:30:00.129")]
    [InlineData("datetime2", "0", "2026-11-16T08:30:00.5")]
    [InlineData("datetimeoffset", "1", "2026-11-16T08:30:00.19+05:30")]
    public async Task A_digit_past_the_declared_scale_is_rounded_as_an_exec_rounds_it(string name, string scale, string text)
    {
        var token = TestContext.Current.CancellationToken;
        var type = new SqlType(SqlTypeKind.BuiltIn, name, [scale]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(text));
        var argument = RunFile.Value(json.RootElement, type, "a test");

        await using var session = await server.WriteAsync(token);
        await using (var create = session.Command($"CREATE PROCEDURE #echo @v {type} AS SELECT CONVERT(varchar(40), @v, 121);"))
            await create.ExecuteNonQueryAsync(token);
        await using var exec = session.Command($"EXEC #echo '{text}';");
        var expected = (string?)await exec.ExecuteScalarAsync(token);
        await using var sent = session.Command("#echo");
        sent.CommandType = System.Data.CommandType.StoredProcedure;
        sent.Parameters.Add(argument.Parameter("@v"));

        Assert.Equal(expected, (string?)await sent.ExecuteScalarAsync(token));
    }
}

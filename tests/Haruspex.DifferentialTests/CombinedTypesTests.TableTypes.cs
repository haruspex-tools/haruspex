using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A table-valued parameter combined, held to the server: its columns narrowed to what both types take, the combined function's
/// rows are taken by the procedure of either database, and a row only one type takes is refused before it is sent. Each database
/// is one of the test's own, its types committed, since a type created in the transaction a call runs in deadlocks it.
/// </summary>
public partial class CombinedTypesTests
{
    private const string BaselineLines = "CREATE TYPE drift.lines AS TABLE (a int NOT NULL, qty smallint NULL, note nvarchar(20) NULL, PRIMARY KEY (a));";

    private const string CandidateLines = "CREATE TYPE drift.lines AS TABLE (a int NOT NULL, qty smallint NOT NULL CHECK (qty > 0), note nvarchar(40) NULL, PRIMARY KEY (a));";

    private const string CountLines = "CREATE PROCEDURE drift.count_lines @lines drift.lines READONLY AS SELECT count(*) AS n FROM @lines;";

    private static IrDocument Lines(string type) =>
        InferProcedure.Infer("drift.*", ("schema.sql", "CREATE SCHEMA drift;"), ("type.sql", type), ("procedure.sql", CountLines));

    private static Dictionary<string, object?> Line(int a, int? qty, string note) => new() { ["a"] = a, ["qty"] = qty, ["note"] = note };

    [Theory]
    [InlineData("the baseline's", BaselineLines)]
    [InlineData("the candidate's", CandidateLines)]
    public async Task A_combined_table_valued_parameter_sends_rows_both_types_take(string which, string type)
    {
        var token = TestContext.Current.CancellationToken;
        var combined = IrCombination.Of(Lines(BaselineLines), Lines(CandidateLines), new IrFitOptions());
        Assert.Empty(combined.Findings);

        await using var database = await server.ScratchAsync();
        await database.ExecuteAsync("CREATE SCHEMA drift;", token);
        await database.ExecuteAsync(type, token);
        await database.ExecuteAsync(CountLines, token);
        var responses = await GeneratedFunctions.RunAsync(
            server,
            combined.Document,
            [],
            [
                new GeneratedFunctions.Call("both take it", "countLines", new Dictionary<string, object?> { ["lines"] = new[] { Line(1, 2, new string('x', 20)) } }),
                new GeneratedFunctions.Call("a null qty, which the candidate refuses", "countLines", new Dictionary<string, object?> { ["lines"] = new[] { Line(1, null, "x") } }, Db: "untouchable"),
                new GeneratedFunctions.Call("a note of 21, which the baseline refuses", "countLines", new Dictionary<string, object?> { ["lines"] = new[] { Line(1, 2, new string('x', 21)) } }, Db: "untouchable"),
                new GeneratedFunctions.Call("qty 0, which the candidate's CHECK refuses", "countLines", new Dictionary<string, object?> { ["lines"] = new[] { Line(1, 0, "x") } }, Db: "untouchable"),
            ],
            token,
            committed: database);

        Assert.True(responses[0].Error is null, $"{which}: {responses[0].Error}");
        Assert.All(responses.Skip(1), r => Assert.Equal("HaruspexParameterError", r.Error?.Name));
    }
}

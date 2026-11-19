using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A table type changed under its name, held to the server: the application's function, generated from the baseline, sends a
/// row to the procedure as the candidate defines its type. The operation must refuse the change exactly when the call fails or
/// returns another value than the baseline's would, and pass it when the call returns what the baseline's would. Each case runs
/// in a database of its own whose types are committed, since a type created in the transaction a call runs in deadlocks it.
/// </summary>
public partial class BaselineFitTests
{
    private const string TableLines = "CREATE TYPE drift.lines AS TABLE (a int NOT NULL, b int NOT NULL, note nvarchar(10) NULL, PRIMARY KEY (a));";

    // The procedure reads its columns by name, so a value that went to another column shows; it returns them as JSON text, a result
    // no change to the type changes, so that only the parameter is judged.
    private const string ReadLines = """
        CREATE PROCEDURE drift.read_lines @lines drift.lines READONLY AS
        SELECT CAST((SELECT a, b, note FROM @lines ORDER BY a FOR JSON PATH, INCLUDE_NULL_VALUES) AS nvarchar(4000)) AS sent;
        """;

    private const string Sent = """[{"a":1,"b":7,"note":"abcdef"},{"a":2,"b":7,"note":null}]""";

    /// <summary>What the procedure says it was sent.</summary>
    private static string? Received(GeneratedFunctions.Response response) => response.Value?[0].GetProperty("sent").GetString();

    private static Dictionary<string, object?> Row(int a, int b, string? note) => new() { ["a"] = a, ["b"] = b, ["note"] = note };

    public static TheoryData<string, string, int[]?> TableTypeChanges { get; } = new()
    {
        { "nothing", TableLines, null },
        { "a length widened", Edit(TableLines, "nvarchar(10)", "nvarchar(20)"), null },
        { "a column that became nullable", Edit(TableLines, "b int NOT NULL", "b int NULL"), null },
        { "a key removed", Edit(TableLines, ", PRIMARY KEY (a)", ""), null },
        { "a default added", Edit(TableLines, "note nvarchar(10) NULL", "note nvarchar(10) NULL DEFAULT N'x'"), null },
        { "a column added", Edit(TableLines, "note nvarchar(10) NULL,", "note nvarchar(10) NULL, extra int NULL,"), [500] },
        { "a column that became NOT NULL", Edit(TableLines, "note nvarchar(10) NULL", "note nvarchar(10) NOT NULL"), [515, 8061] },
        { "a length narrowed", Edit(TableLines, "nvarchar(10)", "nvarchar(3)"), [2628, 8061] }, // declared as the old length, then cut: refused
        { "a check added", Edit(TableLines, "b int NOT NULL", "b int NOT NULL CHECK (b < 5)"), [547, 8061] },
        { "a key added", Edit(TableLines, "PRIMARY KEY (a)", "PRIMARY KEY (a), UNIQUE (b)"), [2627, 8061] },
        { "two columns swapped", Edit(TableLines, "a int NOT NULL, b int NOT NULL, note nvarchar(10) NULL, PRIMARY KEY (a)", "b int NOT NULL, a int NOT NULL, note nvarchar(10) NULL"), [] }, // returns, the values swapped
    };

    [Theory]
    [MemberData(nameof(TableTypeChanges))]
    public async Task A_table_type_changed_under_its_name_is_refused_exactly_when_the_old_call_fails_or_changes(string change, string candidate, int[]? fails)
    {
        var token = TestContext.Current.CancellationToken;
        var baseline = Take(TableLines, ReadLines);
        var refused = IrFit.Of(baseline, Take(candidate, ReadLines), new IrFitOptions()).Where(f => f.Verdict == IrFitVerdict.Refused).ToList();

        await using var database = await server.ScratchAsync();
        await database.ExecuteAsync("CREATE SCHEMA drift;", token);
        await database.ExecuteAsync(candidate, token);
        await database.ExecuteAsync(ReadLines, token);

        // Two rows the baseline's type takes, which share b and have a longer note, so that each change that can refuse one does.
        var response = Assert.Single(await GeneratedFunctions.RunAsync(
            server,
            baseline,
            [],
            [new GeneratedFunctions.Call(change, "readLines", new Dictionary<string, object?> { ["lines"] = new[] { Row(1, 7, "abcdef"), Row(2, 7, null) } })],
            token,
            committed: database));

        Assert.True((fails is not null) == (refused.Count > 0), $"{change}: the operation {(refused.Count > 0 ? $"refuses it ({string.Join("; ", refused)})" : "passes it")}, and the call {(response.Error is null ? $"returned {JsonSerializer.Serialize(response.Value)}" : $"failed: {response.Error}")}");
        switch (fails)
        {
            case null:
                Assert.Null(response.Error);
                Assert.Equal(Sent, Received(response));
                break;
            case []:
                Assert.Null(response.Error);
                Assert.NotEqual(Sent, Received(response));
                break;
            case [var number]:
                Assert.Equal(number, response.Error?.Number);
                break;
            default:
                Assert.Equal(fails, response.Error?.Preceding);
                break;
        }
    }

    [Fact]
    public async Task Another_table_type_of_the_same_columns_is_refused_and_fails_the_old_call()
    {
        var token = TestContext.Current.CancellationToken;
        var twin = TableLines.Replace("drift.lines", "drift.twin", StringComparison.Ordinal);
        var procedure = ReadLines.Replace("drift.lines", "drift.twin", StringComparison.Ordinal);
        var baseline = Take(TableLines, ReadLines);

        Assert.Single(IrFit.Of(baseline, Take(TableLines + "\nGO\n" + twin, procedure), new IrFitOptions()));

        await using var database = await server.ScratchAsync();
        await database.ExecuteAsync("CREATE SCHEMA drift;", token);
        await database.ExecuteAsync(TableLines, token);
        await database.ExecuteAsync(twin, token);
        await database.ExecuteAsync(procedure, token);
        var response = Assert.Single(await GeneratedFunctions.RunAsync(
            server, baseline, [], [new GeneratedFunctions.Call("twin", "readLines", new Dictionary<string, object?> { ["lines"] = new[] { Row(1, 2, null) } })], token, committed: database));

        Assert.Equal(206, response.Error?.Number);
    }

    private static string Edit(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }
}

using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server does with the characters .NET's <c>ReplaceLineEndings</c> takes for a line's end besides CR and LF: form feed,
/// NEL (U+0085), and the Unicode line and paragraph separators (U+2028, U+2029). It keeps each in a string literal, and ends neither
/// a <c>--</c> comment nor a line at one, so a query file's text is sent with them as written (F068; <c>QueryFileReader.Read</c>).
/// That the generated function sends them is in <c>MssqlFunctionTests</c>. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class QueryTextObservationTests(SqlServerFixture server)
{
    /// <summary>Each character, as its code point.</summary>
    public static TheoryData<int> NotLineEnds { get; } = [0x0C, 0x85, 0x2028, 0x2029];

    private Task<CallOutcome> Run(string batch) => ObservedCall.Run(server, [], batch);

    [Theory]
    [MemberData(nameof(NotLineEnds))]
    public async Task A_literal_keeps_the_character_as_written(int code) =>
        Assert.Equal(
            $"length,code: 3,{code}",
            (await Run($"SELECT LEN(N'a{(char)code}b') AS length, UNICODE(SUBSTRING(N'a{(char)code}b', 2, 1)) AS code;")).ToString());

    [Theory]
    [MemberData(nameof(NotLineEnds))]
    public async Task A_dash_comment_runs_on_past_the_character_to_the_line_s_end(int code) =>
        Assert.Equal("one: 1", (await Run($"SELECT 1 AS one -- a note{(char)code}, 2 AS two\n;")).ToString());

    [Theory]
    [MemberData(nameof(NotLineEnds))]
    public async Task The_server_counts_no_line_at_the_character(int code) =>
        // The empty set is the division's, whose columns are sent before it fails.
        Assert.Equal(
            "boom:  | line: 2",
            (await Run($"BEGIN TRY\nDECLARE @x int = 1;{(char)code}SELECT 1 / 0 AS boom;\nEND TRY BEGIN CATCH SELECT ERROR_LINE() AS line; END CATCH;")).ToString());
}

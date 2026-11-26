using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Semantics.Collations;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A canonical value set agrees with the server both ways: a <c>varchar</c> value equals a member under the column's collation
/// exactly when its fold (<see cref="CollationFold.Fold"/>) is that member's. Candidates are made from a sample of plain members:
/// every character the code page holds put at every position of each member, and every pair of them at the start, the middle and
/// the end of three; so trailing spaces before an ignored character, characters that look like others (the code page has Latin
/// letters with accents, ligatures and the soft hyphen, though not full-width forms) and ignored characters anywhere are all
/// among them. Each candidate is stored in a <c>varchar</c> column of the collation, as a value is, and read back as the driver
/// reads it; the server's equality is the one a <c>CHECK … IN</c> uses (<c>CheckConstraintObservationTests</c>). Supplementary
/// characters and lone surrogates can't be stored in a code page, so they aren't candidates. Observed on SQL Server 2025
/// RTM-CU10.
/// </summary>
public class CanonicalValueSetObservationTests(SqlServerFixture server)
{
    private static readonly string[] Members = ["paid", "due", "on-hold", "x_y", "v1.2", "aab", "free", "two words", "ss", "ae", "th", "oe", "A1", "-", ".", "9"];

    private static readonly string[] PairedInto = ["paid", "on-hold", "aab"];

    public static TheoryData<string> Cells { get; } = [.. CollationFold.Proven.Select(p => p.Collation)];

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task A_value_equals_a_member_exactly_when_their_folds_are_equal(string collation)
    {
        await server.RequireAsync();
        var fold = CollationFold.For(collation, StringKind.CodePage)!;
        var byFold = Members.ToDictionary(fold.Fold, m => m, StringComparer.Ordinal);
        Assert.Equal(Members.Length, byFold.Count);

        var members = string.Join(", ", Members.Select(m => $"(N'{m}')"));
        var paired = string.Join(", ", PairedInto.Select(m => $"(N'{m}')"));
        var batch = $"""
            SET NOCOUNT ON;
            CREATE TABLE #u (x nvarchar(1) NOT NULL);
            WITH d AS (SELECT v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15)) AS d(v)),
            n AS (SELECT a.v + 16 * b.v + 256 * c.v + 4096 * e.v AS u FROM d a, d b, d c, d e)
            INSERT INTO #u SELECT CAST(NCHAR(n.u) AS nvarchar(1)) FROM n
            WHERE CAST(CAST(CAST(NCHAR(n.u) AS nvarchar(1)) COLLATE {collation} AS varchar(8)) AS nvarchar(8)) COLLATE Latin1_General_BIN2
                = CAST(NCHAR(n.u) AS nvarchar(1)) COLLATE Latin1_General_BIN2;
            CREATE TABLE #m (m varchar(40) COLLATE {collation} NOT NULL);
            INSERT INTO #m VALUES {members};
            CREATE TABLE #v (id int IDENTITY PRIMARY KEY, v varchar(40) COLLATE {collation} NOT NULL);
            WITH m AS (SELECT m FROM (VALUES {members}) m(m)),
            p AS (SELECT m, n FROM m CROSS APPLY (SELECT TOP (LEN(m) + 1) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n FROM #u) n)
            INSERT INTO #v (v) SELECT LEFT(p.m, p.n - 1) + u.x + SUBSTRING(p.m, p.n, 40) FROM p CROSS JOIN #u u;
            WITH m AS (SELECT m FROM (VALUES {paired}) m(m)),
            p AS (SELECT m, n FROM m CROSS APPLY (VALUES (1), (LEN(m) / 2 + 1), (LEN(m) + 1)) n(n))
            INSERT INTO #v (v) SELECT LEFT(p.m, p.n - 1) + a.x + b.x + SUBSTRING(p.m, p.n, 40) FROM p CROSS JOIN #u a CROSS JOIN #u b;
            SELECT v.id, CAST(v.v AS nvarchar(40)), m.m FROM #v v LEFT JOIN #m m ON v.v = m.m OPTION (LOOP JOIN);
            """;

        var cancel = TestContext.Current.CancellationToken;
        await using var session = await server.ReadAsync(cancel);
        await using var command = session.Command(batch);
        command.CommandTimeout = 1800;
        await using var reader = await command.ExecuteReaderAsync(cancel);
        var disagreements = new List<string>();
        var (candidates, accepted) = (0, 0);
        while (await reader.ReadAsync(cancel))
        {
            candidates++;
            var value = reader.GetString(1);
            var member = await reader.IsDBNullAsync(2, cancel) ? null : reader.GetString(2);
            accepted += member is null ? 0 : 1;
            var folded = byFold.GetValueOrDefault(fold.Fold(value));
            if (folded != member && disagreements.Count < 20)
                disagreements.Add($"'{Escaped(value)}': the server says {(member is null ? "no member" : $"'{member}'")}, the fold {(folded is null ? "no member" : $"'{folded}'")}");
        }

        Assert.True(disagreements.Count == 0, $"Under {collation}:\n{string.Join("\n", disagreements)}");
        Assert.True(candidates > 100_000 && accepted >= Members.Length, $"Under {collation}, {candidates} candidates and {accepted} accepted: too few to show anything.");
    }

    private static string Escaped(string value) =>
        string.Concat(value.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));
}

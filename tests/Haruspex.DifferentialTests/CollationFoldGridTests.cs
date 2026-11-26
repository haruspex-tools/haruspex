using Haruspex.Collections;
using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Semantics.Collations;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Each proven collation's fold (<see cref="CollationFold"/>) is what the server does. Every character its code page holds is
/// compared, between two others, with every target character and with the empty string; and one that equals none of them with
/// every pair of letters. The classes the targets fall into, the characters equal to each and the characters ignored are the fold's
/// data file (<c>src/Haruspex.SqlServer.Semantics/Collations</c>), so a run against the server either finds the committed file
/// exact or fails, and <c>HARUSPEX_ACCEPT=1</c> rewrites it. A character's class must be the same beside every target, on its
/// left (<c>t + x + 'b'</c>) and on its right (<c>'a' + x + t</c>), or the fold, which looks at one character at a time, couldn't
/// be the collation's equality, and the cell fails. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class CollationFoldGridTests(SqlServerFixture server)
{
    public static TheoryData<string> Cells { get; } = [.. CollationFold.Proven.Select(p => p.Collation)];

    public static TheoryData<string> UnicodeCells { get; } =
        ["SQL_Latin1_General_CP1_CI_AS", "Latin1_General_CI_AS", "Latin1_General_100_CI_AS", "Latin1_General_100_CS_AS", "Latin1_General_100_CI_AI", "Latin1_General_100_CI_AS_SC_UTF8"];

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task The_fold_of_each_proven_collation_is_the_one_observed(string collation)
    {
        await server.RequireAsync();
        var observed = await Observe(collation);
        var path = Path.Combine(RepositoryPaths.Root, "src", "Haruspex.SqlServer.Semantics", "Collations", CollationFold.FileName(collation, StringKind.CodePage) + ".json");
        if (Environment.GetEnvironmentVariable("HARUSPEX_ACCEPT") == "1")
            await File.WriteAllTextAsync(path, observed.ToJson(), TestContext.Current.CancellationToken);
        Assert.True(File.Exists(path), $"{path} is missing; rerun with HARUSPEX_ACCEPT=1 to write it.");
        Assert.True(observed.ToJson() == await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), $"{path} is not what the server does; rerun with HARUSPEX_ACCEPT=1 and read the diff.");
        if (Environment.GetEnvironmentVariable("HARUSPEX_ACCEPT") != "1")
            Assert.Equal(observed, CollationFold.For(collation, StringKind.CodePage));
    }

    /// <summary>
    /// Why no Unicode collation is proven: a repeat mark (U+3031) equals the character before it, so its class depends on its
    /// neighbour, under every collation a fold might be wanted for, and as <c>varchar</c> under a <c>_UTF8</c> collation too.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnicodeCells))]
    public async Task Under_the_Unicode_rules_a_repeat_mark_equals_the_character_before_it(string collation)
    {
        await server.RequireAsync();
        var cancel = TestContext.Current.CancellationToken;
        await using var session = await server.ReadAsync(cancel);
        foreach (var type in collation.EndsWith("_UTF8", StringComparison.Ordinal) ? ["nvarchar(10)", "varchar(10)"] : new[] { "nvarchar(10)" })
        {
            await using var command = session.Command($"""
                SELECT CASE WHEN CAST((N'a' + NCHAR(12337) + N'b') COLLATE {collation} AS {type}) = CAST(N'aab' COLLATE {collation} AS {type}) THEN 1 ELSE 0 END,
                       CASE WHEN CAST((N'z' + NCHAR(12337) + N'b') COLLATE {collation} AS {type}) = CAST(N'zzb' COLLATE {collation} AS {type}) THEN 1 ELSE 0 END
                """);
            await using var reader = await command.ExecuteReaderAsync(cancel);
            Assert.True(await reader.ReadAsync(cancel));
            Assert.Equal((1, 1), (reader.GetInt32(0), reader.GetInt32(1)));
        }
    }

    /// <summary>The fold the server shows for a collation's code page.</summary>
    private async Task<CollationFold> Observe(string collation)
    {
        string Text(string expression) => $"CAST(({expression}) COLLATE {collation} AS varchar(40))";

        var targets = string.Join(", ", CollationFold.Targets.Select(c => $"(N'{c}')"));
        var letters = string.Join(", ", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".Select(c => $"(N'{c}')"));

        // The contexts: each target, or nothing, on the left of the character with b on its right; and a on its left with each
        // target but a space on its right (a space there would be a trailing one, which every comparison ignores).
        var contexts = string.Join(", ", CollationFold.Targets.Select(c => $"(N'{c}', N'b')").Append("(N'', N'b')")
            .Concat(CollationFold.Targets.Where(c => c != ' ').Select(c => $"(N'a', N'{c}')")));
        var batch = $"""
            SET NOCOUNT ON;
            CREATE TABLE #s (u int PRIMARY KEY, x nvarchar(1) NOT NULL);
            WITH d AS (SELECT v FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15)) AS d(v)),
            n AS (SELECT a.v + 16 * b.v + 256 * c.v + 4096 * e.v AS u FROM d a, d b, d c, d e)
            INSERT INTO #s SELECT n.u, CAST(NCHAR(n.u) AS nvarchar(1)) FROM n
            WHERE CAST(CAST(CAST(NCHAR(n.u) AS nvarchar(1)) COLLATE {collation} AS varchar(8)) AS nvarchar(8)) COLLATE Latin1_General_BIN2
                = CAST(NCHAR(n.u) AS nvarchar(1)) COLLATE Latin1_General_BIN2;
            CREATE TABLE #t (t nvarchar(2) NOT NULL);
            INSERT INTO #t VALUES {targets}, (N'');
            CREATE TABLE #p (t nvarchar(2) NOT NULL);
            INSERT INTO #p SELECT a.t + b.t FROM (VALUES {letters}) a(t) CROSS JOIN (VALUES {letters}) b(t);
            CREATE TABLE #c (id int IDENTITY PRIMARY KEY, l nvarchar(1) NOT NULL, r nvarchar(1) NOT NULL);
            INSERT INTO #c (l, r) VALUES {contexts};
            SELECT a.t, b.t FROM #t a CROSS JOIN #t b WHERE {Text("N'a' + a.t + N'b'")} = {Text("N'a' + b.t + N'b'")} OPTION (LOOP JOIN);
            SELECT u.u, c.id, t.t FROM #s u CROSS JOIN #c c CROSS JOIN #t t WHERE {Text("c.l + u.x + c.r")} = {Text("c.l + t.t + c.r")} OPTION (LOOP JOIN);
            SELECT u.u, p.t INTO #m FROM #s u CROSS JOIN #p p WHERE {Text("N'a' + u.x + N'b'")} = {Text("N'a' + p.t + N'b'")}
                AND NOT EXISTS (SELECT 1 FROM #t t WHERE {Text("N'a' + u.x + N'b'")} = {Text("N'a' + t.t + N'b'")}) OPTION (LOOP JOIN);
            SELECT u, t FROM #m;
            SELECT m.u, c.id FROM #m m JOIN #s u ON u.u = m.u CROSS JOIN #c c WHERE NOT ({Text("c.l + u.x + c.r")} = {Text("c.l + m.t + c.r")}) OPTION (LOOP JOIN);
            SELECT COUNT(*) FROM #c;
            """;

        // OPTION (LOOP JOIN): a hash join on these expressions gives wrong rows (observed: with WHERE 'a' + x + 'b' = 'a' + t + 'b'
        // over two temporary tables, under Latin1_General_100_CI_AS, a hash join missed a = a and paired U+00AD with a space; the same
        // comparison in a CASE, or as a loop join, is right). Each target in its own class, checked below, catches it if it recurs.
        var cancel = TestContext.Current.CancellationToken;
        await using var session = await server.ReadAsync(cancel);
        await using var command = session.Command(batch);
        command.CommandTimeout = 1800;
        await using var reader = await command.ExecuteReaderAsync(cancel);
        var targetEqual = new List<(string A, string B)>();
        while (await reader.ReadAsync(cancel))
            targetEqual.Add((reader.GetString(0), reader.GetString(1)));
        var inContext = new List<(int Unit, int Context, string Target)>();
        await reader.NextResultAsync(cancel);
        while (await reader.ReadAsync(cancel))
            inContext.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        var pairs = new List<(int Unit, string Target)>();
        await reader.NextResultAsync(cancel);
        while (await reader.ReadAsync(cancel))
            pairs.Add((reader.GetInt32(0), reader.GetString(1)));
        var pairsBroken = new List<(int Unit, int Context)>();
        await reader.NextResultAsync(cancel);
        while (await reader.ReadAsync(cancel))
            pairsBroken.Add((reader.GetInt32(0), reader.GetInt32(1)));
        await reader.NextResultAsync(cancel);
        Assert.True(await reader.ReadAsync(cancel));
        var contextCount = reader.GetInt32(0);

        // A class is the targets equal to each other; its representative is its least, ordinally. A pair of letters is in the
        // class of the pair its letters' representatives make.
        string Representative(string target) => target.Length == 2
            ? Representative(target[..1]) + Representative(target[1..])
            : targetEqual.Where(e => e.A == target).Select(e => e.B).Append(target).Min(StringComparer.Ordinal)!;

        // Every character's class, the same in every context: its targets' representatives in each, one in all of them.
        var classOf = new Dictionary<int, string>();
        foreach (var unit in inContext.GroupBy(e => e.Unit))
        {
            var byContext = unit.GroupBy(e => e.Context).ToList();
            var representatives = unit.Select(e => e.Target.Length == 0 ? "" : Representative(e.Target)).Distinct(StringComparer.Ordinal).ToList();
            Assert.True(byContext.Count == contextCount && representatives.Count == 1,
                $"U+{CollationFold.Hex(unit.Key)} is not in one class beside every target under {collation}: it equals {string.Join(", ", representatives.Select(r => $"'{r}'"))} in {byContext.Count} of {contextCount} contexts.");
            classOf[unit.Key] = representatives[0];
        }

        foreach (var (unit, target) in pairs)
            Assert.True(classOf.TryAdd(unit, Representative(target)) || classOf[unit] == Representative(target), $"U+{CollationFold.Hex(unit)} equals two pairs under {collation}.");
        Assert.True(pairsBroken.Count == 0, $"Under {collation}, U+{(pairsBroken.Count > 0 ? CollationFold.Hex(pairsBroken[0].Unit) : "")} equals its pair of letters in some contexts only.");
        foreach (var target in CollationFold.Targets)
            Assert.True(classOf.GetValueOrDefault(target) == Representative(target.ToString()), $"U+{CollationFold.Hex(target)} is not in its own class under {collation}.");

        var classes = classOf.Where(c => c.Value.Length > 0)
            .GroupBy(c => c.Value, StringComparer.Ordinal)
            .ToEquatableMap(g => g.Key, g => CollationFold.Units(g.Select(c => c.Key)));
        return new CollationFold(collation, StringKind.CodePage, CollationFold.Units(classOf.Where(c => c.Value.Length == 0).Select(c => c.Key)), classes);
    }
}

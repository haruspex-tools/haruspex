using System.Globalization;
using System.Text;
using Haruspex.DifferentialTests.Server;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Every expression of the sweep (<see cref="CatalogueSweep"/>: each built-in type through arithmetic, a sign, <c>CAST</c>,
/// <c>ISNULL</c> and the aggregates) that haruspex types has the type SQL Server describes for the same query. So the unit
/// tests' snapshot of the sweep, <c>CatalogueSweepTests</c>, holds only what the server does, for every type, not only those a
/// fixture reaches. An expression haruspex leaves unknown claims nothing and is not compared; one the server refuses is
/// unknown in haruspex too, except over a type inference doesn't model (<see cref="ReadsAnUnmodelledType"/>).
/// </summary>
/// <remarks>
/// The first theory checks the type, which is the server's to describe; not why a value may be null, nor the facets, since the
/// server describes neither. The second checks those against the values the server gives: each claim of never null, a range or
/// a length, over rows at every type's edges. Neither shows a reason is needed, only that a claim without one holds; the
/// reasons rest on the observation tests each rule names.
/// </remarks>
public class CatalogueSweepGridTests(SqlServerFixture server)
{
    public static TheoryData<string> Forms { get; } = [.. CatalogueSweep.Forms];

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task Each_expression_haruspex_types_has_the_type_sql_server_describes(string form)
    {
        await server.RequireAsync();
        var outcomes = CatalogueSweep.Infer(form);
        var typed = outcomes.Where(o => o.Sql is not null).ToList();
        var described = await Describe([.. typed.Select(o => CatalogueSweep.Query(o.Expression))]);

        var differences = typed
            .Select((o, i) => (o.Expression, Haruspex: o.Sql, Server: described[i]))
            .Where(d => !SameType(d.Haruspex, d.Server))
            .Where(d => !(d.Server.StartsWith('!') && ReadsAnUnmodelledType(d.Expression)))
            .Select(d => $"{d.Expression}: haruspex {d.Haruspex}, SQL Server {d.Server}")
            .ToList();
        Assert.True(differences.Count == 0, $"{differences.Count} of {typed.Count} typed expressions differ:\n" + string.Join("\n", differences));
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task Each_expression_claimed_never_null_or_bounded_holds_over_its_types_edges(string form)
    {
        // Each expression haruspex types never null, or within a range or a length, is run over rows at every type's edges,
        // under the caller's options that make an overflow NULL rather than an error. They run in read sessions, over a
        // temporary copy of the table, so that no transaction is open for an error to doom. Each runs on its own, since a
        // failed conversion is an error that ends its statement and claims nothing about a value; but it ends the statement
        // for every row, so an expression that errors over the whole table runs again a row at a time, and each row it gives
        // is checked. A claim is left unchecked only when every row errors: those are the snapshot below.
        await server.RequireAsync();
        var claims = CatalogueSweep.Infer(form)
            .Where(o => o.Field is { Type: IrScalar scalar } field && (field.NullableBecause.Count == 0 || scalar.Facets is { Min: not null } or { Max: not null } or { Length: not null }))
            .ToList();

        // The canary, a claim known to be false: never null, over an expression that fails on edge row 0 and so over the whole
        // table, and is NULL on every other row. It runs with the rest, and must be reported from the rows read again alone,
        // which shows that those rows are checked and not only read.
        var canary = claims.Count;
        claims.Add(new CatalogueSweep.Outcome(Canary, new IrField("e", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"), IrPresence.Always, []), null));
        var cancel = TestContext.Current.CancellationToken;
        var edgeRows = Enumerable.Range(0, CatalogueSweep.EdgeRows().Count).ToList();
        var whole = await RunOverEdges(claims, [.. Enumerable.Range(0, claims.Count).Select(n => (n, (int?)null))], cancel);
        List<int> failed = [.. whole.Errors.Select(e => e.Claim).Except(whole.Rows.Select(r => r.Claim)).Order()];
        var byRow = await RunOverEdges(claims, [.. failed.SelectMany(n => edgeRows.Select(k => (n, (int?)k)))], cancel);

        // Every claim was read over the whole table: it gave its rows, or an error.
        Assert.Equal(claims.Count, whole.Rows.Select(r => r.Claim).Concat(whole.Errors.Select(e => e.Claim)).Distinct().Count());

        // Every row of every claim that failed as a whole was read again: it gave its value, or an error.
        var reread = byRow.Rows.Select(r => (r.Claim, r.Row)).Concat(byRow.Errors.Select(e => (e.Claim, e.Row))).Distinct().ToList();
        Assert.Equal(failed.Count * edgeRows.Count, reread.Count);

        var problems = whole.Rows.Concat(byRow.Rows).SelectMany(row => Problems(claims[row.Claim], row).Select(p => (row.Claim, Problem: p))).Distinct().ToList();
        Assert.Contains(problems, p => p.Claim == canary && p.Problem == $"{Canary} (edge row 1 alone): claimed never null, and NULL");
        var broken = problems.Where(p => p.Claim != canary).ToList();
        Assert.True(broken.Count == 0, $"{broken.Select(p => p.Claim).Distinct().Count()} of {claims.Count - 1} claims fail at the edges, "
            + $"in {broken.Count} problems:\n" + string.Join("\n", broken.Select(p => p.Problem)));

        // A claim no row of which runs claims nothing the sweep can see. Each is kept as a snapshot with its errors, so that a
        // new one shows in review; the server refuses some when it compiles them, which is a claim the type sweep counts and
        // leaves only over a type inference doesn't model (ReadsAnUnmodelledType), and must be that kind here too.
        var noValue = failed.Except(byRow.Rows.Select(r => r.Claim)).ToList();
        var refusals = await Describe([.. noValue.Select(n => CatalogueSweep.Query(claims[n].Expression))]);
        var notDocumented = noValue.Where((n, i) => refusals[i].StartsWith('!') && !ReadsAnUnmodelledType(claims[n].Expression)).Select(n => claims[n].Expression).ToList();
        Assert.True(notDocumented.Count == 0, "The server refuses these, which haruspex types:\n" + string.Join("\n", notDocumented));
        var lines = noValue.Select((n, i) => $"{claims[n].Expression}: "
            + (refusals[i].StartsWith('!') ? "refused " + refusals[i][1..] : string.Join(", ", byRow.Errors.Where(e => e.Claim == n).Select(e => e.Error).Distinct().Order())));
        MatchesSnapshot(form, string.Concat(lines.Select(l => l + "\n")));
    }

    /// <summary>The edge test's canary: an error on edge row 0, NULL on every other.</summary>
    private const string Canary = "CASE WHEN id = 0 THEN CAST(N'x' AS int) END";

    /// <summary>The temporary copy of the edge table a query reads in the edge test's read session.</summary>
    private static string Temporary(string sql) => sql.Replace("dbo.g", "#g", StringComparison.Ordinal);

    /// <summary>One value a claim gave: the claim's index, the edge row it ran on alone (or null for the whole table), whether
    /// it was NULL, its decimal text (empty for a float or a non-number) and its length in bytes (-1 when NULL).</summary>
    private sealed record EdgeValue(int Claim, int? Row, bool Null, string Text, long Bytes);

    /// <summary>The values a batch of claims gave, and the errors that ended the others.</summary>
    private sealed record EdgeRun(List<EdgeValue> Rows, List<(int Claim, int? Row, int Error)> Errors);

    /// <summary>
    /// The most queries one read session runs. Chosen when every write waited at one gate for the reads already in, which a long
    /// read could hold for two minutes; write sessions now have databases of their own, and the batches are kept as they were,
    /// so the sweep runs the same queries.
    /// </summary>
    private const int RunsPerSession = 250;

    /// <summary>
    /// Runs each claim's query, over the whole edge table or over one row of it, each in its own <c>TRY</c>, and returns the
    /// values they gave and the errors that ended them. They run in batches, each in a read session of its own over a
    /// temporary copy of the table (<see cref="RunsPerSession"/>).
    /// </summary>
    private async Task<EdgeRun> RunOverEdges(List<CatalogueSweep.Outcome> claims, IReadOnlyList<(int Claim, int? Row)> runs, CancellationToken cancel)
    {
        var all = new EdgeRun([], []);
        foreach (var chunk in runs.Chunk(RunsPerSession))
        {
            var run = await RunInSession(claims, chunk, cancel);
            all.Rows.AddRange(run.Rows);
            all.Errors.AddRange(run.Errors);
        }

        return all;
    }

    /// <summary>Runs one batch of <see cref="RunOverEdges"/> in a read session of its own, over a copy of the edge table.</summary>
    private async Task<EdgeRun> RunInSession(List<CatalogueSweep.Outcome> claims, IReadOnlyList<(int Claim, int? Row)> runs, CancellationToken cancel)
    {
        await using var session = await server.ReadAsync(cancel);
        await using (var setup = session.Command(Temporary(CatalogueSweep.Table + "\n" + string.Join("\n", CatalogueSweep.EdgeRows()))))
            await setup.ExecuteNonQueryAsync(cancel);
        var batch = new StringBuilder("""
            SET NOCOUNT ON; SET ANSI_WARNINGS OFF; SET ARITHABORT OFF;
            DROP TABLE IF EXISTS #r; DROP TABLE IF EXISTS #e;
            CREATE TABLE #r (n int NOT NULL, k int NULL, absent bit NOT NULL, v nvarchar(400) NULL, bytes bigint NULL);
            CREATE TABLE #e (n int NOT NULL, k int NULL, error int NOT NULL);

            """);
        foreach (var (claim, row) in runs)
        {
            // money is written with its four places by style 2; other numbers by their own text.
            var scalar = (IrScalar)claims[claim].Field!.Type;
            var text = scalar.Kind != IrScalarKind.Number || scalar.Format == IrFormat.Float ? "NULL"
                : ScalarType.Parse(scalar.Sql) is { IsMoney: true } ? "CONVERT(nvarchar(400), x.e, 2)" : "CONVERT(nvarchar(400), x.e)";
            var n = claim.ToString(CultureInfo.InvariantCulture);
            var k = row?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
            var query = Temporary(CatalogueSweep.Query(claims[claim].Expression)) + (row is null ? "" : $" WHERE id = {k}");
            var inner = $"SELECT {n}, {k}, CASE WHEN x.e IS NULL THEN 1 ELSE 0 END, {text}, DATALENGTH(x.e) FROM ({query}) AS x";
            batch.Append(CultureInfo.InvariantCulture, $"BEGIN TRY INSERT INTO #r EXEC sp_executesql N'{inner.Replace("'", "''", StringComparison.Ordinal)}'; END TRY BEGIN CATCH INSERT INTO #e VALUES ({n}, {k}, ERROR_NUMBER()); END CATCH;\n");
        }

        batch.Append("SELECT n, k, absent, ISNULL(v, N''), ISNULL(bytes, -1) FROM #r ORDER BY n, k; SELECT n, k, error FROM #e ORDER BY n, k;");
        var rows = new List<EdgeValue>();
        var errors = new List<(int Claim, int? Row, int Error)>();
        await using var command = session.Command(batch.ToString());
        command.CommandTimeout = 600;
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
        {
            rows.Add(new EdgeValue(
                reader.GetInt32(0), await reader.IsDBNullAsync(1, cancel) ? null : reader.GetInt32(1), reader.GetBoolean(2), reader.GetString(3), reader.GetInt64(4)));
        }

        await reader.NextResultAsync(cancel);
        while (await reader.ReadAsync(cancel))
            errors.Add((reader.GetInt32(0), await reader.IsDBNullAsync(1, cancel) ? null : reader.GetInt32(1), reader.GetInt32(2)));
        return new EdgeRun(rows, errors);
    }

    /// <summary>Where a value a claim gave breaks it: NULL where it claims never NULL, past a bound, or longer than its length.</summary>
    private static IEnumerable<string> Problems(CatalogueSweep.Outcome claim, EdgeValue row)
    {
        var (field, scalar) = (claim.Field!, (IrScalar)claim.Field!.Type);
        var at = row.Row is { } k ? $" (edge row {k.ToString(CultureInfo.InvariantCulture)} alone)" : "";
        if (row.Null)
        {
            if (field.NullableBecause.Count == 0)
                yield return $"{claim.Expression}{at}: claimed never null, and NULL";
            yield break;
        }

        if (ExactDecimal.TryParse(row.Text, out var value))
        {
            if (scalar.Facets?.Min is { } min && ExactDecimal.TryParse(min.Value, out var least) && !Bounds.Admits(new Bound(least, min.Inclusive), value, BoundSide.Lower))
                yield return $"{claim.Expression}{at}: {value} below {min.Value}";
            if (scalar.Facets?.Max is { } max && ExactDecimal.TryParse(max.Value, out var most) && !Bounds.Admits(new Bound(most, max.Inclusive), value, BoundSide.Upper))
                yield return $"{claim.Expression}{at}: {value} above {max.Value}";
        }

        if (scalar.Facets?.Length is { } length && row.Bytes >= 0 && row.Bytes / (length.Unit == IrLengthUnit.Utf16 ? 2 : 1) > length.Value)
            yield return $"{claim.Expression}{at}: {row.Bytes} bytes, longer than {length.Value}";
    }

    /// <summary>
    /// Checks the claims of a form that no edge row runs against their committed list, in
    /// <c>tests/Haruspex.DifferentialTests/Snapshots</c>; <c>HARUSPEX_ACCEPT=1</c> rewrites it.
    /// </summary>
    private static void MatchesSnapshot(string form, string actual)
    {
        var path = Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.DifferentialTests", "Snapshots", $"CatalogueSweepGridTests.{form}.unchecked.txt");
        if (Environment.GetEnvironmentVariable("HARUSPEX_ACCEPT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.True(File.Exists(path), $"{path} is missing; rerun with HARUSPEX_ACCEPT=1 to write it, and read it.");
        Assert.True(actual == File.ReadAllText(path), $"{path} is not what the server does; rerun with HARUSPEX_ACCEPT=1 and read the diff.\n{actual}");
    }

    /// <summary>
    /// Whether haruspex's type and the server's are one: the IR writes a column's type as declared (<c>float(24)</c>), and the
    /// server describes the type it is (<c>real</c>), so the two are compared as types, and as text only where either names none.
    /// </summary>
    private static bool SameType(string? haruspex, string server) =>
        ScalarType.Parse(haruspex) is { } a && ScalarType.Parse(server) is { } b ? a == b : string.Equals(haruspex, server, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an expression reads a column of a type inference doesn't model (binary, <c>xml</c>, <c>sql_variant</c>,
    /// <c>json</c>). Its value is unknown, so haruspex can't tell that the server refuses an expression over it, such as
    /// <c>ISNULL(i, x)</c>, whose second argument doesn't convert to int; when the first argument is never NULL it is that
    /// argument, which is right for every expression the server accepts. A refused one is in no procedure the server
    /// created, so what haruspex says of it claims nothing, and it is the one difference not counted.
    /// </summary>
    private static bool ReadsAnUnmodelledType(string expression) =>
        CatalogueSweep.Columns
            .Where(c => ScalarType.Parse(c.Type) is not { } type || !SqlTypes.IsModelled(type.Type))
            .Any(c => System.Text.RegularExpressions.Regex.IsMatch(expression, $@"\b{c.Name}\b"));

    /// <summary>
    /// The type SQL Server describes for each query's one column, or <c>!</c> and the error it gives: each described on its own,
    /// since an error fails its whole query, in one batch.
    /// </summary>
    private async Task<string[]> Describe(List<string> queries)
    {
        var batch = new StringBuilder("SET NOCOUNT ON; DECLARE @r TABLE (n int NOT NULL, t nvarchar(200) NULL, e int NULL);\n");
        for (var i = 0; i < queries.Count; i++)
        {
            var query = queries[i].Replace("'", "''", StringComparison.Ordinal);
            batch.Append(CultureInfo.InvariantCulture, $"INSERT INTO @r SELECT TOP (1) {i}, system_type_name, error_number FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;\n");
        }

        batch.Append("SELECT CONCAT(n, ' ', ISNULL(t, CONCAT('!', e))) AS c FROM @r ORDER BY n;");
        var outcome = await ObservedCall.Run(server, [CatalogueSweep.Table], batch.ToString());
        Assert.Null(outcome.Error);
        var types = new string[queries.Count];
        if (queries.Count == 0)
            return types;
        foreach (var line in outcome.Sets[0]["c: ".Length..].Split(';'))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            types[int.Parse(line[..space], CultureInfo.InvariantCulture)] = line[(space + 1)..];
        }

        return types;
    }
}

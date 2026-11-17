using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The database module end to end: every run of every fixture calls its procedure through the function haruspex generates for
/// it, through the mssql driver, as an application does, and the function must return without a failure. So the parameters'
/// schema accepts what the run sends, the driver's types bind it, and every result set and <c>OUTPUT</c> parameter passes its
/// schema (a <c>ResultError</c> otherwise).
/// </summary>
/// <remarks>
/// A run's arguments are written for SqlClient (<c>runs.json</c>, read by <c>RunFile</c>); here each is turned into the form the
/// parameters' schema takes: a date or time as the ISO 8601 string of its type's form, a <c>bigint</c> past 2^53 as a
/// JavaScript bigint. Runs without a setup only read, so a fixture's are made in one transaction; a run with a setup writes,
/// so it has its own.
/// </remarks>
public class GeneratedFunctionTests(SqlServerFixture server)
{
    public static TheoryData<string> Fixtures { get; } = [.. Runs.Fixtures];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Every_run_returns_through_its_generated_function(string fixture)
    {
        var document = Fixture.Named(fixture).Ir;
        var runs = Runs.All.Where(r => r.Fixture == fixture).ToList();
        var batches = new List<(IReadOnlyList<string> Setup, List<ProcedureRun> Runs)> { ([], [.. runs.Where(r => r.Setup is null)]) };
        batches.AddRange(runs.Where(r => r.Setup is not null).Select(r => ((IReadOnlyList<string>)[r.Setup!], new List<ProcedureRun> { r })));

        var failed = new List<string>();
        foreach (var (setup, batch) in batches.Where(b => b.Runs.Count > 0))
        {
            var calls = batch.Select(r => Call(document.Objects.Single(o => o.Id == r.Procedure), r)).ToList();
            var responses = await GeneratedFunctions.RunAsync(server, document, setup, calls, TestContext.Current.CancellationToken);
            failed.AddRange(responses.Where(r => r.Error is not null).Select(r => $"{r.Name}: {r.Error!.Name}: {r.Error.Message}"));
        }

        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    /// <summary>The deployed fixtures that hold query files.</summary>
    public static TheoryData<string> QueryFixtures { get; } = [.. Runs.Fixtures.Where(f => Fixture.Named(f).QueryIr is not null)];

    /// <summary>
    /// Every run of a fixture's query file calls its function, which sends the file's text through <c>sp_executesql</c> and
    /// checks what comes back with the query's schema: a <c>ResultError</c> is a claim the server broke. The runs only read, so
    /// a fixture's are made in one transaction.
    /// </summary>
    [Theory]
    [MemberData(nameof(QueryFixtures))]
    public async Task Every_run_of_a_query_file_returns_through_its_generated_function(string fixture)
    {
        var document = Fixture.Named(fixture).QueryIr!;
        var calls = Runs.Queries
            .Where(r => r.Fixture == fixture)
            .Select(r => new GeneratedFunctions.Call(
                r.Name,
                MssqlEmitter.FunctionName(document.Objects.Single(o => o.Id == r.Query).Name),
                r.Parameters.ToDictionary(p => p.Key.TrimStart('@'), p => (object?)p.Value)))
            .ToList();

        var responses = await GeneratedFunctions.RunAsync(server, document, [], calls, TestContext.Current.CancellationToken);

        var failed = responses.Where(r => r.Error is not null).Select(r => $"{r.Name}: {r.Error!.Name}: {r.Error.Message}").ToList();
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    [Theory]
    [MemberData(nameof(QueryFixtures))]
    public void Every_query_file_of_a_fixture_is_run(string fixture)
    {
        var run = Runs.Queries.Where(r => r.Fixture == fixture).Select(r => r.Query).ToHashSet(StringComparer.Ordinal);

        var notRun = Fixture.Named(fixture).QueryIr!.Objects.Select(o => o.Id).Where(q => !run.Contains(q)).ToList();

        Assert.True(notRun.Count == 0, "No run in runs.json calls these query files: " + string.Join(", ", notRun));
    }

    private static GeneratedFunctions.Call Call(IrObject procedure, ProcedureRun run) =>
        new(
            run.Name,
            MssqlEmitter.FunctionName(procedure.Name),
            procedure.Parameters.Count == 0 ? null : run.Parameters.ToDictionary(p => p.Key.TrimStart('@'), p => Argument(procedure.Parameters.Single(d => d.Name.Equals(p.Key, StringComparison.OrdinalIgnoreCase)).Type, p.Value)));

    /// <summary>A run's argument in the form the parameters' schema takes for <paramref name="type"/>.</summary>
    private static object? Argument(IrType type, object? value) => value switch
    {
        null => null,
        DateTime at when type is IrScalar { Kind: IrScalarKind.Date } => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime at => at.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
        TimeSpan time => time.ToString(@"hh\:mm\:ss\.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
        DateTimeOffset at => at.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.') + at.ToString("zzz", CultureInfo.InvariantCulture),
        long big when Math.Abs((decimal)big) > 9007199254740991m => new MssqlDriver.BigIntValue(big.ToString(CultureInfo.InvariantCulture)),
        decimal exact => (double)exact,
        Guid id => id.ToString(),
        _ => value,
    };
}

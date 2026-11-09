using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Runs every fixture procedure in SQL Server and checks the real output against the IR inferred for it. A
/// failure is a soundness bug in the inferencer (or a wrong claim in the IR), never something to accept.
/// </summary>
public class DifferentialTests(SqlServerFixture server)
{
    public static TheoryData<string> RunNames { get; } = [.. Runs.All.Select(r => r.Name)];

    public static TheoryData<string> Fixtures { get; } = [.. Runs.Fixtures];

    [Theory]
    [MemberData(nameof(RunNames))]
    public async Task The_output_keeps_every_claim_of_the_ir(string run)
    {
        server.Require();

        var report = await server.ValidateAsync(Runs.Named(run));

        Assert.True(report.Violations.Count == 0, "The output broke claims of the IR:\n" + string.Join("\n", report.Violations));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task The_seed_reaches_every_case_the_ir_allows(string fixture)
    {
        server.Require();

        var seen = new Dictionary<string, IReadOnlySet<(string, Seen)>>(StringComparer.Ordinal);
        foreach (var run in Runs.All.Where(r => r.Fixture == fixture))
        {
            var report = await server.ValidateAsync(run);
            var into = seen.TryGetValue(run.Procedure, out var s) ? new HashSet<(string, Seen)>(s) : [];
            into.UnionWith(report.Seen);
            seen[run.Procedure] = into;
        }

        // A procedure that is not run (Runs.NotRun says why) is not counted as missing every case.
        var missing = Coverage.Missing(server.Ir(fixture), seen)
            .Where(m => !Runs.NotRun.Keys.Any(p => m.StartsWith(p + " ", StringComparison.Ordinal)))
            .ToList();
        Assert.True(missing.Count == 0, "No run saw these cases, so the claims around them are untested. Extend the seed or add a run:\n" + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_procedure_is_run_or_excluded_with_a_reason(string fixture)
    {
        var procedures = server.Ir(fixture).Objects.Select(o => o.Id);
        var run = Runs.All.Where(r => r.Fixture == fixture).Select(r => r.Procedure).ToHashSet(StringComparer.Ordinal);

        var neither = procedures.Where(p => !run.Contains(p) && !Runs.NotRun.ContainsKey(p)).ToList();
        var both = Runs.NotRun.Keys.Where(run.Contains).ToList();

        Assert.Empty(neither);
        Assert.Empty(both);
    }
}

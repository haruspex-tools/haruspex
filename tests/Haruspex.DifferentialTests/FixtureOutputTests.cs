using System.Data;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Runs every fixture procedure in SQL Server and checks the real output against the IR inferred for it. A
/// failure is a soundness bug in the inferencer (or a wrong claim in the IR), never something to accept.
/// </summary>
public class FixtureOutputTests(SqlServerFixture server)
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
        var missing = Coverage.Missing(Fixture.Named(fixture).Ir, seen)
            .Where(m => !Runs.NotRun.Keys.Any(p => m.StartsWith(p + " ", StringComparison.Ordinal)))
            .ToList();
        Assert.True(missing.Count == 0, "No run saw these cases, so the claims around them are untested. Extend the seed or add a run:\n" + string.Join("\n", missing));
    }

    public static TheoryData<string> EdgeFixtures { get; } = [.. Runs.EdgeFixtures];

    [Theory]
    [MemberData(nameof(EdgeFixtures))]
    public async Task The_seed_reaches_every_edge_the_ir_claims(string fixture)
    {
        server.Require();

        var reached = new Dictionary<string, IReadOnlySet<(string, string)>>(StringComparer.Ordinal);
        foreach (var run in Runs.All.Where(r => r.Fixture == fixture))
        {
            var report = await server.ValidateAsync(run);
            var into = reached.TryGetValue(run.Procedure, out var r) ? new HashSet<(string, string)>(r) : [];
            into.UnionWith(report.Edges);
            reached[run.Procedure] = into;
        }

        var missing = Coverage.MissingEdges(Fixture.Named(fixture).Ir, reached)
            .Where(m => !Runs.NotRun.Keys.Any(p => m.StartsWith(p + " ", StringComparison.Ordinal)))
            .ToList();
        Assert.True(missing.Count == 0, "No run reached these edges, so the facets there are untested. Extend the seed:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// An untrusted constraint guarantees nothing, and the fixture proves it: ck_ticket_muted is disabled by NOCHECK
    /// and ck_ticket_late added WITH NOCHECK after a row that breaks it, and the data really does break both, while the
    /// IR claims only the columns' type range. ck_ticket_restored, validated again WITH CHECK, is claimed and holds.
    /// </summary>
    [Fact]
    public async Task An_untrusted_constraint_claims_nothing_and_the_data_breaks_it()
    {
        server.Require();
        var fields = Fixture.Named("checks").Ir.Types["ListTicketsRow"].Fields;
        IrFacets Facets(string name) => Assert.IsType<IrScalar>(fields.Single(f => f.Name == name).Type).Facets!;

        foreach (var untrusted in new[] { "muted", "late" })
        {
            Assert.Equal(new IrFactSource(IrFactSourceKind.Type, "int"), Facets(untrusted).Min!.From);
            Assert.Equal("-2147483648", Facets(untrusted).Min!.Value);
        }

        Assert.Equal(new IrBound("0", false, new IrFactSource(IrFactSourceKind.Check, "chk.ck_ticket_restored")), Facets("restored").Min);

        var rows = Assert.Single(await server.ObserveAsync(Runs.Named("checks: ticket_rows"))).Rows;
        var columns = Assert.Single(await server.ObserveAsync(Runs.Named("checks: ticket_rows"))).Columns;
        int? Least(string column) => rows.Min(r => (int?)r[columns.ToList().IndexOf(column)]);
        Assert.True(Least("muted") <= 0, "The seed should break ck_ticket_muted.");
        Assert.True(Least("late") <= 0, "ticket_late.sql should break ck_ticket_late.");
        Assert.True(Least("restored") > 0);
    }

    /// <summary>
    /// A run's argument is sent as the type its parameter declares (<see cref="RunArgument"/>), so the earliest day reaches a
    /// <c>date</c> and the seventh fractional digit a <c>datetime2(7)</c>. Sent as <c>datetime</c>, the first is refused before
    /// it leaves the driver, and the second rounded to <c>.1233333</c>.
    /// </summary>
    [Fact]
    public async Task A_run_s_date_argument_reaches_the_procedure_as_written()
    {
        server.Require();
        var run = Runs.Named("till: shift_times, the edges");
        Assert.Equal(SqlDbType.Date, run.Parameters["@day"].Parameter("@day").SqlDbType);

        var set = Assert.Single(await server.ObserveAsync(run));
        var row = Assert.Single(set.Rows);
        object? Column(string name) => row[set.Columns.ToList().IndexOf(name)];
        Assert.Equal(DateTime.MinValue, Column("day"));
        Assert.Equal(DateTime.MinValue, Column("opened_at"));
        Assert.Equal(new DateTime(2026, 11, 16, 8, 30, 0).AddTicks(1_234_567), Column("clocked_at"));
    }

    [Fact]
    public void Earlier_runs_left_no_database_behind()
    {
        server.Require();

        // Reported as a skip, which the test output prints with its reason at any verbosity (xunit's warnings are
        // not shown by `dotnet test`). Not a failure: the database may belong to another run going on now against
        // the same server.
        if (server.RecentLeftovers.Count > 0)
        {
            Assert.Skip(
                $"Warning: the server has test databases from other runs: {string.Join(", ", server.RecentLeftovers)}. If no other run is going on, an earlier one was killed (SIGKILL) before it could drop them. They are dropped automatically once a day old, or drop them by hand.");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_procedure_is_run_or_excluded_with_a_reason(string fixture)
    {
        var procedures = Fixture.Named(fixture).Ir.Objects.Select(o => o.Id);
        var run = Runs.All.Where(r => r.Fixture == fixture).Select(r => r.Procedure).ToHashSet(StringComparer.Ordinal);

        var neither = procedures.Where(p => !run.Contains(p) && !Runs.NotRun.ContainsKey(p)).ToList();
        var both = Runs.NotRun.Keys.Where(run.Contains).ToList();

        Assert.Empty(neither);
        Assert.Empty(both);
    }
}

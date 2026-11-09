using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests;

/// <summary>
/// One snapshot of the IR and one of the Zod per fixture procedure. A snapshot is a claim about what
/// SQL Server returns, so a change to one is reviewed against the engine's behaviour, not just accepted.
/// </summary>
public class FixtureSnapshotTests
{
    private static readonly Dictionary<string, Lazy<SqlProject>> Fixtures = new(StringComparer.Ordinal)
    {
        ["orders"] = new(() => SqlProject.Load(TestPaths.Fixture("orders"))),
        ["values"] = new(() => SqlProject.Load(TestPaths.Fixture("values"))),
        ["checks"] = new(() => SqlProject.Load(TestPaths.Fixture("checks"))),
        ["payloads"] = new(() => SqlProject.Load(TestPaths.Fixture("payloads"))),
    };

    /// <summary>Every fixture procedure, with its fixture. Procedure ids are unique across fixtures.</summary>
    private static readonly Dictionary<string, string> FixtureOf = new(StringComparer.Ordinal)
    {
        ["sales.list_customers"] = "orders",
        ["sales.list_orders"] = "orders",
        ["sales.get_order"] = "orders",
        ["sales.list_customer_orders"] = "orders",
        ["sales.order_report"] = "orders",
        ["sales.archive_orders"] = "orders",
        ["vals.list_samples"] = "values",
        ["vals.list_samples_with_nulls"] = "values",
        ["vals.get_sample"] = "values",
        ["vals.sample_rows"] = "values",
        ["vals.list_nested"] = "values",
        ["vals.list_nested_with_nulls"] = "values",
        ["chk.list_tickets"] = "checks",
        ["chk.ticket_rows"] = "checks",
        ["pay.get_author"] = "payloads",
        ["pay.list_books"] = "payloads",
        ["pay.author_stats"] = "payloads",
        ["pay.reviews_by_book"] = "payloads",
        ["pay.author_documents"] = "payloads",
        ["pay.review_digest"] = "payloads",
    };

    public static TheoryData<string> Procedures { get; } = [.. FixtureOf.Keys];

    public static TheoryData<string> FixtureNames { get; } = [.. Fixtures.Keys];

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void The_fixture_loads_without_diagnostics(string fixture) => Assert.Empty(Fixtures[fixture].Value.Diagnostics);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Every_fixture_procedure_has_snapshots(string fixture) =>
        Assert.Equal(
            FixtureOf.Where(p => p.Value == fixture).Select(p => p.Key).Order(StringComparer.Ordinal),
            Fixtures[fixture].Value.Procedures.Select(p => p.ToString()).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Procedures))]
    public void Ir(string procedure) =>
        Snapshot.Match(IrJsonWriter.Write(Infer(procedure)), $"{nameof(FixtureSnapshotTests)}.{nameof(Ir)}_procedure={procedure}", "json");

    [Theory]
    [MemberData(nameof(Procedures))]
    public void Zod(string procedure) =>
        Snapshot.Match(ZodEmitter.Emit(Infer(procedure)), $"{nameof(FixtureSnapshotTests)}.{nameof(Zod)}_procedure={procedure}", "ts");

    private static IrDocument Infer(string procedure)
    {
        var project = Fixtures[FixtureOf[procedure]].Value;
        var selection = project.Select([ObjectPattern.Parse(procedure)]);
        Assert.Empty(selection.Unmatched);
        return project.Infer(selection.Procedures);
    }
}

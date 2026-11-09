using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests;

/// <summary>
/// One snapshot of the IR and one of the Zod per fixture procedure. A snapshot is a claim about what
/// SQL Server returns, so a change to one is reviewed against the engine's behaviour, not just accepted.
/// </summary>
public class FixtureSnapshotTests
{
    private static readonly Lazy<SqlProject> Orders = new(() => SqlProject.Load(TestPaths.Fixture("orders")));

    public static TheoryData<string> Procedures { get; } = new()
    {
        "sales.list_customers",
        "sales.list_orders",
        "sales.get_order",
        "sales.list_customer_orders",
        "sales.order_report",
        "sales.archive_orders",
    };

    [Fact]
    public void The_fixture_loads_without_diagnostics() => Assert.Empty(Orders.Value.Diagnostics);

    [Fact]
    public void Every_fixture_procedure_has_snapshots() =>
        Assert.Equal(
            Procedures.Select(p => (string)p.Data).Order(StringComparer.Ordinal),
            Orders.Value.Procedures.Select(p => p.ToString()).Order(StringComparer.Ordinal));

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
        var selection = Orders.Value.Select([ObjectPattern.Parse(procedure)]);
        Assert.Empty(selection.Unmatched);
        return Orders.Value.Infer(selection.Procedures);
    }
}

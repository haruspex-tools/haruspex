using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Inference;

/// <summary>
/// What haruspex infers for every built-in type through every operator of the sweep (<see cref="CatalogueSweep"/>), one
/// snapshot per operator: each expression's type, why it may be null and its type's facets, or the unknown it gives. A change
/// to any type's entry, or to a rule over it, changes a line here, even where no fixture reaches it. The differential tests'
/// <c>CatalogueSweepGridTests</c> checks each typed line's type against SQL Server.
/// </summary>
public class CatalogueSweepTests
{
    public static TheoryData<string> Forms { get; } = [.. CatalogueSweep.Forms];

    [Theory]
    [MemberData(nameof(Forms))]
    public void Every_type_through_the_operator_gives_what_the_snapshot_holds(string form) =>
        Snapshot.Match(string.Join("\n", CatalogueSweep.Infer(form)) + "\n", $"CatalogueSweepTests.{form}", "txt");
}

namespace Haruspex.Tests.Snapshotting;

/// <summary>
/// Asserts that a text matches its committed snapshot: in <c>tests/Haruspex.Tests/Snapshots</c> for a test's own output,
/// or in a fixture's <c>expected/</c> folder for what haruspex must produce from that fixture.
/// </summary>
internal static class Snapshot
{
    // Located from the repository root rather than [CallerFilePath], which CI builds map to /_/.
    private static readonly SnapshotStore Store =
        SnapshotStore.FromEnvironment(Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.Tests", "Snapshots"));

    /// <param name="actual">The text to check.</param>
    /// <param name="name">The snapshot's name, conventionally <c>Class.Method</c> plus any parameters.</param>
    /// <param name="extension">The file extension, so the snapshot opens with the right highlighting.</param>
    public static void Match(string actual, string name, string extension) => Assert(Store.Check(name, extension, actual));

    /// <summary>Checks a snapshot kept in <paramref name="directory"/>, such as a fixture's <c>expected/</c> folder.</summary>
    /// <param name="actual">The text to check.</param>
    /// <param name="directory">The folder holding the snapshot.</param>
    /// <param name="name">The snapshot's name, which says what it holds, such as <c>sales.get_order.ir</c>.</param>
    /// <param name="extension">The file extension, so the snapshot opens with the right highlighting.</param>
    public static void Match(string actual, string directory, string name, string extension) =>
        Assert(SnapshotStore.FromEnvironment(directory).Check(name, extension, actual));

    private static void Assert(SnapshotOutcome outcome)
    {
        if (!outcome.Passed)
            Xunit.Assert.Fail(outcome.Message);
    }
}

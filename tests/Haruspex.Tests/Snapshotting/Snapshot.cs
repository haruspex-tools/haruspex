namespace Haruspex.Tests.Snapshotting;

/// <summary>Asserts that a text matches its committed snapshot in <c>tests/Haruspex.Tests/Snapshots</c>.</summary>
internal static class Snapshot
{
    // Located from the repository root rather than [CallerFilePath], which CI builds map to /_/.
    private static readonly SnapshotStore Store =
        SnapshotStore.FromEnvironment(Path.Combine(TestPaths.RepoRoot, "tests", "Haruspex.Tests", "Snapshots"));

    /// <param name="actual">The text to check.</param>
    /// <param name="name">The snapshot's name, conventionally <c>Class.Method</c> plus any parameters.</param>
    /// <param name="extension">The file extension, so the snapshot opens with the right highlighting.</param>
    public static void Match(string actual, string name, string extension)
    {
        var outcome = Store.Check(name, extension, actual);
        if (!outcome.Passed)
            Assert.Fail(outcome.Message);
    }
}

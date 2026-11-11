namespace Haruspex.Tests;

/// <summary>Locates the repository from the test binaries, so tests read fixtures in place.</summary>
internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Fixture(string name) => Path.Combine(RepoRoot, "tests", "fixtures", name);

    public static string EmitCheck => Path.Combine(RepoRoot, "tests", "emit-check");

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Haruspex.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException($"Haruspex.slnx not found above {AppContext.BaseDirectory}.");
    }
}

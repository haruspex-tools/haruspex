namespace Haruspex.DifferentialTests;

/// <summary>Locates the repository from the test binaries, so fixtures and seeds are read in place.</summary>
internal static class Paths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Fixture(string name) => Path.Combine(RepoRoot, "tests", "fixtures", name);

    public static string Seed(string fixture) => Path.Combine(RepoRoot, "tests", "Haruspex.DifferentialTests", "Seeds", fixture + ".sql");

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

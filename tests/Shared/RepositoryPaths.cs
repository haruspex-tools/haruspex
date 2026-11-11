namespace Haruspex.TestSupport;

/// <summary>
/// Locates the repository from the test binaries, so tests read fixtures, documents and the emit-check package in place
/// rather than copies in the build output.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>The folder holding <c>Haruspex.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The folder of the fixtures, one folder each (<c>tests/fixtures/README.md</c>).</summary>
    public static string Fixtures => Path.Combine(Root, "tests", "fixtures");

    /// <summary>The npm package that compiles the generated Zod and runs real output through it.</summary>
    public static string EmitCheck => Path.Combine(Root, "tests", "emit-check");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Haruspex.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException($"Haruspex.slnx not found above {AppContext.BaseDirectory}.");
    }
}

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

/// <summary>A directory under the system temp folder, deleted when disposed.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "haruspex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string relativePath, string contents)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, contents);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail a test.
        }
    }
}

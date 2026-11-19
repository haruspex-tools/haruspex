using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex generate</c> and <c>haruspex check</c> on a config file that splits its output, end to end on the market fixture
/// (<c>tests/fixtures/market</c>, <c>"split": "schema"</c>): a module's files and the shared one are written, said and compared
/// as one module's are (docs/decisions/0074-a-config-may-split-its-output-into-modules.md).
/// </summary>
public class SplitCliTests
{
    private static string Market => Fixture.Named("market").Folder;

    /// <summary>A run in the fixture's folder: its exit code, and what it said, standard output and error together.</summary>
    private static (int ExitCode, string Said) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, Market);
        return (exitCode, (output.ToString() + error).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Generate_says_how_many_files_it_wrote_and_the_modules_rather_than_every_file()
    {
        using var outDir = new TempDirectory();

        var (exitCode, said) = Run("generate", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("Wrote 15 files to ", said, StringComparison.Ordinal);
        Assert.Contains(": the IR and the modules admin, queries, sales and shop, with what they share.\n", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_passes_on_what_generate_wrote_and_fails_naming_a_shared_file_that_was_edited()
    {
        using var outDir = new TempDirectory();
        Assert.Equal(0, Run("generate", "--out", outDir.Path).ExitCode);
        Assert.Equal(0, Run("check", "--out", outDir.Path).ExitCode);

        var shared = Path.Combine(outDir.Path, "shared.zod.ts");
        File.WriteAllText(shared, File.ReadAllText(shared).Replace("@internal", "@public", StringComparison.Ordinal));
        var (exitCode, said) = Run("check", "--out", outDir.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("shared.zod.ts: error HAR6", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_when_a_module_s_file_is_missing()
    {
        using var outDir = new TempDirectory();
        Assert.Equal(0, Run("generate", "--out", outDir.Path).ExitCode);
        File.Delete(Path.Combine(outDir.Path, "admin.mssql.ts"));

        var (exitCode, said) = Run("check", "--out", outDir.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("admin.mssql.ts isn't there", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_without_the_split_writes_one_module_with_every_name_the_split_gives()
    {
        using var copy = new TempDirectory();
        foreach (var file in Directory.GetFiles(Market, "*.sql", SearchOption.AllDirectories))
            copy.File(Path.GetRelativePath(Market, file), File.ReadAllText(file));
        copy.File("haruspex.json", File.ReadAllText(Path.Combine(Market, "haruspex.json")).Replace("\"split\": \"schema\",", "", StringComparison.Ordinal));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HaruspexCli.Run(["generate", "--out", "out"], output, error, copy.Path);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(copy.Path, "out", "haruspex.zod.ts")));
        // The query file's -- @module comment does nothing without the split, which a warning says.
        Assert.Contains("warning HAR4009: -- @module shop:", error.ToString(), StringComparison.Ordinal);
        static string Exports(string text) => string.Join("\n", text.Split('\n').Where(l => l.StartsWith("export type ", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        var split = string.Join("\n", Directory.GetFiles(Fixture.Named("market").Expected, "*.zod.verified.ts").SelectMany(f => File.ReadAllText(f).Split('\n')));
        Assert.Equal(Exports(File.ReadAllText(Path.Combine(copy.Path, "out", "haruspex.zod.ts"))), Exports(split));
    }
}

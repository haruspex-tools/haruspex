using Haruspex.Cli;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests;

public class CliTests
{
    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void Generate_writes_the_ir_and_the_zod_module()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run("generate", TestPaths.Fixture("orders"), "--include", "sales.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Contains("6 procedures", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(outDir.Path, "haruspex.ir.json")));
        Snapshot.Match(
            File.ReadAllText(Path.Combine(outDir.Path, "haruspex.zod.ts")),
            $"{nameof(CliTests)}.{nameof(Generate_writes_the_ir_and_the_zod_module)}",
            "ts");
    }

    [Fact]
    public void Generate_accepts_several_include_patterns()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, _) = Run(
            "generate", TestPaths.Fixture("orders"), "--include", "sales.get_order", "-i", "sales.list_*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("4 procedures", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_directory_is_a_usage_error()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run("generate", Path.Combine(outDir.Path, "nope"), "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Contains("not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_include_pattern_that_matches_nothing_is_an_error()
    {
        using var outDir = new TempDirectory();

        var (exitCode, _, error) = Run("generate", TestPaths.Fixture("orders"), "--include", "sales.get_ordr", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Contains("sales.get_ordr", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir.Path));
    }

    [Fact]
    public void A_malformed_include_pattern_is_a_usage_error()
    {
        using var outDir = new TempDirectory();

        var (exitCode, _, error) = Run("generate", TestPaths.Fixture("orders"), "--include", "get_order", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Contains("get_order", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_required_options_are_a_usage_error()
    {
        var (exitCode, output, error) = Run("generate", TestPaths.Fixture("orders"));

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Contains("--include", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parse_error_fails_with_its_location_and_writes_nothing()
    {
        using var source = new TempDirectory();
        using var outDir = new TempDirectory();
        source.File("bad.sql", "CREATE PROCEDURE dbo.p AS\nSELECT FROM;\n");

        var (exitCode, _, error) = Run("generate", source.Path, "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("bad.sql:2:", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir.Path));
    }

    [Fact]
    public void Help_succeeds()
    {
        var (exitCode, output, _) = Run("generate", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--include", output, StringComparison.Ordinal);
    }
}

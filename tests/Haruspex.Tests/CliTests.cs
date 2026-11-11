using Haruspex.Cli;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests;

public class CliTests
{
    /// <summary>Runs the CLI from an empty folder, so no haruspex.json above the test binaries can be found.</summary>
    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var cwd = new TempDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, cwd.Path);
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
    public void Without_a_config_file_out_is_required()
    {
        var (exitCode, output, error) = Run("generate", TestPaths.Fixture("orders"), "--include", "*.*");

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal("haruspex: give --out, the folder to write to, or a config file with \"out\".\n", error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Without_a_config_file_include_is_required()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run("generate", TestPaths.Fixture("orders"), "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal("haruspex: nothing is selected: give --include, or a config file whose \"schemas\" include procedures. Use '*.*' to type every procedure.\n", error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Without_a_config_file_the_folder_is_required()
    {
        using var outDir = new TempDirectory();

        var (exitCode, _, error) = Run("generate", "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal("haruspex: give the folder of .sql files to read, or a config file with \"source\".\n", error.ReplaceLineEndings("\n"));
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
    public void A_write_to_a_table_outside_the_project_is_a_warning_with_its_location_and_the_output_is_written()
    {
        // A trigger on it could return result sets, which haruspex cannot see (slice 6d). Once per table.
        using var source = new TempDirectory();
        using var outDir = new TempDirectory();
        source.File("p.sql", "CREATE PROCEDURE dbo.p AS\nINSERT INTO audit.trail (what) VALUES (1);\nDELETE FROM audit.trail;\n");

        var (exitCode, _, error) = Run("generate", source.Path, "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Single(error.Split('\n'), l => l.Contains("warning:", StringComparison.Ordinal));
        Assert.Contains("p.sql:2:1: warning: audit.trail is written here but is not defined in the project", error, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(outDir.Path));
    }

    [Fact]
    public void Generate_help_succeeds_and_lists_its_options()
    {
        var (exitCode, output, _) = Run("generate", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--include", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_config_file_a_clash_is_the_command_line_s_error_and_says_where_settings_go()
    {
        using var source = new TempDirectory();
        using var outDir = new TempDirectory();
        source.File("p.sql", "CREATE PROCEDURE dbo.person AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE [dbo].[person#] AS SELECT 2 AS a;\nGO\n");

        var (exitCode, _, error) = Run("generate", source.Path, "--include", "dbo.*", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "haruspex: error: \"Person\" would name both the procedure dbo.person and the procedure dbo.person#. Generated names must not clash: add a naming \"replace\" rule that tells their names apart (such as { \"pattern\": \"#$\", \"with\": \"Row\" }), or give one of them its own name with a \"name\" override in \"procedures\". These settings go in a haruspex.json beside the project, which it doesn't have yet.\n",
            error.ReplaceLineEndings("\n"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir.Path));
    }
}

using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.Tests.Snapshotting;
using Haruspex.Tests.Sources;

namespace Haruspex.Tests.Cli;

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

    /// <summary>As <c>Run</c>, from <paramref name="workingDirectory"/>, where a config file is looked for.</summary>
    private static (int ExitCode, string Output, string Error) RunIn(string workingDirectory, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, workingDirectory);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void Generate_writes_the_ir_and_the_zod_module()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run("generate", Fixture.Named("orders").Database, "--include", "sales.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.All(error.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'), line => Assert.Contains(": note HAR", line, StringComparison.Ordinal));
        Assert.Contains("6 procedures", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(outDir.Path, "haruspex.ir.json")));
        Snapshot.Match(
            File.ReadAllText(Path.Combine(outDir.Path, "haruspex.zod.ts")),
            $"{nameof(CliTests)}.{nameof(Generate_writes_the_ir_and_the_zod_module)}",
            "ts");
    }

    [Fact]
    public void Generate_reads_a_dacpac_and_records_it_and_its_references_in_the_ir()
    {
        using var outDir = new TempDirectory();
        var package = Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "harbour", "harbour.dacpac");

        var (exitCode, output, error) = Run("generate", package, "--include", "dock.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("warning HAR1003", error, StringComparison.Ordinal);
        Assert.Contains("3 procedures", output, StringComparison.Ordinal);
        var source = IrJsonReader.Read(File.ReadAllText(Path.Combine(outDir.Path, "haruspex.ir.json"))).Source;
        Assert.Equal(new IrPackage("harbour.dacpac", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package)))), source.Package);
        Assert.Equal(["crane.dacpac", "master.dacpac", "quay.dacpac", "tally.dacpac"], source.References.Select(r => r.Name));
        Assert.Null(source.References.Single(r => r.Name == "master.dacpac").Sha256);
    }

    [Fact]
    public void Generate_reads_a_dacpac_named_as_the_config_file_s_source_with_its_references_folders()
    {
        using var dir = new TempDirectory();
        var packages = Path.Combine(RepositoryPaths.Root, "tests", "dacpacs", "harbour");
        Directory.CreateDirectory(Path.Combine(dir.Path, "app"));
        File.Copy(Path.Combine(packages, "harbour.dacpac"), Path.Combine(dir.Path, "app", "harbour.dacpac"));
        dir.File("haruspex.json", $$"""{ "source": "app/harbour.dacpac", "references": [{{System.Text.Json.JsonSerializer.Serialize(packages)}}], "out": "out", "schemas": { "dock": { "include": ["*"] } } }""");

        var (exitCode, _, error) = RunIn(dir.Path, "generate");

        Assert.Equal(0, exitCode);
        Assert.Single(error.Split('\n'), line => line.Contains("HAR1003", StringComparison.Ordinal));
    }

    [Fact]
    public void References_beside_a_folder_of_scripts_are_a_warning_since_a_folder_has_none_to_find()
    {
        using var dir = new TempDirectory();
        dir.File("db/p.sql", "CREATE PROCEDURE dbo.p AS SELECT 1 AS one;");
        dir.File("haruspex.json", """{ "source": "db", "references": ["db"], "out": "out", "schemas": { "dbo": { "include": ["*"] } } }""");

        var (exitCode, _, error) = RunIn(dir.Path, "generate");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning HAR6009", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_problem_in_a_referenced_package_is_printed_under_the_folder_that_package_was_found_in()
    {
        // The view is in lib.dacpac, found in shared/ through "references"; what it reads isn't there, so the note about the
        // procedure that reads it is placed in the view, and its path must be shared/lib.dacpac/…, not app/lib.dacpac/….
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "app"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "shared"));
        TestPackage.Write(Path.Combine(dir.Path, "shared", "lib.dacpac"), TestPackage.View("dbo", "v", "SELECT m.id FROM dbo.missing AS m;"));
        TestPackage.Write(Path.Combine(dir.Path, "app", "app.dacpac"), TestPackage.Procedure("dbo", "p", "SELECT v.id FROM dbo.v AS v;"), references: new TestPackage.Reference("lib.dacpac"));
        dir.File("haruspex.json", """{ "source": "app/app.dacpac", "references": ["shared"], "out": "out", "schemas": { "dbo": { "include": ["*"] } } }""");

        var (exitCode, _, error) = RunIn(dir.Path, "generate");

        Assert.Equal(0, exitCode);
        Assert.Contains(DiagnosticFormatter.PathAsShown(Path.Join(dir.Path, "shared", "lib.dacpac/[dbo].[v]:2:")), error, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Join(dir.Path, "app", "lib.dacpac"), error, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_refuses_a_dacpac_that_is_not_there()
    {
        var (exitCode, _, error) = Run("generate", "missing.dacpac", "--include", "*.*", "--out", "out");

        Assert.Equal(2, exitCode);
        Assert.Contains("file not found: missing.dacpac", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_accepts_several_include_patterns()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, _) = Run(
            "generate", Fixture.Named("orders").Database, "--include", "sales.get_order", "-i", "sales.list_*", "--out", outDir.Path);

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
        Assert.StartsWith("haruspex: error HAR6004: directory not found: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_haruspex_does_not_know_is_a_usage_error_with_its_code()
    {
        var (exitCode, output, error) = Run("generate", "--colour", "blue");

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.StartsWith("haruspex: error HAR6001: ", error, StringComparison.Ordinal);
        Assert.EndsWith("Run 'haruspex --help' for usage.\n", error.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void An_include_that_is_not_a_two_part_name_is_a_usage_error_with_its_code()
    {
        using var outDir = new TempDirectory();

        var (exitCode, _, error) = Run("generate", Fixture.Named("orders").Database, "--include", "a.b.c", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "haruspex: error HAR6006: invalid --include pattern: 'a.b.c' is not a two-part name such as sales.get_order, sales.* or *.list_*.\n",
            error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void An_include_pattern_that_matches_nothing_is_an_error()
    {
        using var outDir = new TempDirectory();

        var (exitCode, _, error) = Run("generate", Fixture.Named("orders").Database, "--include", "sales.get_ordr", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Contains("sales.get_ordr", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir.Path));
    }

    [Fact]
    public void A_malformed_include_pattern_is_a_usage_error()
    {
        using var outDir = new TempDirectory();

        var (exitCode, _, error) = Run("generate", Fixture.Named("orders").Database, "--include", "get_order", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Contains("get_order", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_config_file_out_is_required()
    {
        var (exitCode, output, error) = Run("generate", Fixture.Named("orders").Database, "--include", "*.*");

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal("haruspex: error HAR6005: give --out, the folder to write to, or a config file with \"out\".\n", error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Without_a_config_file_include_is_required()
    {
        using var outDir = new TempDirectory();

        var (exitCode, output, error) = Run("generate", Fixture.Named("orders").Database, "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal("haruspex: error HAR6007: nothing is selected: give --include, or a config file whose \"schemas\" include procedures. Use '*.*' to type every procedure.\n", error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Without_a_config_file_the_folder_is_required()
    {
        using var outDir = new TempDirectory();

        var (exitCode, _, error) = Run("generate", "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(2, exitCode);
        Assert.Equal("haruspex: error HAR6003: give the folder of .sql files, the .sqlproj or the .dacpac to read, or a config file with \"source\".\n", error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Problems_with_independent_settings_are_all_reported_not_only_the_first()
    {
        var (exitCode, output, error) = Run("generate", "--include", "a.b.c");

        Assert.Equal(2, exitCode);
        Assert.Equal("", output);
        Assert.Equal(
            "haruspex: error HAR6003: give the folder of .sql files, the .sqlproj or the .dacpac to read, or a config file with \"source\".\n"
            + "haruspex: error HAR6005: give --out, the folder to write to, or a config file with \"out\".\n"
            + "haruspex: error HAR6006: invalid --include pattern: 'a.b.c' is not a two-part name such as sales.get_order, sales.* or *.list_*.\n",
            error.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_check_that_needs_a_value_another_step_failed_to_give_is_not_run()
    {
        // The includes are malformed, so whether anything is selected can't be known; saying nothing is would be a cascade.
        using var outDir = new TempDirectory();

        var (_, _, error) = Run("generate", Fixture.Named("orders").Database, "--include", "a.b.c", "--out", outDir.Path);

        Assert.DoesNotContain("HAR6007", error, StringComparison.Ordinal);
        Assert.Single(error.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public void Each_unknown_is_listed_as_a_note_with_its_code_where_it_is_and_why()
    {
        using var source = new TempDirectory();
        using var outDir = new TempDirectory();
        source.File("tables.sql", "CREATE TABLE dbo.t (id int NOT NULL);");
        source.File("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT t.id, t.nope AS gone FROM dbo.t AS t;\n");

        var (exitCode, output, error) = Run("generate", source.Path, "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("1 unknown(s)", output, StringComparison.Ordinal);
        var note = Assert.Single(error.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
        Assert.StartsWith($"{DiagnosticFormatter.PathAsShown(Path.Join(source.Path, "p.sql"))}:2:", note, StringComparison.Ordinal);
        Assert.Contains(": note HAR2009: dbo.p results[0][].gone: ", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_only_a_declaration_can_give_are_said_once_as_a_warning_with_what_to_declare_and_not_again_as_a_note()
    {
        using var source = new TempDirectory();
        using var outDir = new TempDirectory();
        source.File("p.sql", "CREATE PROCEDURE dbo.p @sql nvarchar(max) AS\nEXEC sp_executesql @sql;\n");

        var (exitCode, _, error) = Run("generate", source.Path, "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        var line = Assert.Single(error.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
        Assert.StartsWith("haruspex: warning HAR4204: dbo.p: its result sets are unknown: ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reasons_without_a_note_are_exactly_those_the_config_layer_reports_with_what_to_declare() =>
        Assert.Equal(
            ConfigApplication.Declarable.Order(),
            Enum.GetValues<IrUnknownReason>().Where(r => InferenceCodes.Of(r) is null).Order());

    [Fact]
    public void A_write_that_fails_on_the_second_file_puts_the_first_back_as_it_was_and_leaves_no_temporary_file()
    {
        using var outDir = new TempDirectory();
        var irPath = Path.Combine(outDir.Path, "haruspex.ir.json");
        File.WriteAllText(irPath, "the old IR\n");
        Directory.CreateDirectory(Path.Combine(outDir.Path, "haruspex.zod.ts")); // a folder where the module would go

        var (exitCode, output, error) = Run("generate", Fixture.Named("orders").Database, "--include", "sales.*", "--out", outDir.Path);

        Assert.Equal(1, exitCode);
        Assert.Equal("", output);
        Assert.Contains("haruspex: error HAR6008: cannot write to ", error, StringComparison.Ordinal);
        Assert.Equal("the old IR\n", File.ReadAllText(irPath));
        Assert.Equal(["haruspex.ir.json", "haruspex.zod.ts"], Directory.EnumerateFileSystemEntries(outDir.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_write_that_fails_on_the_second_file_removes_the_first_when_there_was_none_before()
    {
        using var outDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(outDir.Path, "haruspex.zod.ts"));

        var (exitCode, _, _) = Run("generate", Fixture.Named("orders").Database, "--include", "sales.*", "--out", outDir.Path);

        Assert.Equal(1, exitCode);
        Assert.Equal(["haruspex.zod.ts"], Directory.EnumerateFileSystemEntries(outDir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void A_run_that_fails_before_writing_creates_nothing_not_even_the_output_folder()
    {
        using var root = new TempDirectory();
        var outDir = Path.Combine(root.Path, "out");

        var (exitCode, _, _) = Run("generate", Fixture.Named("orders").Database, "--include", "sales.nothing_here", "--out", outDir);

        Assert.Equal(2, exitCode);
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public void A_run_that_succeeds_replaces_both_files_and_leaves_nothing_else()
    {
        using var outDir = new TempDirectory();
        File.WriteAllText(Path.Combine(outDir.Path, "haruspex.ir.json"), "old");
        File.WriteAllText(Path.Combine(outDir.Path, "haruspex.zod.ts"), "old");

        var (exitCode, _, _) = Run("generate", Fixture.Named("orders").Database, "--include", "sales.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Equal(["haruspex.ir.json", "haruspex.zod.ts"], Directory.EnumerateFileSystemEntries(outDir.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.NotEqual("old", File.ReadAllText(Path.Combine(outDir.Path, "haruspex.ir.json")));
        Assert.NotEqual("old", File.ReadAllText(Path.Combine(outDir.Path, "haruspex.zod.ts")));
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
        // A trigger on it could return result sets, which haruspex cannot see. Once per table.
        using var source = new TempDirectory();
        using var outDir = new TempDirectory();
        source.File("p.sql", "CREATE PROCEDURE dbo.p AS\nINSERT INTO audit.trail (what) VALUES (1);\nDELETE FROM audit.trail;\n");

        var (exitCode, _, error) = Run("generate", source.Path, "--include", "*.*", "--out", outDir.Path);

        Assert.Equal(0, exitCode);
        Assert.Single(error.Split('\n'), l => l.Contains("warning HAR2101:", StringComparison.Ordinal));
        Assert.Contains("p.sql:2:1: warning HAR2101: audit.trail is written here but is not defined in the project", error, StringComparison.Ordinal);
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
            "haruspex: error HAR4303: \"Person\" would name both the procedure dbo.person and the procedure dbo.person#. Generated names must not clash: add a naming \"replace\" rule that tells their names apart (such as { \"pattern\": \"#$\", \"with\": \"Row\" }), or give one of them its own name with a \"name\" override in \"procedures\". These settings go in a haruspex.json beside the project, which it doesn't have yet.\n",
            error.ReplaceLineEndings("\n"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir.Path));
    }
}

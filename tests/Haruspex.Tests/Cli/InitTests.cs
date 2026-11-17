using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex init</c>: it finds the database below the working directory (a project file first, then a DACPAC, then the folder
/// holding the <c>.sql</c> files), writes a starter <c>haruspex.json</c> that <c>generate</c> runs with as it is, says what to run
/// next, and never writes over a config file.
/// </summary>
public class InitTests
{
    private const string Procedure = "CREATE PROCEDURE sales.list_orders AS SELECT 1 AS one;\nGO\nCREATE PROCEDURE audit.list_events AS SELECT 2 AS two;";

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(["init", .. args], output, error, folder.Path);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string Config(TempDirectory folder) => File.ReadAllText(Path.Combine(folder.Path, "haruspex.json"));

    [Fact]
    public void A_folder_of_scripts_gives_a_config_that_types_every_schema_s_procedures_and_generate_runs_with_it()
    {
        using var folder = new TempDirectory();
        folder.File("database/procedures/orders.sql", Procedure);
        folder.File("database/tables/notes.sql", "-- nothing yet");
        folder.File("src/app.ts", "");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Equal(
            """
            haruspex: wrote haruspex.json, which reads database and types every procedure in audit and sales, written to src/db.
            Next, run haruspex generate. Then narrow each schema's "include" to the procedures the application calls.

            """,
            output.ReplaceLineEndings("\n"));
        Assert.Equal(
            """
            // haruspex's settings, written by haruspex init. Each is described in
            // https://github.com/sharpblue/haruspex/blob/main/docs/configuration.md
            {
              "$schema": "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json",
              "source": "database",
              "out": "src/db",
              // Which procedures to type, by schema: every one, to start. Narrow each "include" to those the application calls.
              "schemas": {
                "audit": { "include": ["*"] },
                "sales": { "include": ["*"] },
              },
            }

            """,
            Config(folder));

        using var generated = new StringWriter();
        using var problems = new StringWriter();
        Assert.Equal(0, HaruspexCli.Run(["generate"], generated, problems, folder.Path));
        Assert.StartsWith("haruspex: 2 procedures, no unknowns. Wrote src/db/haruspex.ir.json", generated.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_file_is_chosen_over_the_scripts_beside_it_and_the_packages_its_build_wrote()
    {
        using var folder = new TempDirectory();
        folder.File("db/Sales/Sales.sqlproj", """<Project Sdk="Microsoft.Build.Sql/2.3.0"><PropertyGroup><DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP></PropertyGroup></Project>""");
        folder.File("db/Sales/orders.sql", Procedure);
        folder.File("db/Sales/bin/Release/Sales.dacpac", "");

        var (exitCode, _, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"source\": \"db/Sales/Sales.sqlproj\",", Config(folder), StringComparison.Ordinal);
        Assert.Contains("\"out\": \"generated\",", Config(folder), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dacpac_in_a_build_s_folder_is_found_and_the_system_databases_beside_it_are_not()
    {
        using var folder = new TempDirectory();
        TestPackageFile(folder, "bin/Release/Sales.dacpac");
        folder.File("bin/Release/master.dacpac", "");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"source\": \"bin/Release/Sales.dacpac\",", Config(folder), StringComparison.Ordinal);
        Assert.Contains("types every procedure in sales,", output, StringComparison.Ordinal);
    }

    private static void TestPackageFile(TempDirectory folder, string path)
    {
        var full = Path.Combine(folder.Path, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? folder.Path);
        Sources.TestPackage.Write(full, Sources.TestPackage.Procedure("sales", "list_orders", "SELECT 1 AS one;"));
    }

    [Fact]
    public void Two_project_files_are_a_choice_init_leaves_to_the_developer()
    {
        using var folder = new TempDirectory();
        folder.File("a/A.sqlproj", "<Project />");
        folder.File("b/B.sqlproj", "<Project />");

        var (exitCode, _, error) = Run(folder);

        Assert.Equal(2, exitCode);
        Assert.Equal("haruspex: error HAR6023: found 2 project files, a/A.sqlproj and b/B.sqlproj, and can't tell which is the application's. Name one: haruspex init a/A.sqlproj.\n", error.ReplaceLineEndings("\n"));
        Assert.False(File.Exists(Path.Combine(folder.Path, "haruspex.json")));

        Assert.Equal(0, Run(folder, "b/B.sqlproj").ExitCode);
        Assert.Contains("\"source\": \"b/B.sqlproj\",", Config(folder), StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_file_already_there_is_never_written_over()
    {
        using var folder = new TempDirectory();
        folder.File("db/p.sql", Procedure);
        folder.File("haruspex.json", "{ \"mine\": true }");

        var (exitCode, _, error) = Run(folder);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("haruspex.json: error HAR6021: ", error, StringComparison.Ordinal);
        Assert.Equal("{ \"mine\": true }", Config(folder));
    }

    [Fact]
    public void Nothing_to_read_is_said_and_nothing_is_written()
    {
        using var folder = new TempDirectory();
        folder.File("node_modules/some-package/schema.sql", Procedure);
        folder.File(".git/hooks/x.sql", Procedure);

        var (exitCode, _, error) = Run(folder);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("haruspex: error HAR6022: ", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder.Path, "haruspex.json")));
    }

    [Fact]
    public void A_source_with_no_procedure_gives_a_config_that_selects_none_and_says_so()
    {
        using var folder = new TempDirectory();
        folder.File("sql/tables.sql", "CREATE TABLE dbo.t (id int NOT NULL);");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"schemas\": {},", Config(folder), StringComparison.Ordinal);
        Assert.Contains("where no procedure was found, so it selects none yet.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_file_that_can_t_be_written_is_1_as_any_output_is()
    {
        // A folder where the file would go: no file is there to keep, and the file can't be written, on every platform and as root.
        using var folder = new TempDirectory();
        folder.File("db/p.sql", Procedure);
        Directory.CreateDirectory(Path.Combine(folder.Path, "haruspex.json"));

        var (exitCode, _, error) = Run(folder);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("haruspex: error HAR6008: cannot write haruspex.json: ", error, StringComparison.Ordinal);
    }
}

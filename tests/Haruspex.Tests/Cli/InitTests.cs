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
        folder.File("database/tables/notes.sql", "CREATE TABLE sales.note (id int NOT NULL);");
        folder.File("src/app.ts", "");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Equal(
            """
            haruspex: wrote haruspex.json, which reads database and types every procedure in audit and sales, written to src/db.
            Next, run haruspex generate. Then narrow each schema's "include" to the procedures the application calls.
            Keep src/db out of your formatter (in .prettierignore, or as "!src/db" after "**" in Biome's files.includes): haruspex check fails on a file a formatter restyled.

            """,
            output.ReplaceLineEndings("\n"));
        Assert.Equal(
            """
            // haruspex's settings, written by haruspex init. Each is described in
            // https://haruspex.tools/docs/configuration
            // where "The settings you probably want" says which most projects need next: the callers' session,
            // a DACPAC's build, the collation of a folder of scripts.
            {
              "$schema": "https://haruspex.tools/schemas/main/haruspex.schema.json",
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

    /// <summary>
    /// The layout of the example application: the database's scripts in one folder and the application's query files in another.
    /// The folder holding both would read the query files as the database's scripts, and with them anything else in the
    /// application's folder that a folder source reads (installed packages, hidden folders), so the source is the folder whose
    /// scripts define objects, and the other is named as what it may be.
    /// </summary>
    [Fact]
    public void Scripts_that_define_nothing_in_a_folder_of_their_own_are_left_out_of_the_source_and_named()
    {
        using var folder = new TempDirectory();
        folder.File("database/tables/plants.sql", "CREATE TABLE garden.plant (id int NOT NULL PRIMARY KEY, name nvarchar(40) NOT NULL);");
        folder.File("database/procedures/get_plant.sql", "CREATE PROCEDURE garden.get_plant @id int AS SELECT p.name FROM garden.plant AS p WHERE p.id = @id;");
        folder.File("queries/thirsty_plants.sql", "SELECT p.id FROM garden.plant AS p;");
        folder.File("queries/water_plant.sql", "INSERT INTO garden.water_log (plant_id, watered_at) VALUES (@plant_id, SYSUTCDATETIME());");
        folder.File("queries/count_plants.sql", "SET NOCOUNT ON; SELECT COUNT(*) AS n FROM garden.plant AS p;");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Contains("\"source\": \"database\",", Config(folder), StringComparison.Ordinal);
        Assert.Contains(
            "\nThe .sql files in queries add nothing to the database, so the source leaves them out. If they are the application's query files, add \"queries\": \"queries\" to type them.\n",
            output.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    /// <summary>The same layout one folder down: the source is the database's folder, not the application's.</summary>
    [Fact]
    public void The_database_s_folder_is_found_below_the_application_s()
    {
        using var folder = new TempDirectory();
        folder.File("app/database/t.sql", "CREATE TABLE garden.plant (id int NOT NULL);");
        folder.File("app/database/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.id FROM garden.plant AS p;");
        folder.File("app/queries/reports/q.sql", "SELECT p.id FROM garden.plant AS p;");
        folder.File("app/queries/r.sql", "WITH p AS (SELECT 1 AS id) SELECT p.id FROM p;");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"source\": \"app/database\",", Config(folder), StringComparison.Ordinal);
        Assert.Contains("The .sql files in app/queries add nothing to the database", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A script that isn't queries alone stays in the source wherever it is, since leaving it out could claim more than the
    /// database holds: a deployment script that turns a check off, a <c>SELECT INTO</c>, seed data beside the database's folder.
    /// With the database in folders side by side beside the queries, the source lists those folders, and the queries are left out.
    /// </summary>
    [Theory]
    [InlineData("ALTER TABLE garden.plant NOCHECK CONSTRAINT ck_height;")]
    [InlineData("BULK INSERT garden.plant FROM 'plants.csv';")]
    [InlineData("ALTER TABLE garden.plant ADD note nvarchar(20) NULL;")]
    [InlineData("SELECT p.id FROM garden.plant AS p;\nGO\nDROP TABLE garden.plant;")]
    public void A_script_that_changes_the_database_stays_in_the_source_and_queries_inside_it_are_named(string sql)
    {
        using var folder = new TempDirectory();
        folder.File("database/t.sql", "CREATE TABLE garden.plant (id int NOT NULL, height int NOT NULL CONSTRAINT ck_height CHECK (height > 0));");
        folder.File("database/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.height FROM garden.plant AS p;");
        folder.File("deploy/script.sql", sql);
        folder.File("queries/q.sql", "SELECT p.id FROM garden.plant AS p;");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Contains("\"source\": [\"database\", \"deploy\"],", Config(folder), StringComparison.Ordinal);
        Assert.Contains("haruspex: wrote haruspex.json, which reads database and deploy and types every procedure in garden", output, StringComparison.Ordinal);
        Assert.Contains(
            "The .sql files in queries add nothing to the database, so the source leaves them out. If they are the application's query files, add \"queries\": \"queries\" to type them.",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("one folder of their own", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The greenhouse example as a developer has it before init: its <c>CREATE SCHEMA</c> script adds nothing the folder reader
    /// records, but it sits beside the table it is for, in the database's folder, so it is read as the database's and nothing more
    /// is said of it. Only <c>queries</c>, outside the source, is named.
    /// </summary>
    [Fact]
    public void The_greenhouse_example_reads_its_database_folder_and_names_only_its_queries()
    {
        using var folder = new TempDirectory();
        var example = Path.Combine(RepositoryPaths.Root, "examples", "greenhouse");
        foreach (var part in new[] { "database", "queries", "src" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(example, part), "*", SearchOption.AllDirectories))
                folder.File(Path.GetRelativePath(example, file), File.ReadAllText(file));
        }

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Contains("\"source\": \"database\",", Config(folder), StringComparison.Ordinal);
        Assert.Equal(
            """
            haruspex: wrote haruspex.json, which reads database and types every procedure in garden, written to src/db.
            Next, run haruspex generate. Then narrow each schema's "include" to the procedures the application calls.
            Keep src/db out of your formatter (in .prettierignore, or as "!src/db" after "**" in Biome's files.includes): haruspex check fails on a file a formatter restyled.
            The .sql files in queries add nothing to the database, so the source leaves them out. If they are the application's query files, add "queries": "queries" to type them.

            """,
            output.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// A script that adds nothing, beside one that adds to the database, is part of the database's folder wherever the source is:
    /// its folder isn't named, while a folder of such scripts alone beside the database's folders is left out, the source listing
    /// those folders.
    /// </summary>
    [Fact]
    public void A_script_that_adds_nothing_beside_the_database_s_scripts_is_not_named()
    {
        using var folder = new TempDirectory();
        folder.File("database/tables/schemas.sql", "CREATE SCHEMA garden;");
        folder.File("database/tables/plants.sql", "CREATE TABLE garden.plant (id int NOT NULL);");
        folder.File("database/seed/plants.sql", "INSERT INTO garden.plant (id) VALUES (1);");
        folder.File("database/procedures/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.id FROM garden.plant AS p;");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"source\": [\"database/procedures\", \"database/tables\"],", Config(folder), StringComparison.Ordinal);
        Assert.Contains("The .sql files in database/seed add nothing to the database, so the source leaves them out.", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// With the database's folders beside the queries folder, no one of them is the database's: init lists them all as the source,
    /// which leaves the queries out, rather than advising a move (decision 0127). The file it writes reads as generate reads it.
    /// </summary>
    [Fact]
    public void Database_folders_beside_the_queries_are_listed_as_the_source()
    {
        using var folder = new TempDirectory();
        folder.File("tables/t.sql", "CREATE TABLE garden.plant (id int NOT NULL);");
        folder.File("tables/u.sql", "CREATE TABLE garden.water_log (plant_id int NOT NULL);");
        folder.File("procedures/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.id FROM garden.plant AS p;");
        folder.File("queries/q.sql", "SELECT p.id FROM garden.plant AS p;");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("  \"source\": [\"procedures\", \"tables\"],\n", Config(folder), StringComparison.Ordinal);
        Assert.Equal(
            """
            haruspex: wrote haruspex.json, which reads procedures and tables and types every procedure in garden, written to generated.
            Next, run haruspex generate. Then narrow each schema's "include" to the procedures the application calls.
            Keep generated out of your formatter (in .prettierignore, or as "!generated" after "**" in Biome's files.includes): haruspex check fails on a file a formatter restyled.
            The .sql files in queries add nothing to the database, so the source leaves them out. If they are the application's query files, add "queries": "queries" to type them.

            """,
            output.ReplaceLineEndings("\n"));
        Assert.Empty(ConfigReader.Read(Path.Combine(folder.Path, HaruspexConfig.FileName)).Diagnostics);
    }

    /// <summary>
    /// A folder of queries alone inside one of the database's folders is read as that folder's, which a list can't leave out: it is
    /// named, with the folder that holds it.
    /// </summary>
    [Fact]
    public void Queries_inside_a_listed_folder_are_named_with_the_folder_that_holds_them()
    {
        using var folder = new TempDirectory();
        folder.File("tables/t.sql", "CREATE TABLE garden.plant (id int NOT NULL);");
        folder.File("tables/reports/r.sql", "SELECT p.id FROM garden.plant AS p;");
        folder.File("procedures/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.id FROM garden.plant AS p;");
        folder.File("queries/q.sql", "SELECT p.id FROM garden.plant AS p;");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"source\": [\"procedures\", \"tables\"],", Config(folder), StringComparison.Ordinal);
        Assert.Contains("The .sql files in tables/reports add nothing to the database, but tables holds them, so they are read as the database's scripts and not typed.", output, StringComparison.Ordinal);
        Assert.Contains("The .sql files in queries add nothing to the database, so the source leaves them out.", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A database script directly in the source is named as itself, since there is no folder of it to name; and a list, which names
    /// folders, can't hold it, so init advises the move it would otherwise make for you.
    /// </summary>
    [Fact]
    public void A_database_script_directly_in_the_source_is_named_as_itself()
    {
        using var folder = new TempDirectory();
        folder.File("schema.sql", "CREATE TABLE garden.plant (id int NOT NULL);");
        folder.File("queries/q.sql", "SELECT p.id FROM garden.plant AS p;");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "It reads . because what adds to the database is in schema.sql. If queries holds the application's query files, put schema.sql in a folder of its own, remove haruspex.json and run haruspex init again.",
            output,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A query script outside the source, in a folder that can't be named since the database's own scripts are below it, is named as
    /// itself: left out, as a folder of queries is, with the <c>queries</c> entry that would type it.
    /// </summary>
    [Fact]
    public void A_query_script_left_out_whose_folder_holds_the_database_is_named_as_itself()
    {
        using var folder = new TempDirectory();
        folder.File("db/t.sql", "CREATE TABLE garden.plant (id int NOT NULL);");
        folder.File("db/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.id FROM garden.plant AS p;");
        folder.File("q.sql", "SELECT p.id FROM garden.plant AS p;");
        folder.File("r.sql", "SELECT p.id FROM garden.plant AS p WHERE p.id = @id;");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Contains("\"source\": \"db\",", Config(folder), StringComparison.Ordinal);
        Assert.Equal(
            """
            haruspex: wrote haruspex.json, which reads db and types every procedure in garden, written to generated.
            Next, run haruspex generate. Then narrow each schema's "include" to the procedures the application calls.
            Keep generated out of your formatter (in .prettierignore, or as "!generated" after "**" in Biome's files.includes): haruspex check fails on a file a formatter restyled.
            q.sql and r.sql add nothing to the database, so the source leaves them out. If they are the application's query files, add "queries": ["q.sql", "r.sql"] to type them.

            """,
            output.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// A query script whose folder holds a database script further down is read as the database's, since the folder can't be left
    /// out: it is named, with the script below that holds it in, so that it isn't passed over without a word.
    /// </summary>
    [Fact]
    public void A_query_script_read_as_the_database_s_names_the_database_script_below_its_folder()
    {
        using var folder = new TempDirectory();
        folder.File("db/t.sql", "CREATE TABLE garden.plant (id int NOT NULL, height int NOT NULL CONSTRAINT ck_height CHECK (height > 0));");
        folder.File("db/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.id FROM garden.plant AS p;");
        folder.File("queries/q.sql", "SELECT p.id FROM garden.plant AS p;");
        folder.File("queries/legacy/fix.sql", "ALTER TABLE garden.plant NOCHECK CONSTRAINT ck_height;");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Contains("\"source\": \".\",", Config(folder), StringComparison.Ordinal);
        Assert.EndsWith(
            "queries/q.sql adds nothing to the database, but . holds it, so it is read as the database's script and not typed. Its folder can't be left out, since queries/legacy/fix.sql, below it, adds to the database.\n",
            output.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    /// <summary>When the source is a list of folders, such scripts are named with the listed folder that holds them.</summary>
    [Fact]
    public void Query_scripts_read_as_the_database_s_in_a_listed_folder_are_named_with_it()
    {
        using var folder = new TempDirectory();
        folder.File("tables/t.sql", "CREATE TABLE garden.plant (id int NOT NULL, height int NOT NULL CONSTRAINT ck_height CHECK (height > 0));");
        folder.File("tables/reports/r.sql", "SELECT p.id FROM garden.plant AS p;");
        folder.File("procedures/p.sql", "CREATE PROCEDURE garden.list_plants AS SELECT p.id FROM garden.plant AS p;");
        folder.File("queries/a.sql", "SELECT p.id FROM garden.plant AS p;");
        folder.File("queries/b.sql", "SELECT p.height FROM garden.plant AS p;");
        folder.File("queries/legacy/fix.sql", "ALTER TABLE garden.plant NOCHECK CONSTRAINT ck_height;");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal((0, ""), (exitCode, error));
        Assert.Contains("\"source\": [\"procedures\", \"queries\", \"tables\"],", Config(folder), StringComparison.Ordinal);
        Assert.EndsWith(
            "queries/a.sql and queries/b.sql add nothing to the database, but queries holds them, so they are read as the database's scripts and not typed. Their folder can't be left out, since queries/legacy/fix.sql, below it, adds to the database.\n",
            output.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    /// <summary>A script that doesn't parse stays in the source too, so that its error is said, not passed over.</summary>
    [Fact]
    public void A_script_that_does_not_parse_stays_in_the_source_and_its_error_is_said()
    {
        using var folder = new TempDirectory();
        folder.File("database/t.sql", "CREATE TABLE garden.plant (id int NOT NULL);");
        folder.File("broken/q.sql", "SELECT p.id FROM garden.plant AS p WHERE");

        var (exitCode, _, error) = Run(folder);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"source\": \".\",", Config(folder), StringComparison.Ordinal);
        Assert.Contains("broken/q.sql:1:", error, StringComparison.Ordinal);
    }

    /// <summary>Two folders of definitions are one database's parts, as <c>tables</c> and <c>procedures</c> are, so both are read.</summary>
    [Fact]
    public void Scripts_that_define_objects_in_two_folders_are_read_from_the_folder_that_holds_both()
    {
        using var folder = new TempDirectory();
        folder.File("tables/t.sql", "CREATE TABLE sales.t (id int NOT NULL);");
        folder.File("procedures/p.sql", "CREATE PROCEDURE sales.p AS SELECT t.id FROM sales.t AS t;");

        var (exitCode, output, _) = Run(folder);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"source\": \".\",", Config(folder), StringComparison.Ordinal);
        Assert.DoesNotContain("define no object", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// What reading the source found wrong is said, and the run fails: a project file that isn't XML was reported as a source where
    /// no procedure was found, and the next generate said only that nothing was selected.
    /// </summary>
    [Fact]
    public void A_source_that_reads_with_errors_says_them_and_is_1()
    {
        using var folder = new TempDirectory();
        folder.File("db/Shop.sqlproj", "not a project");

        var (exitCode, output, error) = Run(folder);

        Assert.Equal(1, exitCode);
        Assert.Contains("db/Shop.sqlproj", error, StringComparison.Ordinal);
        Assert.Contains("error HAR1010: ", error, StringComparison.Ordinal);
        Assert.Equal(
            "haruspex: wrote haruspex.json, which reads db/Shop.sqlproj, but reading it gave 1 error. Correct it, then run haruspex generate.\n",
            output.ReplaceLineEndings("\n"));
        Assert.True(File.Exists(Path.Combine(folder.Path, "haruspex.json")));
    }

    /// <summary>The section the file init writes points to is on the configuration page, under the heading it names.</summary>
    [Fact]
    public void The_section_the_written_file_points_to_is_on_the_configuration_page()
    {
        using var folder = new TempDirectory();
        folder.File("db/p.sql", Procedure);
        Assert.Equal(0, Run(folder).ExitCode);
        var heading = Config(folder).Split('"')[1];

        var page = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "configuration.md"));
        Assert.Contains($"\n## {heading}\n", page.ReplaceLineEndings("\n"), StringComparison.Ordinal);
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

    [Fact]
    public void A_source_outside_the_folder_is_written_relative_to_the_config_file()
    {
        using var folder = new TempDirectory();
        folder.File("db/p.sql", Procedure);
        Directory.CreateDirectory(Path.Combine(folder.Path, "app"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, HaruspexCli.Run(["init", "../db"], output, error, Path.Combine(folder.Path, "app")));

        Assert.Contains("\"source\": \"../db\",", File.ReadAllText(Path.Combine(folder.Path, "app", "haruspex.json")), StringComparison.Ordinal);
    }
}

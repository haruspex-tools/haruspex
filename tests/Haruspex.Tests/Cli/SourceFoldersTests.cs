using System.Text.Json.Nodes;
using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>source</c> as several folders, end to end: a database kept as folders side by side, beside the application's query files,
/// read as one, from the config file's list and from the command line's; the IR records the folders (<c>source.paths</c>, IR 0.32)
/// and a document of one folder is as before; a list holding a project or a package, a folder not there, and folders that hold the
/// same scripts are each refused naming the entry at fault; and <c>check</c> reports a folder added or removed.
/// Why: docs/decisions/0127-a-source-may-be-several-folders.md.
/// </summary>
public class SourceFoldersTests
{
    private const string Table = "CREATE TABLE shop.ingot (id int NOT NULL PRIMARY KEY, weight int NOT NULL, note nvarchar(20) NULL);";

    private const string Procedure = "CREATE PROCEDURE shop.weigh @id int AS SELECT i.weight, i.note FROM shop.ingot AS i WHERE i.id = @id;";

    /// <summary>The database in <c>tables</c> and <c>procedures</c> side by side, beside a <c>queries</c> folder that isn't the database's.</summary>
    private static TempDirectory SideBySide(string source)
    {
        var folder = new TempDirectory();
        folder.File("tables/ingot.sql", Table);
        folder.File("procedures/weigh.sql", Procedure);
        folder.File("queries/heaviest.sql", "SELECT TOP (1) i.id FROM shop.ingot AS i ORDER BY i.weight DESC;");
        folder.File("haruspex.json", $$"""{ "source": {{source}}, "out": "out", "schemas": { "shop": { "include": ["*"] } } }""");
        return folder;
    }

    private static (int ExitCode, string Output, string Error) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, CliOutput.Shown(output.ToString(), folder.Path), CliOutput.Shown(error.ToString(), folder.Path));
    }

    private static JsonNode Ir(TempDirectory folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder.Path, "out", HaruspexCli.IrFileName)))!;

    [Fact]
    public void Folders_the_config_file_lists_are_read_as_one_database_and_recorded_in_the_ir()
    {
        using var folder = SideBySide("""["tables", "procedures"]""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.True(exitCode == 0, error);
        var ir = Ir(folder);
        Assert.Equal("0.32", ir["irVersion"]!.GetValue<string>());
        Assert.Equal(["procedures", "tables"], ir["source"]!["paths"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Null(ir["source"]!["path"]);
        Assert.Equal("procedures/weigh.sql:1:1", ir["objects"]![0]!["at"]!.GetValue<string>());
        Assert.Contains("tables/ingot.sql", ir["source"]!["files"]!.AsObject().Select(f => f.Key));
    }

    [Fact]
    public void A_change_of_case_alone_in_the_list_where_the_file_system_ignores_case_changes_nothing_check_sees()
    {
        // The IR records each folder by its name on disk, as its locations and files do: what the list says in another case is
        // the same folder, so the same document.
        using var folder = SideBySide("""["tables", "procedures"]""");
        if (!Directory.Exists(Path.Combine(folder.Path, "TABLES")))
            Assert.Skip("this file system tells case apart, so TABLES would be another folder");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("haruspex.json", """{ "source": ["TABLES", "Procedures"], "out": "out", "schemas": { "shop": { "include": ["*"] } } }""");

        var (exitCode, _, error) = Run(folder, "check");

        Assert.True(exitCode == 0, error);
        Assert.Equal(["procedures", "tables"], Ir(folder)["source"]!["paths"]!.AsArray().Select(p => p!.GetValue<string>()));
    }

    [Fact]
    public void Folders_the_command_line_gives_override_the_config_file_s_source_and_read_alike()
    {
        using var listed = SideBySide("""["tables", "procedures"]""");
        using var given = SideBySide("\"queries\"");

        Assert.Equal(0, Run(listed, "generate").ExitCode);
        var (exitCode, _, error) = Run(given, "generate", "procedures", "tables");

        Assert.True(exitCode == 0, error);
        Assert.Equal(File.ReadAllText(Path.Combine(listed.Path, "out", HaruspexCli.IrFileName)), File.ReadAllText(Path.Combine(given.Path, "out", HaruspexCli.IrFileName)));
    }

    [Fact]
    public void A_list_of_one_folder_gives_the_ir_one_path_gives_byte_for_byte()
    {
        using var listed = new TempDirectory();
        using var single = new TempDirectory();
        foreach (var (folder, source) in new[] { (listed, "[\"database\"]"), (single, "\"database\"") })
        {
            folder.File("database/ingot.sql", Table);
            folder.File("database/weigh.sql", Procedure);
            folder.File("haruspex.json", $$"""{ "source": {{source}}, "out": "out", "schemas": { "shop": { "include": ["*"] } } }""");
            Assert.Equal(0, Run(folder, "generate").ExitCode);
        }

        Assert.Equal(File.ReadAllText(Path.Combine(single.Path, "out", HaruspexCli.IrFileName)), File.ReadAllText(Path.Combine(listed.Path, "out", HaruspexCli.IrFileName)));
    }

    [Fact]
    public void Check_compares_one_folder_with_several_both_ways_naming_the_folder_added_or_removed()
    {
        // One folder against two: the folder added is reported; two against one: the folder removed. A folder of no scripts the
        // procedure reads, so that no type changes and the report is the folders' alone.
        using var folder = new TempDirectory();
        folder.File("database/ingot.sql", Table);
        folder.File("database/weigh.sql", Procedure);
        folder.File("audit/log.sql", "CREATE TABLE shop.audit_log (id int NOT NULL);");
        folder.File("haruspex.json", """{ "source": "database", "out": "out", "schemas": { "shop": { "include": ["weigh"] } } }""");
        Assert.Equal(0, Run(folder, "generate").ExitCode);

        var (widenedExit, _, widened) = Run(folder, "check", "database", "audit");
        Assert.Equal(0, Run(folder, "generate", "database", "audit").ExitCode);
        var (narrowedExit, _, narrowed) = Run(folder, "check", "database");

        Assert.Equal(HaruspexCli.ExitCodes.Failed, widenedExit);
        Assert.Contains("source.paths.audit: now read", widened, StringComparison.Ordinal);
        Assert.DoesNotContain("source.paths.database", widened, StringComparison.Ordinal);
        Assert.Equal(HaruspexCli.ExitCodes.Failed, narrowedExit);
        Assert.Contains("source.paths.audit: no longer read", narrowed, StringComparison.Ordinal);
        Assert.DoesNotContain("source.paths.database", narrowed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_of_one_folder_is_that_folder_and_its_document_is_labelled_as_before()
    {
        using var folder = new TempDirectory();
        folder.File("database/ingot.sql", Table);
        folder.File("database/weigh.sql", Procedure);
        folder.File("haruspex.json", """{ "source": ["database"], "out": "out", "schemas": { "shop": { "include": ["*"] } } }""");

        Assert.Equal(0, Run(folder, "generate").ExitCode);

        var ir = Ir(folder);
        Assert.Equal("0.30", ir["irVersion"]!.GetValue<string>());
        Assert.Equal("database", ir["source"]!["path"]!.GetValue<string>());
        Assert.Null(ir["source"]!["paths"]);
        Assert.Equal("weigh.sql:1:1", ir["objects"]![0]!["at"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("db/Shop.sqlproj", ".sqlproj")]
    [InlineData("Shop.DACPAC", ".dacpac")]
    public void A_project_or_a_package_among_the_command_line_s_sources_is_refused_naming_it(string whole, string kind)
    {
        using var folder = SideBySide("\"tables\"");
        folder.File(whole, "");

        var (exitCode, _, error) = Run(folder, "generate", "tables", whole);

        Assert.Equal(HaruspexCli.ExitCodes.Usage, exitCode);
        Assert.Contains($"error HAR6044: {whole} is a {kind}, a whole database, so it can't be read with other sources", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_in_the_config_file_s_list_is_refused_naming_its_entry()
    {
        using var folder = SideBySide("""["tables", "db/Shop.sqlproj"]""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.Equal(HaruspexCli.ExitCodes.Usage, exitCode);
        Assert.Contains("haruspex.json:1:24: error HAR3005: source[1]", error, StringComparison.Ordinal);
        Assert.Contains("is \"db/Shop.sqlproj\", a project or a package", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_folder_listed_that_isn_t_there_is_named()
    {
        using var folder = SideBySide("""["tables", "views", "procedures", "functions"]""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.Equal(HaruspexCli.ExitCodes.Usage, exitCode);
        Assert.Contains("error HAR6004: directory not found: views", error, StringComparison.Ordinal);
        Assert.Contains("error HAR6004: directory not found: functions", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_listed_inside_another_is_an_error_naming_both_and_nothing_is_written()
    {
        using var folder = SideBySide("""["procedures", "."]""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.Equal(HaruspexCli.ExitCodes.Usage, exitCode);
        Assert.Contains("error HAR1023: procedures is inside ., so its scripts would be read twice.", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "out")));
    }

    [Fact]
    public void A_folder_listed_twice_in_two_spellings_is_named_as_the_config_file_writes_each()
    {
        using var folder = SideBySide("""["tables", "procedures", "./tables"]""");

        var (exitCode, _, error) = Run(folder, "generate");

        Assert.Equal(HaruspexCli.ExitCodes.Usage, exitCode);
        Assert.Contains("error HAR1023: tables and ./tables are the same folder", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_reports_a_folder_added_to_the_list_or_removed_from_it()
    {
        using var folder = SideBySide("""["tables", "procedures"]""");
        folder.File("views/light.sql", "CREATE VIEW shop.light AS SELECT i.id FROM shop.ingot AS i WHERE i.weight < 10;");
        Assert.Equal(0, Run(folder, "generate").ExitCode);

        var (addedExit, _, addedError) = Run(folder, "check", "tables", "procedures", "views");
        var (removedExit, _, _) = Run(folder, "check", "tables", "procedures");
        folder.File("haruspex.json", """{ "source": ["tables", "procedures", "views"], "out": "out", "schemas": { "shop": { "include": ["*"] } } }""");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        var (goneExit, _, goneError) = Run(folder, "check", "procedures", "tables");

        Assert.Equal(HaruspexCli.ExitCodes.Failed, addedExit);
        Assert.Contains("source.paths.views: now read", addedError, StringComparison.Ordinal);
        Assert.Equal(0, removedExit);
        Assert.Equal(HaruspexCli.ExitCodes.Failed, goneExit);
        Assert.Contains("source.paths.views: no longer read", goneError, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_shows_the_line_a_location_in_a_listed_folder_beside_the_config_file_s_points_at()
    {
        // Listed folders beside the config file's folder: a location climbs out of it, and is followed only into a folder listed.
        using var folder = new TempDirectory();
        folder.File("db/tables/ingot.sql", Table);
        folder.File("db/procedures/weigh.sql", Procedure);
        var app = Path.Combine(folder.Path, "app");
        folder.File("app/haruspex.json", """{ "source": ["../db/tables", "../db/procedures"], "out": "out", "schemas": { "shop": { "include": ["*"] } } }""");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, HaruspexCli.Run(["generate"], output, error, app));

        var exitCode = HaruspexCli.Run(["explain", "shop.weigh", "note"], output, error, app);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Contains("note nvarchar(20) NULL", output.ToString(), StringComparison.Ordinal);
    }
}

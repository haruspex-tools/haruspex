using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// <c>haruspex generate</c> removes the files of its last run that this run no longer writes, as the list it keeps beside them
/// (<c>haruspex.files.json</c>) names them, and nothing else; <c>check</c> reports each such file
/// (docs/decisions/0091-generate-removes-only-the-files-its-list-names.md). Every test runs on real temporary folders: an
/// invented shop whose output is split into a module for <c>sales</c> and one for <c>admin</c>.
/// </summary>
public class StaleFilesTests
{
    private const string Tables = """
        CREATE SCHEMA sales;
        GO
        CREATE SCHEMA admin;
        GO
        CREATE TABLE sales.orders (order_id int NOT NULL PRIMARY KEY, total decimal(9, 2) NOT NULL);
        CREATE TABLE admin.clerks (clerk_id int NOT NULL PRIMARY KEY, login varchar(30) NOT NULL);
        """;

    private const string Procedures = """
        CREATE PROCEDURE sales.list_orders AS SELECT o.order_id, o.total FROM sales.orders AS o;
        GO
        CREATE PROCEDURE admin.list_clerks AS SELECT c.clerk_id, c.login FROM admin.clerks AS c;
        """;

    /// <summary>The config with both modules.</summary>
    private const string BothModules = """
        { "source": "db", "out": "out", "emit": ["zod", "json-schema", "mssql"],
          "split": { "sales": ["sales"], "admin": ["admin"] },
          "schemas": { "sales": { "include": ["*"] }, "admin": { "include": ["*"] } } }
        """;

    /// <summary>The config once <c>"admin": ["admin"]</c> is taken out of the split, and admin's procedures out of the selection.</summary>
    private const string SalesOnly = """
        { "source": "db", "out": "out", "emit": ["zod", "json-schema", "mssql"],
          "split": { "sales": ["sales"] },
          "schemas": { "sales": { "include": ["*"] } } }
        """;

    private static readonly string[] AdminFiles = ["admin.jsonschema.json", "admin.mssql.ts", "admin.zod.ts"];

    private static TempDirectory Shop(string config = BothModules)
    {
        var folder = new TempDirectory();
        folder.File("db/tables.sql", Tables);
        folder.File("db/procedures.sql", Procedures);
        folder.File("haruspex.json", config);
        return folder;
    }

    private static (int ExitCode, string Said) Run(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path);
        return (exitCode, CliOutput.Shown(output.ToString() + error, folder.Path));
    }

    private static string Out(TempDirectory folder, string name = "") => Path.Combine(folder.Path, "out", name);

    private static string ListText(TempDirectory folder) => File.ReadAllText(Out(folder, "haruspex.files.json"));

    /// <summary>Generates with <paramref name="first"/>, then with <paramref name="second"/>; what the second run said.</summary>
    private static (int ExitCode, string Said) Twice(TempDirectory folder, string first, string second)
    {
        folder.File("haruspex.json", first);
        var (exitCode, said) = Run(folder, "generate");
        Assert.True(exitCode == 0, said);
        folder.File("haruspex.json", second);
        return Run(folder, "generate");
    }

    [Fact]
    public void Generate_writes_a_sorted_list_of_the_files_it_wrote_beside_them()
    {
        using var folder = Shop();

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.Equal(
            """
            {
              "about": "The files haruspex generate wrote to this folder, which a later run removes when it no longer writes them. Generated: do not edit.",
              "files": [
                "admin.jsonschema.json",
                "admin.mssql.ts",
                "admin.zod.ts",
                "haruspex.ir.json",
                "sales.jsonschema.json",
                "sales.mssql.ts",
                "sales.zod.ts"
              ]
            }

            """.ReplaceLineEndings("\n"),
            ListText(folder));
    }

    [Fact]
    public void Removing_a_module_from_the_split_removes_its_three_files_and_nothing_else()
    {
        using var folder = Shop();
        folder.File("out/notes.md", "The team's own notes, beside the output.");
        folder.File("out/admin.extra.ts", "// A file of the team's own, named like a module's.");

        var (exitCode, said) = Twice(folder, BothModules, SalesOnly);

        Assert.True(exitCode == 0, said);
        Assert.All(AdminFiles, f => Assert.False(File.Exists(Out(folder, f)), f));
        Assert.True(File.Exists(Out(folder, "notes.md")));
        Assert.True(File.Exists(Out(folder, "admin.extra.ts")));
        Assert.True(File.Exists(Out(folder, "sales.zod.ts")));
        Assert.Contains("Removed out/admin.jsonschema.json, out/admin.mssql.ts and out/admin.zod.ts, which this run no longer writes.", said, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", ListText(folder), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_reports_the_removed_files_in_its_json_report()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("haruspex.json", SalesOnly);

        var (exitCode, said) = Run(folder, "generate", "--format", "json");

        Assert.True(exitCode == 0, said);
        Assert.Contains("\"removed\": [\n        \"out/admin.jsonschema.json\",\n        \"out/admin.mssql.ts\",\n        \"out/admin.zod.ts\"\n      ]", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_edited_by_hand_is_still_removed_when_the_list_names_it()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        File.AppendAllText(Out(folder, "admin.zod.ts"), "// edited by hand\n");
        folder.File("haruspex.json", SalesOnly);

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.False(File.Exists(Out(folder, "admin.zod.ts")));
    }

    [Fact]
    public void Taking_an_emitter_off_without_a_split_removes_its_file()
    {
        using var folder = Shop();
        const string Both = """{ "source": "db", "out": "out", "emit": ["zod", "json-schema"], "schemas": { "sales": { "include": ["*"] } } }""";
        const string JsonSchemaOnly = """{ "source": "db", "out": "out", "emit": ["json-schema"], "schemas": { "sales": { "include": ["*"] } } }""";

        var (exitCode, said) = Twice(folder, Both, JsonSchemaOnly);

        Assert.True(exitCode == 0, said);
        Assert.False(File.Exists(Out(folder, "haruspex.zod.ts")));
        Assert.True(File.Exists(Out(folder, "haruspex.jsonschema.json")));
        Assert.Contains("Removed out/haruspex.zod.ts, which this run no longer writes.", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_fails_removes_nothing_and_keeps_the_list()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        var list = ListText(folder);

        // admin's procedures still selected, but in no module: an error before anything is written.
        folder.File("haruspex.json", SalesOnly.Replace("\"sales\": { \"include\": [\"*\"] } }", "\"sales\": { \"include\": [\"*\"] }, \"admin\": { \"include\": [\"*\"] } }", StringComparison.Ordinal));
        var (exitCode, said) = Run(folder, "generate");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("HAR4008", said, StringComparison.Ordinal);
        Assert.All(AdminFiles, f => Assert.True(File.Exists(Out(folder, f)), f));
        Assert.Equal(list, ListText(folder));
    }

    [Fact]
    public void The_first_run_with_no_list_removes_nothing_and_writes_one()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        File.Delete(Out(folder, "haruspex.files.json"));
        folder.File("haruspex.json", SalesOnly);

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.All(AdminFiles, f => Assert.True(File.Exists(Out(folder, f)), f));
        Assert.DoesNotContain("Removed", said, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", ListText(folder), StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_that_cannot_be_read_is_warned_about_and_written_anew_removing_nothing()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("out/haruspex.files.json", "{ \"files\": [\"admin.zod.ts\", ");
        folder.File("haruspex.json", SalesOnly);

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.Contains("out/haruspex.files.json: warning HAR6039: ", said, StringComparison.Ordinal);
        Assert.All(AdminFiles, f => Assert.True(File.Exists(Out(folder, f)), f));
        Assert.Contains("\"sales.zod.ts\"", ListText(folder), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../x.ts")]
    [InlineData("sub/../../x.ts")]
    [InlineData("./../x.ts")]
    public void A_listed_path_that_climbs_out_of_the_folder_is_never_removed(string entry)
    {
        using var folder = Shop();
        var outside = folder.File("x.ts", "// outside the output folder");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        Listing(folder, entry);

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.True(File.Exists(outside));
        Assert.Contains($"out/haruspex.files.json: warning HAR6040: \"{entry}\"", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_absolute_path_is_never_removed()
    {
        using var folder = Shop();
        var outside = folder.File("x.ts", "// outside the output folder");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        Listing(folder, outside.Replace('\\', '/'));

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.True(File.Exists(outside));
        Assert.Contains("warning HAR6040: ", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_path_through_a_symbolic_link_out_of_the_folder_is_never_removed()
    {
        using var folder = Shop();
        var victim = folder.File("elsewhere/victim.ts", "// outside the output folder");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        Link(() => Directory.CreateSymbolicLink(Out(folder, "linked"), Path.Combine(folder.Path, "elsewhere")));
        Listing(folder, "linked/victim.ts");

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.True(File.Exists(victim));
        Assert.Contains("out/haruspex.files.json: warning HAR6040: \"linked/victim.ts\"", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_symbolic_link_to_a_file_out_of_the_folder_is_never_removed_nor_its_target()
    {
        using var folder = Shop();
        var victim = folder.File("elsewhere/victim.ts", "// outside the output folder");
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        Link(() => File.CreateSymbolicLink(Out(folder, "victim.ts"), victim));
        Listing(folder, "victim.ts");

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.True(File.Exists(victim));
        Assert.NotNull(new FileInfo(Out(folder, "victim.ts")).LinkTarget);
        Assert.Contains("out/haruspex.files.json: warning HAR6040: \"victim.ts\"", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_folder_or_a_listed_file_that_is_gone_is_passed_over_without_a_word()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        Directory.CreateDirectory(Out(folder, "kept"));
        Listing(folder, "kept", "gone.ts");

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.True(Directory.Exists(Out(folder, "kept")));
        Assert.DoesNotContain("HAR60", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Removed", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_a_file_leaves_the_folder_it_was_in_even_when_empty()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("out/old/old.zod.ts", "// Generated by haruspex. Do not edit.");
        Listing(folder, "old/old.zod.ts");

        var (exitCode, said) = Run(folder, "generate");

        Assert.True(exitCode == 0, said);
        Assert.False(File.Exists(Out(folder, "old/old.zod.ts")));
        Assert.True(Directory.Exists(Out(folder, "old")));
    }

    [Fact]
    public void A_file_that_cannot_be_removed_is_warned_about_and_kept_on_the_list()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        var held = folder.File("out/held/old.zod.ts", "// Generated by haruspex. Do not edit.");
        Listing(folder, "held/old.zod.ts");
        Hold(held);
        try
        {
            var (exitCode, said) = Run(folder, "generate");

            Assert.True(exitCode == 0, said);
            Assert.True(File.Exists(held));
            Assert.Contains("out/held/old.zod.ts: warning HAR6041: ", said, StringComparison.Ordinal);
            Assert.Contains("\"held/old.zod.ts\"", ListText(folder), StringComparison.Ordinal);
        }
        finally
        {
            Release(held);
        }
    }

    /// <summary>
    /// Makes <paramref name="file"/> one the file system won't remove: read-only on Windows, which refuses to remove such a file;
    /// elsewhere, its folder read-only, since removing a file is a change to its folder.
    /// </summary>
    internal static void Hold(string file)
    {
        if (OperatingSystem.IsWindows())
            File.SetAttributes(file, FileAttributes.ReadOnly);
        else
            File.SetUnixFileMode(Path.GetDirectoryName(file)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    /// <summary>Undoes <see cref="Hold"/>, so that the test's folder can be removed.</summary>
    internal static void Release(string file)
    {
        if (OperatingSystem.IsWindows())
            File.SetAttributes(file, FileAttributes.Normal);
        else
            File.SetUnixFileMode(Path.GetDirectoryName(file)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void A_list_that_cannot_be_written_fails_the_run_saying_the_module_is_written()
    {
        using var folder = Shop();
        folder.File("out/haruspex.files.json/kept.txt", "a folder where the list would go");

        var (exitCode, said) = Run(folder, "generate");

        Assert.Equal(1, exitCode);
        Assert.True(File.Exists(Out(folder, "admin.zod.ts")));
        Assert.True(File.Exists(Out(folder, "sales.zod.ts")));
        Assert.Contains("out/haruspex.files.json: error HAR6008: the module is written, but this list of its files can't be: ", said, StringComparison.Ordinal);
        Assert.False(File.Exists(Out(folder, "haruspex.files.json")));
    }

    [Fact]
    public void A_module_renamed_only_by_case_keeps_the_file_this_run_wrote()
    {
        using var folder = Shop();
        var upper = BothModules.Replace("\"admin\": [\"admin\"]", "\"Admin\": [\"admin\"]", StringComparison.Ordinal);

        var (exitCode, said) = Twice(folder, upper, BothModules);

        // On a file system that ignores case (macOS's and Windows' by default) the old Admin.zod.ts is the new admin.zod.ts,
        // which must not be removed as the old name; on one that doesn't, the old file is a file apart, and is removed.
        Assert.True(exitCode == 0, said);
        Assert.True(File.Exists(Out(folder, "admin.zod.ts")));
        Assert.Contains("export const ListClerks", File.ReadAllText(Out(folder, "admin.zod.ts")), StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Out(folder)), f => string.Equals(Path.GetFileName(f), "admin.zod.ts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"admin.zod.ts\"", ListText(folder), StringComparison.Ordinal);
        Assert.DoesNotContain("\"Admin.zod.ts\"", ListText(folder), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_naming_each_file_generate_would_remove()
    {
        using var folder = Shop();
        const string Both = """{ "source": "db", "out": "out", "emit": ["zod", "json-schema"], "schemas": { "sales": { "include": ["*"] } } }""";
        folder.File("haruspex.json", Both);
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        folder.File("haruspex.json", Both.Replace("\"zod\", ", "", StringComparison.Ordinal));

        var (exitCode, said) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("out/haruspex.zod.ts: error HAR6042: ", said, StringComparison.Ordinal);
        Assert.True(File.Exists(Out(folder, "haruspex.zod.ts")));
    }

    [Fact]
    public void Check_fails_when_the_list_is_missing()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        File.Delete(Out(folder, "haruspex.files.json"));

        var (exitCode, said) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("out/haruspex.files.json isn't there", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_when_the_list_was_edited()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);
        Listing(folder, "gone.ts");

        var (exitCode, said) = Run(folder, "check");

        Assert.Equal(1, exitCode);
        Assert.Contains("out/haruspex.files.json: error HAR6015: ", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_passes_on_what_generate_wrote_with_its_list()
    {
        using var folder = Shop();
        Assert.Equal(0, Run(folder, "generate").ExitCode);

        var (exitCode, said) = Run(folder, "check");

        Assert.True(exitCode == 0, said);
    }

    /// <summary>Adds <paramref name="entries"/> to the list in the output folder, as a hand edit would.</summary>
    private static void Listing(TempDirectory folder, params string[] entries)
    {
        var text = ListText(folder);
        var added = string.Concat(entries.Select(e => $"\n    \"{e}\","));
        File.WriteAllText(Out(folder, "haruspex.files.json"), text.Replace("\"files\": [", "\"files\": [" + added, StringComparison.Ordinal));
    }

    /// <summary>Makes a symbolic link, or skips the test where the platform won't (Windows without the right to make one).</summary>
    private static void Link(Action link)
    {
        try
        {
            link();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && OperatingSystem.IsWindows())
        {
            Assert.Skip($"this user can't make a symbolic link: {e.Message}");
        }
    }
}

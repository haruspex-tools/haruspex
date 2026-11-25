using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The exit codes the CLI's README promises, a case for each row: 0 for success (notes and warnings included), 1 when the
/// input has errors, the output can't be written or a check fails, 2 when the command line or the config file is wrong, and 3
/// for a fault.
/// Scripts and CI rely on them, so each is pinned here.
/// </summary>
public class ExitCodeTests
{
    /// <summary>Runs the CLI in <paramref name="folder"/>, holding a project and perhaps a config file, writing to its <c>out</c>.</summary>
    private static int Run(TempDirectory folder, params string[] args) => Runs(folder, args).ExitCode;

    private static (int ExitCode, string Error) Runs(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(["generate", .. args], output, error, folder.Path);
        return (exitCode, error.ToString());
    }

    /// <summary>Asserts the exit code, and that the run failed for the reason the case is about.</summary>
    private static void Fails(int expected, string code, TempDirectory folder, params string[] args)
    {
        var (exitCode, error) = Runs(folder, args);
        Assert.Equal(expected, exitCode);
        Assert.Contains($"error {code}:", error, StringComparison.Ordinal);
    }

    private static TempDirectory Project(string sql, string? config = null)
    {
        var folder = new TempDirectory();
        folder.File("db/tables.sql", "CREATE TABLE dbo.t (id int NOT NULL, note nvarchar(20) NULL);");
        folder.File("db/p.sql", sql);
        if (config is not null)
            folder.File("haruspex.json", config);
        return folder;
    }

    private const string Plain = "CREATE PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;";

    [Fact]
    public void Success_is_0()
    {
        using var folder = Project(Plain);
        Assert.Equal(0, Run(folder, "db", "--include", "*.*", "--out", "out"));
    }

    [Fact]
    public void Success_with_notes_and_warnings_is_still_0()
    {
        using var folder = Project("CREATE PROCEDURE dbo.p @sql nvarchar(max) AS\nEXEC sp_executesql @sql;\nSELECT t.nope AS gone FROM dbo.t AS t;\nINSERT INTO audit.trail (id) VALUES (1);");
        Assert.Equal(0, Run(folder, "db", "--include", "*.*", "--out", "out"));
    }

    [Fact]
    public void Sql_that_does_not_parse_is_1()
    {
        using var folder = Project("CREATE PROCEDURE dbo.p AS SELECT FROM;");
        Fails(1, "HAR1001", folder, "db", "--include", "*.*", "--out", "out");
    }

    [Fact]
    public void An_object_defined_twice_is_1()
    {
        using var folder = Project(Plain + "\nGO\n" + Plain);
        Fails(1, "HAR1002", folder, "db", "--include", "*.*", "--out", "out");
    }

    [Fact]
    public void A_folder_that_can_t_be_entered_is_1()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("a folder's Unix mode is what shuts it here");
            return;
        }

        // The error is placed at the folder, not at a line of SQL, and is still the input's.
        using var folder = Project(Plain);
        folder.File("db/shut/load.sql", Plain);
        var shut = Path.Combine(folder.Path, "db", "shut");
        File.SetUnixFileMode(shut, UnixFileMode.None);
        try
        {
            if (Directory.EnumerateFileSystemEntries(shut).Any())
                Assert.Skip("this user may enter any folder (root)");
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            Fails(1, "HAR1020", folder, "db", "--include", "*.*", "--out", "out");
        }
        finally
        {
            File.SetUnixFileMode(shut, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Output_that_cannot_be_written_is_1()
    {
        using var folder = Project(Plain);
        folder.File("out", "a file where the output folder would be");
        Fails(1, "HAR6008", folder, "db", "--include", "*.*", "--out", "out");
    }

    [Theory]
    [InlineData("HAR6001", "db", "--colour")]
    [InlineData("HAR6001", "db", "--include")]
    [InlineData("HAR6005", "db", "--include", "*.*")]
    [InlineData("HAR6003", "--include", "*.*", "--out", "out")]
    [InlineData("HAR6007", "db", "--out", "out")]
    [InlineData("HAR6006", "db", "--include", "a.b.c", "--out", "out")]
    [InlineData("HAR4005", "db", "--include", "dbo.nothing", "--out", "out")]
    [InlineData("HAR6004", "nowhere", "--include", "*.*", "--out", "out")]
    [InlineData("HAR6002", "db", "--config", "nowhere.json", "--include", "*.*", "--out", "out")]
    public void A_wrong_command_line_is_2(string code, params string[] args)
    {
        using var folder = Project(Plain);
        Fails(2, code, folder, args);
    }

    [Theory]
    [InlineData("HAR3002", "{ \"source\": ")]
    [InlineData("HAR3004", "{ \"source\": \"db\", \"out\": \"out\", \"colour\": \"blue\" }")]
    [InlineData("HAR4002", "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"dbo\": { \"include\": [\"nothing_*\"] } } }")]
    [InlineData("HAR4104", "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"dbo\": { \"include\": [\"*\"] } }, \"procedures\": { \"dbo.p\": { \"paths\": { \"[].nope\": { \"name\": \"N\" } } } } }")]
    [InlineData("HAR5003", "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"dbo\": { \"include\": [\"*\"] } }, \"procedures\": { \"dbo.p\": { \"paths\": { \"[].note\": { \"type\": \"A\", \"import\": \"./a\" }, \"[].id\": { \"type\": \"A\", \"import\": \"./b\" } } } } }")]
    public void A_wrong_config_file_is_2(string code, string config)
    {
        using var folder = Project(Plain.Replace("t.id", "t.id, t.note", StringComparison.Ordinal), config);
        Fails(2, code, folder);
    }

    [Fact]
    public void Clashing_generated_names_are_2()
    {
        using var folder = Project("CREATE PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;\nGO\nCREATE PROCEDURE dbo.[p#] AS SELECT t.id FROM dbo.t AS t;");
        folder.File("haruspex.json", "{ \"source\": \"db\", \"out\": \"out\", \"schemas\": { \"dbo\": { \"include\": [\"*\"] } }, \"naming\": { \"replace\": [{ \"pattern\": \"#$\", \"with\": \"\" }] } }");
        Fails(2, "HAR4303", folder);
    }

    [Fact]
    public void A_check_that_finds_a_change_is_1()
    {
        using var folder = Project(Plain);
        Assert.Equal(0, Run(folder, "db", "--include", "*.*", "--out", "out"));
        folder.File("db/p.sql", Plain.Replace("t.id", "t.note", StringComparison.Ordinal));

        Assert.Equal(1, Checks(folder, "db", "--include", "*.*", "--out", "out").ExitCode);
    }

    [Fact]
    public void A_check_with_nothing_committed_is_1()
    {
        using var folder = Project(Plain);
        var (exitCode, error) = Checks(folder, "db", "--include", "*.*", "--out", "out");

        Assert.Equal(1, exitCode);
        Assert.Contains("error HAR6011:", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HAR6001", "db", "--colour")]
    [InlineData("HAR6005", "db", "--include", "*.*")]
    [InlineData("HAR4005", "db", "--include", "dbo.nothing", "--out", "out")]
    public void A_check_with_a_wrong_command_line_is_2(string code, params string[] args)
    {
        using var folder = Project(Plain);
        var (exitCode, error) = Checks(folder, args);

        Assert.Equal(2, exitCode);
        Assert.Contains($"error {code}:", error, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Error) Checks(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(["check", .. args], output, error, folder.Path);
        return (exitCode, error.ToString());
    }

    [Fact]
    public void A_fault_is_3()
    {
        // A NUL in a path is something no shell can pass and no step expects: the path throws where it is resolved, which is
        // a fault, and the CLI's last line of defence turns it into exit code 3.
        using var folder = Project(Plain);
        Assert.Equal(3, Run(folder, "db", "--include", "*.*", "--out", "out\0"));
    }
}

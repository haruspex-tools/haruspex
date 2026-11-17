using Haruspex.Cli;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Cli;

/// <summary>
/// What a developer reads after each run, mode by mode, snapshotted: the short default, <c>--verbose</c> and <c>--quiet</c>, on a
/// small invented kennel whose procedures give an unknown of each common kind, a warning and an error. Each snapshot holds the
/// exit code, standard output and standard error, in the order the command writes them.
/// </summary>
public class OutputTests
{
    private const string Tables = """
        CREATE TABLE kennel.dog (
            dog_id int NOT NULL PRIMARY KEY,
            name nvarchar(50) NOT NULL,
            notes nvarchar(max) NULL);
        """;

    private const string Procedures = """
        CREATE PROCEDURE kennel.list_dogs AS
        SELECT d.dog_id, d.name, json_query(d.notes) AS notes FROM kennel.dog AS d FOR JSON PATH;
        GO
        CREATE PROCEDURE kennel.get_dog @dog_id int AS
        SELECT d.dog_id, d.name, d.nickname FROM kennel.dog AS d WHERE d.dog_id = @dog_id;
        GO
        CREATE PROCEDURE kennel.find_dogs @where nvarchar(200) AS
        DECLARE @sql nvarchar(max) = N'SELECT d.dog_id FROM kennel.dog AS d WHERE ' + @where;
        EXEC sp_executesql @sql;
        GO
        CREATE PROCEDURE kennel.count_dogs AS
        SELECT count(*) AS dogs FROM kennel.dog AS d;
        """;

    private const string Config = """{ "source": "db", "out": "src/db", "schemas": { "kennel": { "include": ["*"] } } }""";

    /// <summary>A folder holding the kennel and its config file.</summary>
    internal static TempDirectory Kennel(string procedures = Procedures)
    {
        var folder = new TempDirectory();
        folder.File("db/tables.sql", Tables);
        folder.File("db/procedures.sql", procedures);
        folder.File("haruspex.json", Config);
        return folder;
    }

    /// <summary>The exit code and both streams, as a snapshot holds them, paths shown from the folder.</summary>
    internal static string Printed(TempDirectory folder, Terminal? terminal, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = HaruspexCli.Run(args, output, error, folder.Path, terminal);
        return CliOutput.Shown($"exit code {exitCode}\n--- standard output\n{output}--- standard error\n{error}", folder.Path);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("--verbose")]
    [InlineData("--quiet")]
    public void Generate_says_what_the_mode_asks_for(string mode)
    {
        using var folder = Kennel();

        var printed = Printed(folder, null, mode == "default" ? ["generate"] : ["generate", mode]);

        Snapshot.Match(printed, $"{nameof(OutputTests)}.Generate.{mode.TrimStart('-')}", "txt");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("--verbose")]
    [InlineData("--quiet")]
    public void A_failed_generate_says_its_errors_in_every_mode(string mode)
    {
        using var folder = Kennel(Procedures + "\nGO\nCREATE PROCEDURE kennel.broken AS SELECT FROM;");

        var printed = Printed(folder, null, mode == "default" ? ["generate"] : ["generate", mode]);

        Snapshot.Match(printed, $"{nameof(OutputTests)}.Failed.{mode.TrimStart('-')}", "txt");
    }

    [Fact]
    public void Verbose_and_quiet_can_t_go_together()
    {
        using var folder = Kennel();

        Assert.StartsWith("exit code 2\n", Printed(folder, null, "generate", "--verbose", "--quiet"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_check_that_passes_is_one_line()
    {
        using var folder = Kennel(Known);
        Printed(folder, null, "generate");

        Assert.Equal(
            "exit code 0\n--- standard output\nharuspex: 3 procedures, 2 unknowns (--verbose lists them). src/db is up to date.\n--- standard error\n",
            Printed(folder, null, "check"));
    }

    /// <summary>The kennel without the procedure whose results only a declaration can give, which a check doesn't pass.</summary>
    private static string Known => Procedures[..Procedures.IndexOf("CREATE PROCEDURE kennel.find_dogs", StringComparison.Ordinal)] +
        Procedures[Procedures.IndexOf("CREATE PROCEDURE kennel.count_dogs", StringComparison.Ordinal)..];
}

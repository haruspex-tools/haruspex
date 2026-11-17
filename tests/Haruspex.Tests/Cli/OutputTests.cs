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

    [Fact]
    public void A_second_run_that_changes_nothing_says_so_in_one_line()
    {
        using var folder = Kennel();
        Printed(folder, null, "generate");

        Assert.Equal(
            "exit code 0\n--- standard output\nharuspex: nothing changed since the last run: 4 procedures, 3 unknowns (--verbose lists them).\n--- standard error\n" + FindDogsWarning,
            Printed(folder, null, "generate"));
    }

    [Fact]
    public void A_second_run_says_what_changed_since_the_last()
    {
        using var folder = Kennel();
        Printed(folder, null, "generate");

        // A database change: name may now be null, count_dogs goes, walk_dogs arrives with an unknown of its own, and get_dog
        // reads a column that exists, so its unknown is resolved.
        folder.File("db/tables.sql", Tables.Replace("name nvarchar(50) NOT NULL", "name nvarchar(50) NULL", StringComparison.Ordinal));
        folder.File("db/procedures.sql", Procedures
            .Replace("d.nickname", "d.notes", StringComparison.Ordinal)
            .Replace("CREATE PROCEDURE kennel.count_dogs AS\nSELECT count(*) AS dogs FROM kennel.dog AS d;", "CREATE PROCEDURE kennel.walk_dogs AS\nSELECT w.walker FROM kennel.walk AS w;", StringComparison.Ordinal));

        Snapshot.Match(Printed(folder, null, "generate"), $"{nameof(OutputTests)}.Changed", "txt");
    }

    [Fact]
    public void A_module_whose_text_alone_changed_says_which_file()
    {
        using var folder = Kennel(Known);
        Printed(folder, null, "generate");
        File.AppendAllText(Path.Combine(folder.Path, "src", "db", HaruspexCli.ZodFileName), "// edited by hand\n");

        Assert.Contains(
            "\nNo claim changed since the last run, but the text of haruspex.zod.ts did: a comment written from a reason, or the output of another version.\n2 unknowns, as before (--verbose lists them).\n",
            Printed(folder, null, "generate"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "irVersion": "0.21" }""", "Nothing to compare with: the earlier haruspex.ir.json is IR 0.21, and this haruspex writes IR ")]
    [InlineData("not JSON", "Nothing to compare with: the earlier haruspex.ir.json can't be read as IR (")]
    public void An_earlier_ir_that_can_t_be_compared_says_why_and_gives_the_whole_summary(string earlier, string said)
    {
        using var folder = Kennel(Known);
        folder.File("src/db/haruspex.ir.json", earlier);

        var printed = Printed(folder, null, "generate");

        Assert.StartsWith("exit code 0\n", printed, StringComparison.Ordinal);
        Assert.Contains("\n" + said, printed, StringComparison.Ordinal);
        Assert.Contains("\nUnknowns by reason (--verbose lists each):\n", printed, StringComparison.Ordinal);
    }

    /// <summary>A terminal that shows colour and links, whose clock never moves, so no progress line is drawn.</summary>
    private static Terminal Colourful(params string[] variables) => new()
    {
        OutputIsTerminal = true,
        ErrorIsTerminal = true,
        Clock = new ManualClock(),
        Environment = variables.Append("TERM_PROGRAM=WezTerm").Select(v => v.Split('=', 2)).ToDictionary(p => p[0], p => p[1]),
    };

    [Fact]
    public void On_a_terminal_severities_are_coloured_and_codes_link_to_their_pages()
    {
        using var folder = Kennel();
        Printed(folder, null, "generate");
        folder.File("db/tables.sql", Tables.Replace("name nvarchar(50) NOT NULL", "name nvarchar(50) NULL", StringComparison.Ordinal));

        // The escape character shown as ␛, so the snapshot can be read.
        var printed = Printed(folder, Colourful(), "generate", "--verbose").Replace("\e", "␛", StringComparison.Ordinal);

        Snapshot.Match(printed, $"{nameof(OutputTests)}.Colour", "txt");
    }

    [Theory]
    [InlineData("--color=never")]
    [InlineData("NO_COLOR=1")]
    public void Colour_asked_off_is_the_same_bytes_as_off_a_terminal(string off)
    {
        using var folder = Kennel();
        using var plain = Kennel();

        var coloured = off.StartsWith("--", StringComparison.Ordinal)
            ? Printed(folder, Colourful(), "generate", "--verbose", off)
            : Printed(folder, Colourful(off), "generate", "--verbose");

        Assert.Equal(Printed(plain, null, "generate", "--verbose"), coloured);
    }

    [Fact]
    public void Colour_asked_for_always_colours_a_pipe_but_makes_no_link()
    {
        using var folder = Kennel();

        var printed = Printed(folder, null, "generate", "--color", "always");

        Assert.Contains("\e[1;33m" + "warning\e[0m HAR4204", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("\e]8;;", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void An_earlier_ir_that_isn_t_json_is_said_by_where_the_parser_stopped_and_no_more()
    {
        using var folder = Kennel(Known);
        folder.File("src/db/haruspex.ir.json", "{ \"irVersion\": ");

        var printed = Printed(folder, null, "generate");

        Assert.Contains("\nNothing to compare with: the earlier haruspex.ir.json can't be read as IR ($ is not JSON at line 1", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("BytePositionInLine", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_control_character_in_the_sql_is_escaped_so_no_line_of_the_text_is_another_s()
    {
        // A bracketed identifier may hold a line break; printed raw, its second line would be a GitHub workflow command.
        using var folder = Kennel("CREATE PROCEDURE kennel.get_dog AS\nSELECT d.[nick\n::error::boom\u001b[31m] FROM kennel.dog AS d;");
        using var output = new StringWriter();
        using var error = new StringWriter();

        HaruspexCli.Run(["generate", "--verbose"], output, error, folder.Path, new Terminal { Environment = new Dictionary<string, string> { ["GITHUB_ACTIONS"] = "true" } });

        var lines = (output.ToString() + error).Split('\n');
        Assert.DoesNotContain(lines, line => line.StartsWith("::error::boom", StringComparison.Ordinal));
        Assert.DoesNotContain("\u001b", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"has no column nick\n::error::boom\x1B[31m.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_line_ends_with_lf_alone_on_every_platform()
    {
        using var folder = Kennel(Procedures + "\nGO\nCREATE PROCEDURE kennel.broken AS SELECT FROM;");
        using var fine = Kennel();

        var printed = RawPrinted(fine, "generate", "--verbose") + RawPrinted(folder, "generate") + RawPrinted(fine, "--help");

        Assert.Contains("\n", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", printed, StringComparison.Ordinal);
    }

    /// <summary>What a run prints, both streams, as written: line endings as they are.</summary>
    private static string RawPrinted(TempDirectory folder, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        HaruspexCli.Run(args, output, error, folder.Path);
        return output.ToString() + error;
    }

    private const string FindDogsWarning = "haruspex.json: warning HAR4204: kennel.find_dogs: its result sets are unknown: EXEC sp_executesql @sql runs dynamic SQL, a string built when the procedure runs, so its result sets are not known. (procedures.sql:9:1) Declare them in haruspex.json under procedures[\"kennel.find_dogs\"].results, as \"none\" or a JSON Schema, or declare the results of the procedure that runs it.\n";

    /// <summary>The kennel without the procedure whose results only a declaration can give, which a check doesn't pass.</summary>
    private static string Known => Procedures[..Procedures.IndexOf("CREATE PROCEDURE kennel.find_dogs", StringComparison.Ordinal)] +
        Procedures[Procedures.IndexOf("CREATE PROCEDURE kennel.count_dogs", StringComparison.Ordinal)..];
}

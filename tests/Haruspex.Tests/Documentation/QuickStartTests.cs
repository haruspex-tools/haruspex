using System.Globalization;
using Haruspex.Cli;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// The five-minute start, <c>docs/quick-start.md</c>, run as a reader runs it: its command, with its config file, generates
/// the schemas and the function it shows from the vendored AdventureWorks, and its script, run by Node beside the schemas,
/// prints what it shows. A first run that
/// fails loses the reader at once, so the page is a test like the README's showcase.
/// </summary>
/// <remarks>
/// The page marks what is run with <c>haruspex-start</c> after a fenced block's language: <c>jsonc haruspex-start
/// haruspex.json</c> holds the config file, <c>sh haruspex-start generate</c> the command, <c>ts haruspex-start module</c> the
/// Zod module it writes, <c>ts haruspex-start function</c> the end of the functions' module (as
/// <see cref="WorkedExample.MssqlShown"/> reads it), <c>ts haruspex-start check.ts</c> the script, and <c>text haruspex-start
/// output</c> what the script prints. The command is run from the repository's root as the page says, with only <c>--out</c>
/// and <c>--config</c> moved to a folder of the test's own, which holds the page's config file. The script is run with the zod that <c>tests/emit-check</c>
/// pins, which the page tells the reader to install (<c>NamedVersionsTests</c>), so a new zod that prints its errors
/// otherwise fails here first.
/// </remarks>
public class QuickStartTests
{
    private const string Page = "docs/quick-start.md";
    private const string Mark = "haruspex-start";
    private const string Command = "dotnet run --project src/Haruspex.Cli -- ";

    private static List<MarkdownText.FencedBlock> Blocks => [.. MarkdownText.FencedBlocks(File.ReadAllText(Path.Combine(RepositoryPaths.Root, Page)))];

    [Fact]
    public void The_quick_start_s_command_generates_the_module_it_shows()
    {
        var shown = Block("module").Text;

        Assert.Equal(Text(shown), Text(WorkedExample.ZodShown(Generate().Zod, shown)));
    }

    [Fact]
    public void The_quick_start_s_command_generates_the_function_it_shows()
    {
        var shown = Block("function").Text;

        Assert.Equal(Text(shown), Text(WorkedExample.MssqlShown(Generate().Functions, shown)));
    }

    [Fact]
    public void The_quick_start_s_script_prints_what_it_shows()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the quick start's script was not run");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        // Under tests/emit-check, so that the script finds its zod as the reader's finds the one installed beside it.
        var folder = Path.Combine(RepositoryPaths.EmitCheck, "quick-start", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, HaruspexCli.ZodFileName), Generate().Zod);
            File.WriteAllText(Path.Combine(folder, "check.ts"), Block("check.ts").Text);

            Assert.True(EmitCheckPackage.TryRun("node", ["check.ts"], folder, out var printed, out var why), $"node check.ts failed ({why}):\n{printed}");
            Assert.Equal(Text(Block("output").Text), Text(printed));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_quick_start_s_command_is_run_from_the_repository_s_root_with_a_folder_of_its_own() =>
        Assert.Contains("--config out/haruspex.json --out out", CommandLine(), StringComparison.Ordinal);

    /// <summary>
    /// Runs the page's command from the repository's root, with the page's config file and writing to a folder of the test's
    /// own, and returns the two modules it writes.
    /// </summary>
    private static (string Zod, string Functions) Generate()
    {
        var arguments = CommandLine().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        using var temp = new TempDirectory();
        arguments[arguments.IndexOf("--out") + 1] = temp.Path;
        arguments[arguments.IndexOf("--config") + 1] = temp.File("haruspex.json", Block("haruspex.json").Text);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HaruspexCli.Run([.. arguments], output, error, RepositoryPaths.Root);

        Assert.True(exitCode == 0, $"{Page}: the command exited with {exitCode.ToString(CultureInfo.InvariantCulture)}:\n{error}");
        Assert.True(error.ToString().Length == 0, $"{Page}: the command reported problems the page doesn't show:\n{error}");
        return (File.ReadAllText(Path.Combine(temp.Path, HaruspexCli.ZodFileName)), File.ReadAllText(Path.Combine(temp.Path, HaruspexCli.MssqlFileName)));
    }

    /// <summary>The page's <c>haruspex generate</c> command line, after <c>--</c>.</summary>
    private static string CommandLine()
    {
        var line = Block("generate").Text.Split('\n').Select(l => l.Trim()).SingleOrDefault(l => l.StartsWith(Command, StringComparison.Ordinal));
        Assert.True(line is not null, $"{Page}: the generate block has no line starting \"{Command}\".");
        return line[Command.Length..];
    }

    /// <summary>Whether a fenced block is the module the page shows, which <c>CodeReferences</c> reads generated names from.</summary>
    internal static bool IsModule(MarkdownText.FencedBlock block) => block.Words.Count == 3 && block.Words[1] == Mark && block.Words[2] == "module";

    private static MarkdownText.FencedBlock Block(string name)
    {
        var block = Blocks.SingleOrDefault(b => b.Words.Count == 3 && b.Words[1] == Mark && b.Words[2] == name);
        Assert.True(block is not null, $"{Page} has no block marked \"{Mark} {name}\".");
        return block;
    }

    private static string Text(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}

using System.CommandLine;
using Haruspex.Cli.Output;
using Haruspex.Config;

namespace Haruspex.Cli;

/// <summary>The <c>haruspex</c> command line. Results go to standard output, errors to standard error.</summary>
internal static class HaruspexCli
{
    /// <summary>Exit codes, fixed so scripts and CI can rely on them.</summary>
    internal static class ExitCodes
    {
        public const int Success = 0;

        /// <summary>The input has errors (SQL that does not parse), or the output could not be written.</summary>
        public const int Failed = 1;

        /// <summary>
        /// The command line or the config file is wrong: a missing option, a bad pattern, a pattern that matches nothing,
        /// an override whose path doesn't exist, generated names that clash.
        /// </summary>
        public const int Usage = 2;

        /// <summary>A fault in haruspex itself.</summary>
        public const int Internal = 3;
    }

    public const string IrFileName = "haruspex.ir.json";
    public const string ZodFileName = "haruspex.zod.ts";
    public const string JsonSchemaFileName = "haruspex.jsonschema.json";

    /// <summary>
    /// The name of the database module's functions file, written beside the Zod module, whose schemas it imports by the specifier
    /// <see cref="ZodModuleSpecifier"/>.
    /// </summary>
    public const string MssqlFileName = "haruspex.mssql.ts";

    /// <summary>
    /// How the functions file imports the Zod module beside it: with the <c>.js</c> of the file it compiles to, which an ECMAScript
    /// module needs and TypeScript maps to the <c>.ts</c> under every module resolution (<c>nodenext</c>, <c>node16</c> and
    /// <c>bundler</c>), as bundlers do; an import without an extension resolves under a bundler only.
    /// </summary>
    public const string ZodModuleSpecifier = "./haruspex.zod.js";

    /// <summary>Runs the command in the process's own console, from its working directory.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error) =>
        Run(args, output, error, Directory.GetCurrentDirectory(), Terminal.FromConsole());

    /// <summary>
    /// Runs the command as if from <paramref name="workingDirectory"/>, where relative paths start and the config file is looked
    /// for, in <paramref name="terminal"/>: by default neither stream a terminal and no variable set (<see cref="Terminal.Plain"/>).
    /// </summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory, Terminal? terminal = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        // LF on every platform, as in every file haruspex writes, so a run prints the same bytes on Windows as elsewhere and a CI
        // log or a test compares them as they are.
        output.NewLine = "\n";
        error.NewLine = "\n";

        var root = Build(output, error, workingDirectory, terminal ?? Terminal.Plain);

        // The version is the terminal's, as everything else the command says about where it runs: the binary's own version
        // (Terminal.ToolVersion), which is its npm packages', and not the entry assembly's, which in a test is the test's.
        var version = terminal ?? Terminal.Plain;
        foreach (var option in root.Options.OfType<VersionOption>())
            option.Action = new PrintVersion(output, version.Version);
        var parsed = root.Parse(args);

        // System.CommandLine exits with 1 on a parse error; usage errors are 2 here, and 1 is kept for failures.
        if (parsed.Errors.Count > 0)
        {
            foreach (var parseError in parsed.Errors)
                error.WriteLine(CliCodes.BadCommandLine.At(null, parseError.Message));
            error.WriteLine("Run 'haruspex --help' for usage.");
            return ExitCodes.Usage;
        }

        try
        {
            return parsed.Invoke(new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false });
        }
#pragma warning disable CA1031 // The last line of defence: any fault becomes a message and a distinct exit code.
        catch (Exception e)
#pragma warning restore CA1031
        {
            error.WriteLine($"haruspex: internal error: {e}");
            return ExitCodes.Internal;
        }
    }

    private static RootCommand Build(TextWriter output, TextWriter error, string workingDirectory, Terminal terminal) =>
        new("""
            haruspex types the results of SQL Server procedures and query files for TypeScript, from the database's own files.

            Examples:
              haruspex init                                          write a starter haruspex.json
              haruspex generate                                      write the module, and say what changed
              haruspex generate db --include 'sales.*' --out src/db  without a config file
              haruspex check --all                                   in CI: fail if a committed module would change

            Exit codes: 0 success, 1 failed (errors in the input, or a check that fails), 2 a usage error, 3 a fault in haruspex.
            More: https://github.com/sharpblue/haruspex/blob/main/docs/command-line.md
            """)
        {
            InitCommand(new Streams(output, error, workingDirectory, terminal)),
            Module(
                "generate",
                "Infer the shapes, write the module to the output folder, and say what changed since the last run.",
                CommandName.Generate,
                Generate.Run,
                new Streams(output, error, workingDirectory, terminal)),
            Module(
                "check",
                "Infer as generate does, write nothing, and fail (exit code 1) if the committed module would change.",
                CommandName.Check,
                Check.Run,
                new Streams(output, error, workingDirectory, terminal)),
        };

    /// <summary><c>haruspex init</c>, which writes a starter config file.</summary>
    private static Command InitCommand(Streams streams)
    {
        var source = new Argument<string?>("source")
        {
            Description = "The .sqlproj, .dacpac or folder of .sql files to read. By default, the one found below the working directory.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var init = new Command("init", "Find the database's files below here and write a starter haruspex.json. Never writes over one.")
        {
            source,
        };
        init.SetAction(parsed => Init.Run(parsed.GetValue(source), streams.Output, streams.Error, streams.WorkingDirectory));
        return init;
    }

    /// <summary>Writes the run's report in <paramref name="format"/>.</summary>
    private static void Report(RunReport report, OutputFormat format, OutputSettings settings, Streams streams)
    {
        // In GitHub Actions, files are named from the repository's root, which is how GitHub finds them; the run may be in a
        // folder below it.
        var root = streams.Terminal.Variable("GITHUB_WORKSPACE") ?? streams.WorkingDirectory;
        switch (format)
        {
            case OutputFormat.Json:
                new JsonReport(streams.WorkingDirectory).Write(report, streams.Output);
                break;
            case OutputFormat.Sarif:
                new SarifReport(root, streams.Terminal.Version).Write(report, streams.Output);
                break;
            case OutputFormat.GitHub:
                new TextReport(settings.Detail, settings.Output, settings.Error, streams.WorkingDirectory).Write(report, streams.Output, streams.Error);
                new GitHubAnnotations(root, settings.Detail).Write(report, streams.Output);
                break;
            case OutputFormat.Text:
                new TextReport(settings.Detail, settings.Output, settings.Error, streams.WorkingDirectory).Write(report, streams.Output, streams.Error);
                break;
        }
    }

    /// <summary>What <c>--format</c> names; when it names none, annotations in GitHub Actions and text anywhere else.</summary>
    private static OutputFormat Format(string? named, Terminal terminal) => named switch
    {
        "json" => OutputFormat.Json,
        "sarif" => OutputFormat.Sarif,
        "github" => OutputFormat.GitHub,
        "text" => OutputFormat.Text,
        _ => terminal.Variable("GITHUB_ACTIONS") == "true" ? OutputFormat.GitHub : OutputFormat.Text,
    };

    /// <summary>What the colour option names.</summary>
    private static ColourChoice Colour(string? named) => named switch
    {
        "always" => ColourChoice.Always,
        "never" => ColourChoice.Never,
        _ => ColourChoice.Auto,
    };

    /// <summary><c>--version</c>: the version, alone on a line.</summary>
    private sealed class PrintVersion(TextWriter output, string version) : System.CommandLine.Invocation.SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            output.WriteLine(version);
            return ExitCodes.Success;
        }
    }

    /// <summary>Where a command writes, where it runs from, and what it knows of the terminal.</summary>
    private sealed class Streams(TextWriter output, TextWriter error, string workingDirectory, Terminal terminal)
    {
        public TextWriter Output { get; } = output;

        public TextWriter Error { get; } = error;

        public string WorkingDirectory { get; } = workingDirectory;

        public Terminal Terminal { get; } = terminal;
    }

    /// <summary>A command that makes a module from a project: <c>generate</c> and <c>check</c> take the same arguments.</summary>
    private static Command Module(string name, string description, CommandName command, Func<RunRequest, Progress, ModuleReport> run, Streams streams)
    {
        var workingDirectory = streams.WorkingDirectory;
        var directory = new Argument<string?>("source")
        {
            Description = "A folder of .sql files, a .sqlproj or a .dacpac. Overrides \"source\" in the config file.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var config = new Option<string[]>("--config", "-c")
        {
            Description = $"The config file; by default, {HaruspexConfig.FileName} here or in the nearest folder above. Repeat for more modules.",
            HelpName = "file",
            Arity = ArgumentArity.OneOrMore,
        };
        var all = new Option<string?>("--all", "-a")
        {
            Description = $"Every {HaruspexConfig.FileName} below the folder (by default, the working directory), each a module.",
            HelpName = "folder",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var include = new Option<string[]>("--include", "-i")
        {
            Description = "Procedures to type, as schema.name, with * and ? as wildcards. Repeat for more. Overrides the config file's.",
            HelpName = "pattern",
            Arity = ArgumentArity.OneOrMore,
        };
        var outDirectory = new Option<string?>("--out", "-o")
        {
            Description = "The output folder. Overrides \"out\" in the config file.",
            HelpName = "folder",
        };

        var verbose = new Option<bool>("--verbose", "-v")
        {
            Description = "Also list every note: each unknown, by procedure.",
        };
        var quiet = new Option<bool>("--quiet", "-q")
        {
            Description = "Print errors only.",
        };
        var colour = new Option<string>("--color")
        {
            Description = "Colour on a terminal (auto, unless NO_COLOR is set), always or never.",
            DefaultValueFactory = _ => "auto",
        };
        colour.AcceptOnlyFromAmong("auto", "always", "never");
        var format = new Option<string?>("--format")
        {
            Description = "text; json for tools; sarif for code scanning; github, the default in GitHub Actions, for annotations.",
        };
        format.AcceptOnlyFromAmong("text", "json", "sarif", "github");

        var made = new Command(name, description)
        {
            directory,
            config,
            all,
            include,
            outDirectory,
            verbose,
            quiet,
            colour,
            format,
        };
        made.Validators.Add(result =>
        {
            if (result.GetValue(verbose) && result.GetValue(quiet))
                result.AddError("--verbose and --quiet can't go together.");
        });
        made.SetAction(parsed =>
        {
            var request = new ModulesRequest(
                parsed.GetValue(directory),
                [.. parsed.GetValue(config) ?? []],
                parsed.GetResult(all) is null ? null : parsed.GetValue(all) ?? "",
                [.. parsed.GetValue(include) ?? []],
                parsed.GetValue(outDirectory),
                workingDirectory);
            var detail = parsed.GetValue(quiet) ? OutputDetail.Quiet : parsed.GetValue(verbose) ? OutputDetail.Verbose : OutputDetail.Normal;
            var settings = OutputSettings.Settle(detail, Colour(parsed.GetValue(colour)), streams.Terminal);
            RunReport report;
            using (var progress = Progress.On(streams.Error, settings, streams.Terminal))
                report = ModuleSet.Run(command, request, module => run(module, progress));
            Report(report, Format(parsed.GetValue(format), streams.Terminal), settings, streams);
            return report.ExitCode;
        });
        return made;
    }
}

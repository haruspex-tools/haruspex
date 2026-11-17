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

        var root = Build(output, error, workingDirectory, terminal ?? Terminal.Plain);
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
        new("haruspex: infers the result shapes of SQL Server procedures, FOR JSON included.")
        {
            Module(
                "generate",
                "Infer the result shapes of procedures, and write the IR and what the config file's \"emit\" names: a Zod module by default, a JSON Schema document too or instead.",
                CommandName.Generate,
                Generate.Run,
                new Streams(output, error, workingDirectory, terminal)),
            Module(
                "check",
                $"Infer as generate does, write nothing, and fail if {IrFileName} and the files emitted beside it in the output folder claim something else, saying what changed.",
                CommandName.Check,
                Check.Run,
                new Streams(output, error, workingDirectory, terminal)),
        };

    /// <summary>What the colour option names.</summary>
    private static ColourChoice Colour(string? named) => named switch
    {
        "always" => ColourChoice.Always,
        "never" => ColourChoice.Never,
        _ => ColourChoice.Auto,
    };

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
            Description = "The folder of .sql files to read, including subfolders; a database project's .sqlproj, read as its build reads it; or a .dacpac, read with the packages of its database it refers to. Overrides \"source\" in the config file.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var config = new Option<string[]>("--config", "-c")
        {
            Description = $"The config file. By default, {HaruspexConfig.FileName} in the working directory or the nearest folder above it. Repeat for more modules, each run and reported apart.",
            Arity = ArgumentArity.OneOrMore,
        };
        var all = new Option<string?>("--all", "-a")
        {
            Description = $"Every {HaruspexConfig.FileName} below the folder given, or below the working directory, each a module run and reported apart. Folders whose name starts with a dot, node_modules, and another repository or worktree nested below (a folder with .git) are left out.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var include = new Option<string[]>("--include", "-i")
        {
            Description = "Procedures to type, as schema.name; * and ? match within a part. Repeat for more. Overrides the config file's schema includes for this run.",
            Arity = ArgumentArity.OneOrMore,
        };
        var outDirectory = new Option<string?>("--out", "-o")
        {
            Description = $"The folder of {IrFileName} and the files emitted beside it. Overrides \"out\" in the config file.",
        };

        var verbose = new Option<bool>("--verbose", "-v")
        {
            Description = "Also list every note: each unknown, grouped by the procedure or query file it is in.",
        };
        var quiet = new Option<bool>("--quiet", "-q")
        {
            Description = "Print errors only.",
        };
        var colour = new Option<string>("--color")
        {
            Description = "Colour: auto (on a terminal, unless NO_COLOR is set), always or never.",
            DefaultValueFactory = _ => "auto",
        };
        colour.AcceptOnlyFromAmong("auto", "always", "never");

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
            new TextReport(settings.Detail, settings.Output, settings.Error, workingDirectory).Write(report, streams.Output, streams.Error);
            return report.ExitCode;
        });
        return made;
    }
}

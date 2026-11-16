using System.CommandLine;
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

    public static int Run(string[] args, TextWriter output, TextWriter error) =>
        Run(args, output, error, Directory.GetCurrentDirectory());

    /// <summary>Runs the command as if from <paramref name="workingDirectory"/>, where relative paths start and the config file is looked for.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var root = Build(output, error, workingDirectory);
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

    private static RootCommand Build(TextWriter output, TextWriter error, string workingDirectory) =>
        new("haruspex: infers the result shapes of SQL Server procedures, FOR JSON included.")
        {
            Module(
                "generate",
                "Infer the result shapes of procedures, and write the IR and a Zod module.",
                request => ModuleSet.Run(request, Generate.Run, output, error),
                workingDirectory),
            Module(
                "check",
                $"Infer as generate does, write nothing, and fail if {IrFileName} and {ZodFileName} in the output folder claim something else, saying what changed.",
                request => ModuleSet.Run(request, Check.Run, output, error),
                workingDirectory),
        };

    /// <summary>A command that makes a module from a project: <c>generate</c> and <c>check</c> take the same arguments.</summary>
    private static Command Module(string name, string description, Func<ModulesRequest, int> run, string workingDirectory)
    {
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
            Description = $"The folder of {IrFileName} and {ZodFileName}. Overrides \"out\" in the config file.",
        };

        var command = new Command(name, description)
        {
            directory,
            config,
            all,
            include,
            outDirectory,
        };
        command.SetAction(parsed => run(
            new ModulesRequest(
                parsed.GetValue(directory),
                [.. parsed.GetValue(config) ?? []],
                parsed.GetResult(all) is null ? null : parsed.GetValue(all) ?? "",
                [.. parsed.GetValue(include) ?? []],
                parsed.GetValue(outDirectory),
                workingDirectory)));
        return command;
    }
}

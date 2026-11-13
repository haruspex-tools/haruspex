using System.CommandLine;
using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

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
                error.WriteLine($"haruspex: {parseError.Message}");
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

    private static RootCommand Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var directory = new Argument<string?>("dir")
        {
            Description = "The folder of .sql files to read, including subfolders. Overrides \"source\" in the config file.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var config = new Option<string?>("--config", "-c")
        {
            Description = $"The config file. By default, {HaruspexConfig.FileName} in the working directory or the nearest folder above it.",
        };
        var include = new Option<string[]>("--include", "-i")
        {
            Description = "Procedures to type, as schema.name; * and ? match within a part. Repeat for more. Overrides the config file's schema includes for this run.",
            Arity = ArgumentArity.OneOrMore,
        };
        var outDirectory = new Option<string?>("--out", "-o")
        {
            Description = $"The folder to write {IrFileName} and {ZodFileName} to. Overrides \"out\" in the config file.",
        };

        var generate = new Command("generate", "Infer the result shapes of procedures, and write the IR and a Zod module.")
        {
            directory,
            config,
            include,
            outDirectory,
        };
        generate.SetAction(parsed => Generate(
            new Request(
                parsed.GetValue(directory),
                parsed.GetValue(config),
                [.. parsed.GetValue(include) ?? []],
                parsed.GetValue(outDirectory),
                workingDirectory),
            output,
            error));

        return new RootCommand("haruspex: infers the result shapes of SQL Server procedures, FOR JSON included.")
        {
            generate,
        };
    }

    private sealed record Request(string? Directory, string? Config, EquatableArray<string> Include, string? Out, string WorkingDirectory)
    {
        public string Resolve(string path) => Path.GetFullPath(Path.Combine(WorkingDirectory, path));
    }

    private static int Generate(Request request, TextWriter output, TextWriter error)
    {
        // The config file: named, or found by walking up from the working directory, or none.
        var configPath = request.Config is { } named ? request.Resolve(named) : ConfigReader.Find(request.WorkingDirectory);
        // Names are settled for TypeScript, whose reserved words are the Zod emitter's to know; the config is told them.
        var reserved = new ReservedWords(TypeScriptNames.Language, TypeScriptNames.IsReserved);
        var config = HaruspexConfig.None(request.WorkingDirectory, reserved);
        if (configPath is not null)
        {
            if (!File.Exists(configPath))
            {
                error.WriteLine($"haruspex: config file not found: {request.Config}");
                return ExitCodes.Usage;
            }

            var read = ConfigReader.Read(configPath, reserved);
            foreach (var problem in read.Diagnostics)
                error.WriteLine(problem);
            if (read is not Succeeded<HaruspexConfig> { Value: var fromFile })
                return ExitCodes.Usage;
            config = fromFile;
        }

        var directory = request.Directory is { } given ? request.Resolve(given) : config.Source;
        if (directory is null)
        {
            error.WriteLine(configPath is null
                ? "haruspex: give the folder of .sql files to read, or a config file with \"source\"."
                : $"haruspex: give the folder of .sql files to read, or \"source\" in {configPath}.");
            return ExitCodes.Usage;
        }

        if (!Directory.Exists(directory))
        {
            error.WriteLine($"haruspex: directory not found: {request.Directory ?? directory}");
            return ExitCodes.Usage;
        }

        var outDirectory = request.Out is { } outGiven ? request.Resolve(outGiven) : config.Out;
        if (outDirectory is null)
        {
            error.WriteLine(configPath is null
                ? "haruspex: give --out, the folder to write to, or a config file with \"out\"."
                : $"haruspex: give --out, the folder to write to, or \"out\" in {configPath}.");
            return ExitCodes.Usage;
        }

        // --include replaces the file's schema includes for one run; each schema's exclude still applies.
        var include = new List<ObjectPattern>();
        foreach (var text in request.Include)
        {
            if (!ObjectPattern.TryParse(text, out var pattern, out var problem))
            {
                error.WriteLine($"haruspex: invalid --include pattern: {problem}");
                return ExitCodes.Usage;
            }

            include.Add(pattern);
        }

        var fromCommandLine = include.Count > 0;
        if (!fromCommandLine && !config.SelectsAnything)
        {
            // Nothing is selected without an include: typing every procedure is asked for, never the default.
            error.WriteLine(configPath is null
                ? "haruspex: nothing is selected: give --include, or a config file whose \"schemas\" include procedures. Use '*.*' to type every procedure."
                : $"haruspex: nothing is selected: give --include, or a schema with \"include\" in {configPath}, such as \"schemas\": {{ \"dbo\": {{ \"include\": [\"*\"] }} }}.");
            return ExitCodes.Usage;
        }

        var project = SqlProject.Load(directory);
        if (project.Diagnostics.Count > 0)
        {
            foreach (var diagnostic in project.Diagnostics)
                error.WriteLine(DiagnosticFormatter.Format(diagnostic, directory));

            error.WriteLine($"haruspex: {project.Diagnostics.Count} error(s); nothing was written.");
            return ExitCodes.Failed;
        }

        // A pattern or a schema that matches nothing is an error, not silence: a typo would otherwise drop an interface
        // from the generated code without anyone noticing.
        var selection = fromCommandLine
            ? ProcedureSelection.Select(project.Procedures, include, config)
            : ProcedureSelection.Select(project.Procedures, config);
        foreach (var problem in selection.Diagnostics)
            error.WriteLine(problem);
        if (selection is not Succeeded<EquatableArray<ObjectName>> { Value: var selected })
            return ExitCodes.Usage;

        // The file's own selection still decides which overrides are of selected procedures; problems with its patterns are
        // left for a run without --include, which this one-off isn't about.
        IReadOnlyCollection<ObjectName>? selectedByFile = fromCommandLine ? ProcedureSelection.SelectedByFile(project.Procedures, config).ToHashSet() : null;

        var applied = project.Infer(selected, config, selectedByFile, outDirectory);
        foreach (var problem in applied.Diagnostics)
            error.WriteLine(DiagnosticFormatter.Format(problem, directory));
        if (applied is not Succeeded<IrDocument> { Value: var document })
            return ExitCodes.Usage;

        var emitted = ZodEmitter.Emit(document, new ZodOptions
        {
            Objects = config.Objects == ObjectsPolicy.Strict ? ZodObjects.Strict : ZodObjects.Strip,
        });

        // The emitter knows no config file; what it refuses is a name or an import the file's settings made, so it is
        // reported against the file.
        foreach (var problem in emitted.Diagnostics)
            error.WriteLine(problem with { At = problem.At ?? config.Location });
        if (emitted is not Succeeded<string> { Value: var zod })
            return ExitCodes.Usage;

        var irPath = Path.Join(outDirectory, IrFileName);
        var zodPath = Path.Join(outDirectory, ZodFileName);
        try
        {
            Directory.CreateDirectory(outDirectory);

            // File.WriteAllText writes UTF-8 without a byte order mark.
            File.WriteAllText(irPath, IrJsonWriter.Write(document));
            File.WriteAllText(zodPath, zod);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"haruspex: cannot write to {outDirectory}: {e.Message}");
            return ExitCodes.Failed;
        }

        var procedures = document.Objects.Count;
        output.WriteLine(
            $"haruspex: {procedures} {(procedures == 1 ? "procedure" : "procedures")}, {document.Unknowns.Count} unknown(s). Wrote {irPath} and {zodPath}.");
        return ExitCodes.Success;
    }
}

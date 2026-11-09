using System.CommandLine;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>The <c>haruspex</c> command line. Results go to standard output, errors to standard error.</summary>
public static class HaruspexCli
{
    /// <summary>Exit codes, fixed so scripts and CI can rely on them.</summary>
    public static class ExitCodes
    {
        public const int Success = 0;

        /// <summary>The input has errors (SQL that does not parse), or the output could not be written.</summary>
        public const int Failed = 1;

        /// <summary>The command line is wrong: a missing option, a bad pattern, a pattern that matches nothing.</summary>
        public const int Usage = 2;

        /// <summary>A fault in haruspex itself.</summary>
        public const int Internal = 3;
    }

    public const string IrFileName = "haruspex.ir.json";
    public const string ZodFileName = "haruspex.zod.ts";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var root = Build(output, error);
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

    private static RootCommand Build(TextWriter output, TextWriter error)
    {
        var directory = new Argument<string>("dir")
        {
            Description = "The folder of .sql files to read, including subfolders.",
        };
        var include = new Option<string[]>("--include", "-i")
        {
            Description = "Procedures to type, as schema.name; * and ? match within a part. Repeat for more.",
            Required = true,
            Arity = ArgumentArity.OneOrMore,
        };
        var outDirectory = new Option<string>("--out", "-o")
        {
            Description = $"The folder to write {IrFileName} and {ZodFileName} to.",
            Required = true,
        };

        var generate = new Command("generate", "Infer the result shapes of procedures, and write the IR and a Zod module.")
        {
            directory,
            include,
            outDirectory,
        };
        generate.SetAction(parsed => Generate(
            parsed.GetRequiredValue(directory),
            parsed.GetRequiredValue(include),
            parsed.GetRequiredValue(outDirectory),
            output,
            error));

        return new RootCommand("haruspex: infers the result shapes of SQL Server procedures, FOR JSON included.")
        {
            generate,
        };
    }

    private static int Generate(string directory, string[] includeTexts, string outDirectory, TextWriter output, TextWriter error)
    {
        if (!Directory.Exists(directory))
        {
            error.WriteLine($"haruspex: directory not found: {directory}");
            return ExitCodes.Usage;
        }

        var include = new List<ObjectPattern>();
        foreach (var text in includeTexts)
        {
            if (!ObjectPattern.TryParse(text, out var pattern, out var problem))
            {
                error.WriteLine($"haruspex: invalid --include pattern: {problem}");
                return ExitCodes.Usage;
            }

            include.Add(pattern);
        }

        var project = SqlProject.Load(directory);
        if (project.Diagnostics.Count > 0)
        {
            foreach (var diagnostic in project.Diagnostics)
            {
                error.WriteLine(diagnostic.At is { } at
                    ? $"{Path.Join(directory, at.File)}:{at.Line}:{at.Column}: error: {diagnostic.Message}"
                    : $"haruspex: error: {diagnostic.Message}");
            }

            error.WriteLine($"haruspex: {project.Diagnostics.Count} error(s); nothing was written.");
            return ExitCodes.Failed;
        }

        // A pattern that matches nothing is an error, not silence: a typo would otherwise drop an interface
        // from the generated code without anyone noticing.
        var selection = project.Select(include);
        if (selection.Unmatched.Count > 0)
        {
            foreach (var pattern in selection.Unmatched)
                error.WriteLine($"haruspex: --include '{pattern.Text}' matches no procedure in {directory}");
            return ExitCodes.Usage;
        }

        var document = project.Infer(selection.Procedures);
        var irPath = Path.Join(outDirectory, IrFileName);
        var zodPath = Path.Join(outDirectory, ZodFileName);
        try
        {
            Directory.CreateDirectory(outDirectory);

            // File.WriteAllText writes UTF-8 without a byte order mark.
            File.WriteAllText(irPath, IrJsonWriter.Write(document));
            File.WriteAllText(zodPath, ZodEmitter.Emit(document));
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

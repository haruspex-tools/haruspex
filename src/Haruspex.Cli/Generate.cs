using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex generate</c>: the module the shared steps give (<see cref="ModuleRun"/>), written to the output folder, then
/// each unknown in it noted. The guide's chapter docs/internals/12-from-input-to-output.md follows a run through it.
/// </summary>
internal static class Generate
{
    /// <summary>Runs the command, writing diagnostics to <paramref name="error"/> and the summary to <paramref name="output"/>.</summary>
    /// <returns>The exit code (<see cref="HaruspexCli.ExitCodes"/>).</returns>
    public static int Run(RunRequest request, TextWriter output, TextWriter error)
    {
        var run = ModuleRun.Emitted(request)
            .Then(Write)
            .Then(Noted);
        return Report(run, output, error);
    }

    /// <summary>The written output: where each file went, and the project it was read from, which roots its locations.</summary>
    private sealed class Written(IrDocument document, string irPath, string zodPath, SqlProject project)
    {
        public IrDocument Document { get; } = document;

        public string IrPath { get; } = irPath;

        public string ZodPath { get; } = zodPath;

        public SqlProject Project { get; } = project;
    }

    /// <summary>
    /// Both files, written to the output folder so that a run never leaves a new file beside an old one: each is written in
    /// full to a temporary name in the folder itself (so that putting it in place is a rename within one file system), then
    /// both are put in place, the IR first; if the module can't be, the IR is put back as it was, or removed when there was
    /// none, and no temporary file is left. Two files can't be replaced as one, so a process killed between the two renames
    /// can still leave a new IR beside the old module (and the hidden temporary and backup files beside them), and so can a
    /// file system that refuses to put the IR back, which the message then says, keeping the old IR's backup. The folder is created here, after every
    /// other step has succeeded, so a run that fails before writing creates nothing.
    /// </summary>
    private static Outcome<Written> Write(EmittedModule module)
    {
        var folder = module.Out;
        var irPath = module.IrPath;
        var zodPath = module.ZodPath;
        var run = Guid.NewGuid().ToString("N");
        var irTemporary = Path.Join(folder, $".{HaruspexCli.IrFileName}.{run}.tmp");
        var zodTemporary = Path.Join(folder, $".{HaruspexCli.ZodFileName}.{run}.tmp");
        var irBackup = Path.Join(folder, $".{HaruspexCli.IrFileName}.{run}.old");
        var keepBackup = false;
        try
        {
            Directory.CreateDirectory(folder);

            // File.WriteAllText writes UTF-8 without a byte order mark.
            File.WriteAllText(irTemporary, IrJsonWriter.Write(module.Document));
            File.WriteAllText(zodTemporary, module.Zod);
            var hadIr = PutInPlace(irTemporary, irPath, irBackup);
            try
            {
                PutInPlace(zodTemporary, zodPath, backup: null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (PutBack(hadIr, irBackup, irPath) is { } failure)
                {
                    // The new IR stands beside the old module, so the old IR, the backup, is the one copy left: it is kept.
                    keepBackup = hadIr;
                    return ModuleRun.Refused<Written>(CliCodes.CannotWrite,
                        $"cannot write to {DiagnosticFormatter.PathAsShown(folder)}: {e.Message} The IR written before it could not be put back ({failure}), so {DiagnosticFormatter.PathAsShown(irPath)} is new and {DiagnosticFormatter.PathAsShown(zodPath)} is not{(hadIr ? $"; the old IR is kept at {DiagnosticFormatter.PathAsShown(irBackup)}" : "")}.");
                }

                throw;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ModuleRun.Refused<Written>(CliCodes.CannotWrite, $"cannot write to {DiagnosticFormatter.PathAsShown(folder)}: {e.Message}");
        }
        finally
        {
            foreach (var temporary in keepBackup ? [irTemporary, zodTemporary] : new[] { irTemporary, zodTemporary, irBackup })
                Remove(temporary);
        }

        return new Succeeded<Written>(new Written(module.Document, irPath, zodPath, module.Project));
    }

    /// <summary>
    /// Puts the IR back as it was after the module could not be put in place: the old one from its backup, or none when there
    /// was none. Null when it is back; otherwise what the file system said.
    /// </summary>
    private static string? PutBack(bool hadIr, string backup, string path)
    {
        try
        {
            if (hadIr)
                File.Move(backup, path, overwrite: true);
            else
                File.Delete(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Moves <paramref name="temporary"/> to <paramref name="path"/>, keeping what was there at <paramref name="backup"/> when
    /// one is given; whether there was a file there.
    /// </summary>
    private static bool PutInPlace(string temporary, string path, string? backup)
    {
        if (!File.Exists(path))
        {
            File.Move(temporary, path);
            return false;
        }

        File.Replace(temporary, path, backup);
        return true;
    }

    /// <summary>Removes a temporary file the write made; one that can't be removed is left, the write's outcome standing.</summary>
    private static void Remove(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary file left behind is untidy, not wrong: the outputs are each whole, which is what the write promises.
        }
    }

    /// <summary>Each unknown in what was written, as a note (<see cref="ModuleRun.Notes"/>).</summary>
    private static Succeeded<Written> Noted(Written written) => new(written, [.. ModuleRun.Notes(written.Document, written.Project)]);

    /// <summary>Writes what the run found, and its summary, and gives the exit code.</summary>
    private static int Report(Outcome<Written> run, TextWriter output, TextWriter error)
    {
        foreach (var diagnostic in run.Diagnostics)
            error.WriteLine(diagnostic);

        return run switch
        {
            Succeeded<Written> { Value: var written } => Succeeded(written, output),
            Failed<Written> failed => Failed(failed, error),
        };
    }

    private static int Succeeded(Written written, TextWriter output)
    {
        var procedures = written.Document.Objects.Count;
        output.WriteLine(
            $"haruspex: {procedures} {(procedures == 1 ? "procedure" : "procedures")}, {written.Document.Unknowns.Count} unknown(s). Wrote {DiagnosticFormatter.PathAsShown(written.IrPath)} and {DiagnosticFormatter.PathAsShown(written.ZodPath)}.");
        return HaruspexCli.ExitCodes.Success;
    }

    private static int Failed(Failed<Written> failed, TextWriter error)
    {
        var sql = failed.Diagnostics.Count(d => d.IsError && d.At is SourceLocation);
        if (sql > 0)
            error.WriteLine($"haruspex: {sql} error(s); nothing was written.");
        return ModuleRun.ExitCode(failed.Diagnostics);
    }
}

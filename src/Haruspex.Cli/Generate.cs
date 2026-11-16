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

    /// <summary>The written output: where each file went, the IR first, and the project it was read from, which roots its locations.</summary>
    private sealed class Written(IrDocument document, IReadOnlyList<string> paths, SqlProject project)
    {
        public IrDocument Document { get; } = document;

        public IReadOnlyList<string> Paths { get; } = paths;

        public SqlProject Project { get; } = project;
    }

    /// <summary>One file of a run's output, on its way into place: its text, a temporary name to write it to, and a name to keep what it replaces under.</summary>
    private sealed record Staged(string Path, string Text, string Temporary, string Backup);

    /// <summary>
    /// Every file, the IR first and then what the emitters gave, written to the output folder so that a run never leaves a new file
    /// beside an old one: each is written in full to a temporary name in the folder itself (so that putting it in place is a rename
    /// within one file system), then each is put in place in turn, keeping the one it replaces; if one can't be, those already in
    /// place are put back as they were, or removed when there was none, and no temporary file is left. Files can't be replaced as
    /// one, so a process killed between two renames can still leave a new file beside an old one (and the hidden temporary and
    /// backup files beside them), and so can a file system that refuses to put one back, which the message then says, keeping that
    /// file's backup. The folder is created here, after every other step has succeeded, so a run that fails before writing creates
    /// nothing.
    /// </summary>
    private static Outcome<Written> Write(EmittedModule module)
    {
        var folder = module.Out;
        var run = Guid.NewGuid().ToString("N");
        var staged = new[] { (Path: module.IrPath, Text: IrJsonWriter.Write(module.Document)) }
            .Concat(module.Files.Select(f => (Path: module.PathOf(f), f.Text)))
            .Select(o => new Staged(o.Path, o.Text, Path.Join(folder, $".{Path.GetFileName(o.Path)}.{run}.tmp"), Path.Join(folder, $".{Path.GetFileName(o.Path)}.{run}.old")))
            .ToList();
        var kept = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            Directory.CreateDirectory(folder);

            // File.WriteAllText writes UTF-8 without a byte order mark.
            foreach (var file in staged)
                File.WriteAllText(file.Temporary, file.Text);
            var placed = new List<(Staged File, bool Had)>();
            try
            {
                foreach (var file in staged)
                    placed.Add((file, PutInPlace(file.Temporary, file.Path, file.Backup)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                var stuck = placed.AsEnumerable().Reverse()
                    .Select(p => (p.File, p.Had, Failure: PutBack(p.Had, p.File.Backup, p.File.Path)))
                    .Where(p => p.Failure is not null)
                    .ToList();
                if (stuck.Count > 0)
                {
                    // Each new file left beside the old ones has its old one, the backup, as the one copy left: it is kept.
                    foreach (var p in stuck.Where(p => p.Had))
                        kept.Add(p.File.Backup);
                    var fresh = stuck.Select(p => DiagnosticFormatter.PathAsShown(p.File.Path)).ToList();
                    var backups = stuck.Where(p => p.Had).Select(p => DiagnosticFormatter.PathAsShown(p.File.Backup)).ToList();
                    return ModuleRun.Refused<Written>(CliCodes.CannotWrite,
                        $"cannot write to {DiagnosticFormatter.PathAsShown(folder)}: {e.Message} Files written before it could not be put back ({string.Join("; ", stuck.Select(p => p.Failure))}), so {EnglishList.Of(fresh)} {(fresh.Count == 1 ? "is" : "are")} new and the others are not{(backups.Count > 0 ? $"; the old {(backups.Count == 1 ? "one is" : "ones are")} kept at {EnglishList.Of(backups)}" : "")}.");
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
            foreach (var temporary in staged.SelectMany(f => new[] { f.Temporary, f.Backup }).Where(f => !kept.Contains(f)))
                Remove(temporary);
        }

        return new Succeeded<Written>(new Written(module.Document, [.. staged.Select(f => f.Path)], module.Project));
    }

    /// <summary>
    /// Puts a file back as it was after a later one could not be put in place: the old one from its backup, or none when there
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
            $"haruspex: {procedures} {(procedures == 1 ? "procedure" : "procedures")}, {written.Document.Unknowns.Count} unknown(s). Wrote {EnglishList.Of([.. written.Paths.Select(DiagnosticFormatter.PathAsShown)])}.");
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

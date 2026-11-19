using Haruspex.Cli.Output;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex generate</c>: the module the shared steps give (<see cref="ModuleRun"/>), written to the output folder, with each
/// unknown in it noted. It reports what it did (<see cref="ModuleReport"/>) and prints nothing itself. The guide's chapter
/// docs/internals/12-from-input-to-output.md follows a run through it.
/// </summary>
internal static class Generate
{
    /// <summary>Runs the command over one module, saying each step to <paramref name="progress"/>.</summary>
    public static ModuleReport Run(RunRequest request, Progress progress)
    {
        var emitted = ModuleRun.Emitted(request, progress);
        var run = emitted.Then(module =>
        {
            // Read before it is written over: what the folder held is what this run is compared with.
            progress.Step($"writing {DisplayPath.From(request.WorkingDirectory, module.Out)}");
            var earlier = EarlierOutput.Read(module);
            return Write(module).Select(written => (Written: written, Since: SinceLastRun.Of(earlier, module, written.Unknowns)));
        });
        return run switch
        {
            Succeeded<(Written Written, SinceLastRun Since)> { Value: var value } => Reported(request, run.Diagnostics, value.Written, value.Since),
            Failed<(Written Written, SinceLastRun Since)> failed => new ModuleReport(CommandName.Generate, request, failed.Diagnostics, ModuleRun.ExitCode(failed.Diagnostics))
            {
                Out = (emitted as Succeeded<EmittedModule>)?.Value.Out,
            },
        };
    }

    /// <summary>
    /// The report of a module written: what the run found, but for the warnings the config file acknowledges, which are set aside.
    /// With a baseline, what combining it found too, and each function whose return shape differs from the baseline's; a change
    /// that can't be bridged fails the run, though the module is written for the rest.
    /// </summary>
    private static ModuleReport Reported(RunRequest request, EquatableArray<Diagnostic> found, Written written, SinceLastRun since)
    {
        var module = written.Module;
        Diagnostic[] combining = module.Baseline is { } baseline ? [.. module.Combination, .. BaselineCheck.Shapes(module, baseline.Document, module.Document, baseline)] : [];
        var (kept, acknowledged) = Acknowledgements.Apply([.. found, .. combining], module.Config, sayUnused: request.Include.Count == 0);
        var failures = combining.Where(d => d.IsError).ToList();
        return new ModuleReport(CommandName.Generate, request, [.. kept, .. written.Unknowns.Select(u => u.Note).OfType<Diagnostic>()], failures.Count == 0 ? HaruspexCli.ExitCodes.Success : ModuleRun.ExitCode(failures))
        {
            Out = written.Module.Out,
            Baseline = module.Baseline?.Path,
            Document = written.Module.Document,
            Written = written.Paths,
            Modules = written.Module.Modules,
            Unknowns = written.Unknowns,
            Since = since,
            Acknowledged = acknowledged,
        };
    }

    /// <summary>The written output: the module, where each file went (the IR first), and each unknown in it, noted.</summary>
    private sealed class Written(EmittedModule module, EquatableArray<string> paths)
    {
        public EmittedModule Module { get; } = module;

        public EquatableArray<string> Paths { get; } = paths;

        public EquatableArray<NotedUnknown> Unknowns { get; } = ModuleRun.Notes(module.Document, module.Project);
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

        return new Succeeded<Written>(new Written(module, [.. staged.Select(f => f.Path)]));
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
}

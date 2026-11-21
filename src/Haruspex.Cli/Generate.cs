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
            return Write(module)
                .Select(paths => Tidied(module, paths, earlier.List, narrowed: request.Include.Count > 0))
                .Select(written => (Written: written, Since: SinceLastRun.Of(earlier, module, written.Unknowns)));
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
        var failures = combining.Concat(written.Warnings).Where(d => d.IsError).ToList();
        return new ModuleReport(CommandName.Generate, request, [.. kept, .. written.Warnings, .. written.Unknowns.Select(u => u.Note).OfType<Diagnostic>()], failures.Count == 0 ? HaruspexCli.ExitCodes.Success : ModuleRun.ExitCode(failures))
        {
            Out = written.Module.Out,
            Baseline = module.Baseline?.Path,
            Document = written.Module.Document,
            Written = written.Paths,
            Removed = written.Removed,
            Modules = written.Module.Modules,
            Unknowns = written.Unknowns,
            Since = since,
            Acknowledged = acknowledged,
        };
    }

    /// <summary>
    /// The written output: the module, where each file went (the IR first), the files of the last run it removed, what removing
    /// them and writing the list found (warnings, and an error when the list can't be written), and each unknown in the module,
    /// noted.
    /// </summary>
    private sealed class Written(EmittedModule module, EquatableArray<string> paths, EquatableArray<string> removed, EquatableArray<Diagnostic> warnings)
    {
        public EmittedModule Module { get; } = module;

        public EquatableArray<string> Paths { get; } = paths;

        public EquatableArray<string> Removed { get; } = removed;

        public EquatableArray<Diagnostic> Warnings { get; } = warnings;

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
    private static Outcome<EquatableArray<string>> Write(EmittedModule module)
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
                    return ModuleRun.Refused<EquatableArray<string>>(CliCodes.CannotWrite,
                        $"cannot write to {DiagnosticFormatter.PathAsShown(folder)}: {e.Message} Files written before it could not be put back ({string.Join("; ", stuck.Select(p => p.Failure))}), so {EnglishList.Of(fresh)} {(fresh.Count == 1 ? "is" : "are")} new and the others are not{(backups.Count > 0 ? $"; the old {(backups.Count == 1 ? "one is" : "ones are")} kept at {EnglishList.Of(backups)}" : "")}.");
                }

                throw;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ModuleRun.Refused<EquatableArray<string>>(CliCodes.CannotWrite, $"cannot write to {DiagnosticFormatter.PathAsShown(folder)}: {e.Message}");
        }
        finally
        {
            foreach (var temporary in staged.SelectMany(f => new[] { f.Temporary, f.Backup }).Where(f => !kept.Contains(f)))
                Remove(temporary);
        }

        return new Succeeded<EquatableArray<string>>([.. staged.Select(f => f.Path)]);
    }

    /// <summary>
    /// Once the module is written, at <paramref name="paths"/>: each file the last run's list names and this run didn't write is
    /// removed, and then the list is written anew, naming this run's files and any of the last run's that couldn't be removed, so
    /// that the next run tries again. Without a list to go by, a first run or one whose list can't be read, nothing is removed.
    /// The list is written last, so a run stopped before then leaves the last one's, which still names every file this one would
    /// have removed. Only the list says what may be removed, never the folder's contents
    /// (docs/decisions/0091-generate-removes-only-the-files-its-list-names.md). A run <paramref name="narrowed"/> by
    /// <c>--include</c> is a one-off look at part of the module, so it removes nothing: what it doesn't write is carried onto the
    /// new list, for the next full run to remove or write again.
    /// </summary>
    private static Written Tidied(EmittedModule module, EquatableArray<string> paths, EarlierList earlier, bool narrowed)
    {
        var list = Path.Join(module.Out, OutputList.FileName);
        var at = new FileLocation(list);
        string[] names = [HaruspexCli.IrFileName, .. module.Files.Select(f => f.Name)];
        var warnings = new List<Diagnostic>();
        var removed = new List<string>();
        var kept = new List<string>();
        switch (earlier)
        {
            case EarlierList.Unreadable { Problem: var problem }:
                warnings.Add(CliCodes.ListUnreadable.At(at,
                    $"this list of the files the last run wrote can't be read: {problem} So none of them is removed, and the list is written anew with this run's files; remove yourself any file the last run wrote that this one doesn't."));
                break;
            case EarlierList.Readable { Entries: var entries }:
                var found = StaleFiles.Of(module.Out, entries, names);
                warnings.AddRange(found.Refused.Select(r => CliCodes.ListedOutside.At(at,
                    $"\"{r.Entry}\" {r.Why}, so it isn't removed: generate removes only files inside the output folder, named as it writes them. The list was edited, or written by something else; it is written anew without it.")));
                if (narrowed)
                {
                    kept.AddRange(found.Stale);
                    break;
                }

                foreach (var entry in found.Stale)
                {
                    var path = StaleFiles.PathOf(module.Out, entry);
                    try
                    {
                        File.Delete(path);
                        removed.Add(path);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        kept.Add(entry);
                        warnings.Add(CliCodes.CannotRemove.At(new FileLocation(path),
                            $"the last run wrote this file and this one doesn't, but it can't be removed: {e.Message} It stays on the list, so the next run tries again."));
                    }
                }

                break;
            default:
                // No list: nothing to go by, so nothing is removed.
                break;
        }

        var temporary = Path.Join(module.Out, $".{OutputList.FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, OutputList.Text([.. names, .. kept]));
            File.Move(temporary, list, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Remove(temporary);

            // The module is written and the removals done, so the run is reported as one that wrote, with this error failing it.
            warnings.Add(CliCodes.CannotWrite.At(at,
                $"the module is written, but this list of its files can't be: {e.Message} The next run goes by the list that is there, if any, which may not name every file this run wrote."));
        }

        return new Written(module, paths, [.. removed], [.. warnings]);
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

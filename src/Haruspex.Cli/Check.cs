using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex check</c>: the module the shared steps give (<see cref="ModuleRun"/>), each unknown in it noted, then compared
/// with what is committed in the output folder, claim by claim (<see cref="IrComparison"/>) and as text, without writing
/// anything. It fails when a claim would change, when there is nothing to compare with, and when the module leaves what the
/// config file selected unknown: results that can't be listed, or, if the file says so, any value. Why it compares claims and
/// not files, and why it fails when nothing is committed: docs/decisions/0050-check-compares-claims-not-files.md.
/// </summary>
internal static class Check
{
    /// <summary>Runs the command, writing diagnostics to <paramref name="error"/> and the summary to <paramref name="output"/>.</summary>
    /// <returns>The exit code (<see cref="HaruspexCli.ExitCodes"/>).</returns>
    public static int Run(RunRequest request, TextWriter output, TextWriter error)
    {
        var run = ModuleRun.Emitted(request)
            .Then(Noted)
            .Then(Compared);
        return Report(run, output, error);
    }

    /// <summary>Each unknown, as a note (<see cref="ModuleRun.Notes"/>): said before the comparison, so a failed check says them too.</summary>
    private static Succeeded<EmittedModule> Noted(EmittedModule module) => new(module, [.. ModuleRun.Notes(module.Document, module.Project)]);

    /// <summary>
    /// What would fail the check: the module incomplete, and its differences from what is committed. The two don't depend on each
    /// other, so each is said whatever the other finds.
    /// </summary>
    private static Outcome<EmittedModule> Compared(EmittedModule module)
    {
        Diagnostic[] found = [.. Incomplete(module), .. Differences(module)];
        return found.Length == 0 ? new Succeeded<EmittedModule>(module) : new Failed<EmittedModule>([.. found]);
    }

    /// <summary>
    /// A selected procedure whose result sets can't be listed and aren't declared: being selected is a claim that it is an
    /// interface, which the module can't check. And, when <c>check.unknowns</c> is <c>fail</c>, any unknown.
    /// </summary>
    private static IEnumerable<Diagnostic> Incomplete(EmittedModule module)
    {
        var file = module.Config.File.Length == 0 ? HaruspexConfig.FileName : Path.GetFileName(module.Config.File);
        foreach (var obj in module.Document.Objects.Where(o => o.Results is null).OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            yield return At(module, obj, CliCodes.ResultsUnknown,
                $"{obj.Id}: its result sets are unknown, and check passes a selected procedure only when they are known or declared. Declare them in {file} under procedures[\"{obj.Id}\"].results, as \"none\" or a JSON Schema, or leave the procedure out of the selection.");
        }

        var unknowns = module.Document.Unknowns.Count;
        if (module.Config.CheckUnknowns == CheckUnknowns.Fail && unknowns > 0)
        {
            yield return CliCodes.UnknownsFail.At(module.Config.Location,
                $"{unknowns} unknown{(unknowns == 1 ? "" : "s")} in the module, each said above, and \"check\": {{ \"unknowns\": \"fail\" }} fails the check on any. Type each (an override, declared results), or set it to \"report\".");
        }
    }

    /// <summary>
    /// The module's differences from what is committed: each change of claim at its procedure; otherwise a module whose text
    /// differs, line endings aside (git may convert them on checkout). Nothing to compare with is one error saying why.
    /// </summary>
    private static IEnumerable<Diagnostic> Differences(EmittedModule module) => Committed(module) switch
    {
        Failed<(IrDocument Ir, string Zod)> failed => failed.Diagnostics,
        Succeeded<(IrDocument Ir, string Zod)> { Value: var committed } => Differences(module, committed.Ir, committed.Zod),
    };

    private static IEnumerable<Diagnostic> Differences(EmittedModule module, IrDocument committed, string zod)
    {
        var changes = IrComparison.Compare(committed, module.Document);
        if (changes.Count > 0)
            return changes.Select(change => At(module, Object(change, committed, module.Document), CliCodes.ClaimChanged, change.ToString()));

        return zod.Replace("\r\n", "\n", StringComparison.Ordinal) == module.Zod
            ? []
            : [CliCodes.ModuleDiffers.At(new FileLocation(module.ZodPath),
                "this module isn't what haruspex writes for the IR beside it, though the IR claims the same: another version of haruspex wrote it, or it was edited. Run haruspex generate, and read the change to the module before committing it.")];
    }

    /// <summary>The committed IR and module; or why there are none to compare with: missing, of another version, or not readable as IR.</summary>
    private static Outcome<(IrDocument Ir, string Zod)> Committed(EmittedModule module)
    {
        var missing = new[] { module.IrPath, module.ZodPath }.Where(path => !File.Exists(path)).ToList();
        if (missing.Count > 0)
        {
            return ModuleRun.Refused<(IrDocument, string)>(CliCodes.NothingCommitted,
                $"nothing to compare with: {string.Join(" and ", missing)} {(missing.Count == 1 ? "isn't" : "aren't")} there. Run haruspex generate, read what it writes, and commit it.");
        }

        var at = new FileLocation(module.IrPath);
        try
        {
            var text = File.ReadAllText(module.IrPath);
            if (IrJsonReader.VersionOf(text) is { } version && !IrDocument.ReadableVersions.Contains(version, StringComparer.Ordinal))
            {
                return new Failed<(IrDocument, string)>([CliCodes.CommittedOtherVersion.At(at,
                    $"this file is IR {version}, and this haruspex writes IR {IrDocument.CurrentVersion}, so the two can't be compared claim by claim. Run haruspex generate with this version, read the change to {module.ZodPath} before committing it, and commit both.")]);
            }

            return new Succeeded<(IrDocument, string)>((IrJsonReader.Read(text), File.ReadAllText(module.ZodPath)));
        }
        catch (IrFormatException e)
        {
            return new Failed<(IrDocument, string)>([CliCodes.CommittedUnreadable.At(at, $"this file can't be read as IR: {e.Message} Run haruspex generate to write it again.")]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failed<(IrDocument, string)>([CliCodes.CommittedUnreadable.At(at, $"this file can't be read: {e.Message}")]);
        }
    }

    /// <summary>The object a change is in: the module's, or the committed one's for an object the module no longer has; null for the document's own.</summary>
    private static IrObject? Object(IrChange change, IrDocument committed, IrDocument document) =>
        document.Objects.FirstOrDefault(o => o.Id == change.Object) ?? committed.Objects.FirstOrDefault(o => o.Id == change.Object);

    /// <summary>A diagnostic at the object's place in the SQL, under the folder it was read from; with no place when there is none.</summary>
    private static Diagnostic At(EmittedModule module, IrObject? obj, DiagnosticCode code, string message) =>
        ModuleRun.Rooted(code.At(obj is not null && SourceLocation.TryParse(obj.At, out var at) ? at : null, message), module.Project);

    /// <summary>Writes what the run found, and its summary, and gives the exit code.</summary>
    private static int Report(Outcome<EmittedModule> run, TextWriter output, TextWriter error)
    {
        foreach (var diagnostic in run.Diagnostics)
            error.WriteLine(diagnostic);

        return run switch
        {
            Succeeded<EmittedModule> { Value: var module } => Passed(module, output),
            Failed<EmittedModule> failed => Failed(failed, error),
        };
    }

    private static int Passed(EmittedModule module, TextWriter output)
    {
        var procedures = module.Document.Objects.Count;
        output.WriteLine(
            $"haruspex: {procedures} {(procedures == 1 ? "procedure" : "procedures")}, {module.Document.Unknowns.Count} unknown(s). {module.Out} is up to date.");
        return HaruspexCli.ExitCodes.Success;
    }

    private static int Failed(Failed<EmittedModule> failed, TextWriter error)
    {
        var changes = failed.Diagnostics.Count(d => d.Code == CliCodes.ClaimChanged);
        if (changes > 0)
        {
            error.WriteLine(
                $"haruspex: {changes} change(s) from what is committed. If they are meant, run haruspex generate and commit what it writes; if not, change the SQL back.");
        }

        return ModuleRun.ExitCode(failed.Diagnostics);
    }
}

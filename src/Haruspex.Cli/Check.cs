using Haruspex.Cli.Output;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex check</c>: the module the shared steps give (<see cref="ModuleRun"/>), each unknown in it noted, then compared
/// with the files in the output folder (in CI, the ones committed), claim by claim (<see cref="IrComparison"/>) and as text, without writing
/// anything. It fails when a claim would change, when there is nothing to compare with, and when the module leaves what the
/// config file selected unknown: results that can't be listed, or, if the file says so, any value. Why it compares claims and
/// not files, and why it fails when nothing is committed: docs/decisions/0050-check-compares-claims-not-files.md. With a
/// baseline, it also fails on what the application already running would fail on (<see cref="BaselineCheck"/>).
/// </summary>
internal static class Check
{
    /// <summary>Runs the command over one module, saying each step to <paramref name="progress"/>.</summary>
    public static ModuleReport Run(RunRequest request, Progress progress)
    {
        var emitted = ModuleRun.Emitted(request, progress);
        if (emitted is not Succeeded<EmittedModule> { Value: var module })
            return new ModuleReport(CommandName.Check, request, emitted.Diagnostics, ModuleRun.ExitCode(emitted.Diagnostics));

        // Each unknown is noted before the comparison, so a failed check says them too.
        var unknowns = ModuleRun.Notes(module.Document, module.Project);

        // --fit-only asks for the fit alone, as a database's pipeline does against an application's committed IR, where the
        // exact comparison would refuse every change the fit allows; otherwise the two are made side by side.
        if (request.FitOnly && module.Baseline is null)
        {
            return new ModuleReport(CommandName.Check, request, [CliCodes.FitWithoutBaseline.At(null,
                "--fit-only judges the module against a baseline alone, and none is given. Give --baseline, or \"baseline\" in the config file; or leave out --fit-only to compare with the output folder.")], HaruspexCli.ExitCodes.Usage);
        }

        var withOut = !request.FitOnly;
        if (withOut)
            progress.Step($"comparing with {DisplayPath.From(request.WorkingDirectory, module.Out)}");
        var compared = Compared(module, withOut);
        IReadOnlyList<Diagnostic> fit = [];
        if (module.Baseline is { } baseline)
        {
            progress.Step($"comparing with the baseline {DisplayPath.From(request.WorkingDirectory, baseline.Path)}");
            fit = BaselineCheck.Run(module, baseline);
        }

        var (kept, acknowledged) = Acknowledgements.Apply([.. emitted.Diagnostics, .. module.Combination], module.Config, sayUnused: request.Include.Count == 0);
        Diagnostic[] found = [.. kept, .. unknowns.Select(u => u.Note).OfType<Diagnostic>(), .. compared, .. fit];
        var failures = compared.Concat(fit).Concat(module.Combination).Where(d => d.IsError).ToList();
        return new ModuleReport(CommandName.Check, request, [.. found], failures.Count == 0 ? HaruspexCli.ExitCodes.Success : ModuleRun.ExitCode(failures))
        {
            Out = withOut ? module.Out : null,
            Baseline = module.Baseline?.Path,
            Document = module.Document,
            Unknowns = unknowns,
            Acknowledged = acknowledged,
        };
    }

    /// <summary>
    /// What would fail the check: the module incomplete, and, when <paramref name="withOut"/>, its differences from the files in the
    /// output folder. The two don't depend on each other, so each is said whatever the other finds.
    /// </summary>
    private static List<Diagnostic> Compared(EmittedModule module, bool withOut) => withOut ? [.. Incomplete(module), .. Differences(module)] : [.. Incomplete(module)];

    /// <summary>
    /// A selected procedure whose result sets can't be listed and aren't declared: being selected is a claim that it is an
    /// interface, which the module can't check. And, when <c>check.unknowns</c> is <c>fail</c>, any unknown.
    /// </summary>
    private static IEnumerable<Diagnostic> Incomplete(EmittedModule module)
    {
        var file = module.Config.File.Length == 0 ? HaruspexConfig.FileName : Path.GetFileName(module.Config.File);
        foreach (var obj in module.Document.Objects.Where(o => o.Results is null).OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            yield return At(module, obj, CliCodes.ResultsUnknown, obj.Kind == IrObjectKind.Query
                ? $"{obj.Id}: its result sets are unknown, and check passes a query file only when they are known. Write it so that they can be inferred, or leave it out of \"queries\"."
                : $"{obj.Id}: its result sets are unknown, and check passes a selected procedure only when they are known or declared. Declare them in {file} under procedures[\"{obj.Id}\"].results, as \"none\" or a JSON Schema, or leave the procedure out of the selection.");
        }

        var unknowns = module.Document.Unknowns.Count;
        if (module.Config.CheckUnknowns == CheckUnknowns.Fail && unknowns > 0)
        {
            yield return CliCodes.UnknownsFail.At(module.Config.Location,
                $"{unknowns} unknown{(unknowns == 1 ? "" : "s")} in the module, each said above, and \"check\": {{ \"unknowns\": \"fail\" }} fails the check on any. Type each (an override, declared results), or set it to \"report\".");
        }
    }

    /// <summary>
    /// The module's differences from the files in the output folder: each change of claim at its procedure; otherwise each emitted file whose
    /// text differs, line endings aside (git may convert them on checkout). Nothing to compare with is one error saying why.
    /// </summary>
    private static IEnumerable<Diagnostic> Differences(EmittedModule module) => Committed(module) switch
    {
        Failed<(IrDocument Ir, IReadOnlyList<string> Files)> failed => failed.Diagnostics,
        Succeeded<(IrDocument Ir, IReadOnlyList<string> Files)> { Value: var committed } => Differences(module, committed.Ir, committed.Files),
    };

    /// <param name="files">The text in the output folder of each of the module's files, in <see cref="EmittedModule.Files"/>' order.</param>
    private static IEnumerable<Diagnostic> Differences(EmittedModule module, IrDocument committed, IReadOnlyList<string> files)
    {
        var changes = IrComparison.Compare(committed, module.Document);
        if (changes.Count > 0)
            return changes.Select(change => Placed(module, change, Object(change, committed, module.Document)));

        return
        [
            .. module.Files.Zip(files).SelectMany(pair => FirstDifference(pair.Second.Replace("\r\n", "\n", StringComparison.Ordinal), pair.First.Text) is { } line
                ? [CliCodes.ModuleDiffers.At(new FileLocation(module.PathOf(pair.First)),
                    $"line {line.Number} is {line.Committed} where haruspex now writes {line.Now}, though no claim the IR makes has changed. A comment written from a reason (why a value is unknown, why a result may be absent) changes so; so does a module another version of haruspex wrote, or one edited. Run haruspex generate, and read the change to the module before committing it.")]
                : Array.Empty<Diagnostic>()),
        ];
    }

    /// <summary>The first line where the module in the output folder and the one haruspex writes differ, each cut short and quoted; null when none does.</summary>
    private static (int Number, string Committed, string Now)? FirstDifference(string committed, string now)
    {
        // Each module ends with a line break, which ends its last line rather than beginning one more.
        var was = committed.TrimEnd('\n').Split('\n');
        var written = now.TrimEnd('\n').Split('\n');
        for (var i = 0; i < Math.Max(was.Length, written.Length); i++)
        {
            var before = i < was.Length ? was[i] : null;
            var after = i < written.Length ? written[i] : null;
            if (before != after)
                return (i + 1, Quoted(before), Quoted(after));
        }

        return null;

        static string Quoted(string? line) => line switch
        {
            null => "nothing (the module ends before)",
            { Length: > LineShown } => $"`{line[..LineShown]}…`",
            _ => $"`{line}`",
        };
    }

    /// <summary>The most of a line a message quotes.</summary>
    private const int LineShown = 100;

    /// <summary>
    /// The IR in the output folder and each of the module's files, in their order; or why there are none to compare with: missing, of another
    /// version, or not readable as IR.
    /// </summary>
    private static Outcome<(IrDocument Ir, IReadOnlyList<string> Files)> Committed(EmittedModule module)
    {
        var earlier = EarlierOutput.Read(module);
        if (earlier.Unreadable.Count > 0)
        {
            return new Failed<(IrDocument, IReadOnlyList<string>)>([.. earlier.Unreadable.Select(u =>
                CliCodes.CommittedUnreadable.At(new FileLocation(u.Key), $"this file can't be read: {u.Value}"))]);
        }

        var missing = module.Files.Select(module.PathOf).Where((_, i) => earlier.Files[i] is null).ToList();
        if (earlier.Ir is EarlierIr.Absent)
            missing.Insert(0, module.IrPath);
        if (missing.Count > 0)
        {
            return ModuleRun.Refused<(IrDocument, IReadOnlyList<string>)>(CliCodes.NothingCommitted,
                $"nothing to compare with: {EnglishList.Of([.. missing.Select(DiagnosticFormatter.PathAsShown)])} {(missing.Count == 1 ? "isn't" : "aren't")} there. Run haruspex generate, read what it writes, and commit it.");
        }

        var at = new FileLocation(module.IrPath);
        var emitted = EnglishList.Of([.. module.Files.Select(f => DiagnosticFormatter.PathAsShown(module.PathOf(f)))]);
        return earlier.Ir switch
        {
            EarlierIr.Readable { Document: var document } => new Succeeded<(IrDocument, IReadOnlyList<string>)>((document, [.. earlier.Files.OfType<string>()])),
            EarlierIr.OtherVersion { Version: var version } => new Failed<(IrDocument, IReadOnlyList<string>)>([CliCodes.CommittedOtherVersion.At(at,
                $"this file is IR {version}, and this haruspex writes IR {module.Document.Version}, so the two can't be compared claim by claim. Run haruspex generate with this version, read the change to {emitted} before committing it, and commit {(module.Files.Count == 1 ? "both" : "them all")}.")]),
            EarlierIr.Unreadable unreadable => new Failed<(IrDocument, IReadOnlyList<string>)>([CliCodes.CommittedUnreadable.At(at, unreadable.AsIr
                ? $"this file can't be read as IR: {unreadable.Problem} Run haruspex generate to write it again."
                : $"this file can't be read: {unreadable.Problem}")]),

            // An absent IR is reported as missing above. The arm is a discard because the analyser that checks a switch over a
            // closed hierarchy covers each case doesn't look at this assembly (EarlierIr).
            _ => throw new InvalidOperationException($"No IR to compare with: {earlier.Ir}."),
        };
    }

    /// <summary>The object a change is in: the module's, or the earlier one's for an object the module no longer has; null for the document's own.</summary>
    private static IrObject? Object(IrChange change, IrDocument committed, IrDocument document) =>
        document.Objects.FirstOrDefault(o => o.Id == change.Object) ?? committed.Objects.FirstOrDefault(o => o.Id == change.Object);

    /// <summary>
    /// A change of claim, at the place in the SQL it comes from when the IR says: for a value that became nullable, the first of
    /// its reasons that has a place (the column declared nullable, the outer join, the expression that may give null).
    /// Otherwise at its object, since the IR records no place for a field's own definition, nor for a value that is gone.
    /// </summary>
    private static Diagnostic Placed(EmittedModule module, IrChange change, IrObject? obj) =>
        change.NullableBecause.Select(r => module.Project.PlaceOf(r.At)).FirstOrDefault(at => at is not null) is { } reason
            ? ModuleRun.Rooted(CliCodes.ClaimChanged.At(reason, change.ToString()), module.Project)
            : At(module, obj, CliCodes.ClaimChanged, change.ToString());

    /// <summary>A diagnostic at the object's place in the SQL, under the folder it was read from; with no place when there is none.</summary>
    private static Diagnostic At(EmittedModule module, IrObject? obj, DiagnosticCode code, string message) =>
        ModuleRun.Rooted(code.At(obj is not null && SourceLocation.TryParse(obj.At, out var at) ? at : null, message), module.Project);
}

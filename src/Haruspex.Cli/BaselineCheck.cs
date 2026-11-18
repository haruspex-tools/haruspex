using Haruspex.Cli.Output;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex check --baseline</c>: whether the module fits within the baseline, the database the application already running
/// was built against (<see cref="IrFit"/>). The baseline is read as a source is, with the module's own config file, or from an IR
/// file; then each change the application would fail on is an error, each it sets aside a warning, and each function whose
/// return shape changes a warning too. Why: docs/decisions/0069-check-with-a-baseline-judges-the-deploy-window.md.
/// </summary>
internal static class BaselineCheck
{
    /// <summary>The baseline the run names, full: the command line's over the config file's; null when neither names one.</summary>
    public static string? Of(RunRequest request, HaruspexConfig config) =>
        request.Baseline is { } given ? request.Resolve(given) : config.Baseline;

    /// <summary>What the fit finds, each change at its procedure; or why the baseline can't be read.</summary>
    public static IReadOnlyList<Diagnostic> Run(RunRequest request, EmittedModule module, string baseline, Progress progress)
    {
        var shown = DisplayPath.From(request.WorkingDirectory, baseline);
        if (!File.Exists(baseline) && !Directory.Exists(baseline))
            return [CliCodes.BaselineNotFound.At(request.Baseline is null ? module.Config.Location : null, $"the baseline {shown} isn't there. haruspex never fetches it: put it there first (in CI, a step before this one), or correct --baseline or \"baseline\".")];

        var isIr = File.Exists(baseline) && string.Equals(Path.GetExtension(baseline), ".json", StringComparison.OrdinalIgnoreCase);
        progress.Step($"comparing with the baseline {DisplayPath.From(request.WorkingDirectory, baseline)}");
        var read = isIr ? ReadIr(baseline, shown) : Inferred(request, baseline, progress);
        if (read is not Succeeded<IrDocument> { Value: var application })
            return read.Diagnostics;

        var notChecked = NotChecked(application, module.Document, isIr);
        var findings = IrFit.Of(application, module.Document, new IrFitOptions { StrictObjects = module.Config.Objects == ObjectsPolicy.Strict })
            .Where(f => !notChecked.ContainsKey(f.Change.Object));
        return
        [
            .. findings.Select(f => Said(module, f)),
            .. Shapes(module, application),
            .. notChecked.Select(u => At(module, module.Document.Objects.FirstOrDefault(o => o.Id == u.Key), CliCodes.QueryTextUnchecked, u.Value)),
        ];
    }

    /// <summary>An IR file as the baseline: of this version, or refused, since a claim's meaning may differ between versions.</summary>
    private static Outcome<IrDocument> ReadIr(string path, string shown) => EarlierOutput.ReadIr(path) switch
    {
        EarlierIr.Readable { Document: var document } => new Succeeded<IrDocument>(document),
        EarlierIr.OtherVersion { Version: var version } => ModuleRun.Refused<IrDocument>(CliCodes.BaselineUnreadable,
            $"the baseline {shown} is IR {version}, and this haruspex reads IR {IrDocument.CurrentVersion}, so the two can't be compared claim by claim. Give the baseline as the source it was inferred from (a .dacpac, a .sqlproj or a folder), which this version reads."),
        EarlierIr.Unreadable { Problem: var problem } => ModuleRun.Refused<IrDocument>(CliCodes.BaselineUnreadable, $"the baseline {shown} can't be read as IR: {problem}"),

        // Absent is said before the file is read. The arm is a discard because the analyser that checks a switch over a closed
        // hierarchy covers each case doesn't look at this assembly (EarlierIr).
        _ => throw new InvalidOperationException($"No baseline at {path}."),
    };

    /// <summary>
    /// A source as the baseline, read and inferred with the module's own config file and command line, so that it selects the
    /// same procedures and applies the same overrides and names. Only its errors are said: a warning is about the old source,
    /// which nobody is changing.
    /// </summary>
    private static Outcome<IrDocument> Inferred(RunRequest request, string baseline, Progress progress) =>
        ModuleRun.Inferred(request with { Directory = baseline, Baseline = null }, progress) switch
        {
            Succeeded<IrDocument> { Value: var document } => new Succeeded<IrDocument>(document),
            var failed => new Failed<IrDocument>([.. failed.Diagnostics.Where(d => d.IsError)]),
        };

    /// <summary>
    /// The query files whose fit can't be judged, each with why. A query file is the application's own SQL, and the application
    /// already running sends the text it was built with. A baseline given as an IR holds that text: where it differs from the
    /// file's now, neither document says what the old text returns from this database. A baseline given as a source is read with
    /// the query files as they are now, which hold for the application running only if they haven't changed since. A query file
    /// gone since the baseline is still sent by the application running.
    /// </summary>
    private static Dictionary<string, string> NotChecked(IrDocument application, IrDocument database, bool isIr)
    {
        var now = database.Objects.Where(o => o.Kind == IrObjectKind.Query).ToDictionary(o => o.Id, StringComparer.Ordinal);
        var notChecked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var query in application.Objects.Where(o => o.Kind == IrObjectKind.Query))
        {
            if (!now.ContainsKey(query.Id))
            {
                // Not a procedure gone: the application running holds the text itself, and sends it whatever the database has.
                notChecked[query.Id] = $"{query.Id}: the file is gone, but the application already running still sends the text it was built with, which check doesn't infer against this database. Make sure that text still runs on it, or deploy the application first.";
            }
            else if (!isIr)
            {
                notChecked[query.Id] = $"{query.Id}: the baseline is a source, so this query file is checked with its text as it is now; the application already running sends the text it was built with, which may differ. Give the baseline as the IR file it was built with to check that text.";
            }
            else if (query.Text != now[query.Id].Text)
            {
                notChecked[query.Id] = $"{query.Id}: its text changed since the baseline, and the application already running sends the old text, which check doesn't infer against this database. Make sure the old text still runs on it, or deploy the application first.";
            }
        }

        return notChecked;
    }

    /// <summary>A finding as a diagnostic at its procedure: an error for a change the application would fail on, a warning for one it sets aside.</summary>
    private static Diagnostic Said(EmittedModule module, IrFitFinding finding)
    {
        var obj = module.Document.Objects.FirstOrDefault(o => o.Id == finding.Change.Object);
        return finding.Verdict == IrFitVerdict.Refused
            ? At(module, obj, CliCodes.BaselineRefused, $"{finding.Change}, and {finding.Why}. The application already running, built against the baseline, would fail: ship a change it can take first, and this one once it is redeployed.")
            : At(module, obj, CliCodes.BaselineIgnored, $"{finding.Change}, and {finding.Why}. The application already running sets it aside until it is redeployed.");
    }

    /// <summary>
    /// Each procedure whose function returns another shape than the baseline's did (decision 0060): one result returned itself
    /// becoming an object of several, or an object losing a field. The application already running keeps its own functions, so
    /// this is a warning about the next build, whose callers must change. Only when the module writes the functions.
    /// </summary>
    private static IEnumerable<Diagnostic> Shapes(EmittedModule module, IrDocument application)
    {
        if (!module.Config.Emitters.Contains(Emitter.Mssql))
            yield break;
        var now = module.Document.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var known in application.Objects.Where(o => now.ContainsKey(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            var was = MssqlEmitter.ReturnShape(known);
            var shape = MssqlEmitter.ReturnShape(now[known.Id]);
            if (!shape.KeepsCallersOf(was))
            {
                yield return At(module, now[known.Id], CliCodes.ReturnShapeChanged,
                    $"{known.Id}: its function now returns {shape.Description} where it returned {was.Description}, so each call of it changes when the application is next built; the application already running is unaffected. A procedure whose result sets are named returns the same object whatever sets it gains.");
            }
        }
    }

    /// <summary>A diagnostic at the object's place in the SQL, under the folder it was read from; with no place when there is none.</summary>
    private static Diagnostic At(EmittedModule module, IrObject? obj, DiagnosticCode code, string message) =>
        ModuleRun.Rooted(code.At(obj is not null && SourceLocation.TryParse(obj.At, out var at) ? at : null, message), module.Project);
}

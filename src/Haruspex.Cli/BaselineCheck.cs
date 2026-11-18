using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex check --baseline</c>: whether the module fits within the baseline, the database the application already running
/// was built against (<see cref="IrFit"/>). The baseline is read once for the run (<see cref="Baseline"/>), and the module's own
/// database judged against it: each change the application would fail on is an error, each it sets aside a warning, and each
/// function whose return shape changes a warning too. Why: docs/decisions/0069-check-with-a-baseline-judges-the-deploy-window.md.
/// </summary>
internal static class BaselineCheck
{
    /// <summary>What the fit finds, each change at its procedure, judging the module's own database against the baseline.</summary>
    public static IReadOnlyList<Diagnostic> Run(EmittedModule module, Baseline baseline)
    {
        var application = baseline.Document;
        var database = module.Database;
        var notChecked = NotChecked(application, database, baseline.IsIr);
        var findings = IrFit.Of(application, database, new IrFitOptions { StrictObjects = module.Config.Objects == ObjectsPolicy.Strict })
            .Where(f => !notChecked.ContainsKey(f.Change.Object));
        return
        [
            .. findings.Select(f => Said(module, f)),
            .. Shapes(module, application, database),
            .. notChecked.Select(u => At(module, database.Objects.FirstOrDefault(o => o.Id == u.Key), CliCodes.QueryTextUnchecked, u.Value)),
        ];
    }

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
        var obj = module.Database.Objects.FirstOrDefault(o => o.Id == finding.Change.Object);
        return finding.Verdict == IrFitVerdict.Refused
            ? At(module, obj, CliCodes.BaselineRefused, $"{finding.Change}, and {finding.Why}. The application already running, built against the baseline, would fail: ship a change it can take first, and this one once it is redeployed.")
            : At(module, obj, CliCodes.BaselineIgnored, $"{finding.Change}, and {finding.Why}. The application already running sets it aside until it is redeployed.");
    }

    /// <summary>
    /// Each procedure whose function returns another shape than the baseline's did (decision 0060): one result returned itself
    /// becoming an object of several, or an object losing a field. The application already running keeps its own functions, so
    /// this is a warning about the next build, whose callers must change. Only when the module writes the functions.
    /// </summary>
    /// <param name="module">The module, whose config file says whether it writes the functions.</param>
    /// <param name="application">The document the application already running was built against.</param>
    /// <param name="now">The document whose functions the application is next built with: the database's own for <c>check</c>, the
    /// combined one for <c>generate</c>.</param>
    public static IEnumerable<Diagnostic> Shapes(EmittedModule module, IrDocument application, IrDocument now)
    {
        if (!module.Config.Emitters.Contains(Emitter.Mssql))
            yield break;
        var after = now.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var known in application.Objects.Where(o => after.ContainsKey(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            var was = MssqlEmitter.ReturnShape(known);
            var shape = MssqlEmitter.ReturnShape(after[known.Id]);
            if (!shape.KeepsCallersOf(was))
            {
                yield return At(module, after[known.Id], CliCodes.ReturnShapeChanged,
                    $"{known.Id}: its function now returns {shape.Description} where it returned {was.Description}, so each call of it changes when the application is next built; the application already running is unaffected. A procedure whose result sets are named returns the same object whatever sets it gains.");
            }
        }
    }

    /// <summary>A diagnostic at the object's place in the SQL, under the folder it was read from; with no place when there is none.</summary>
    private static Diagnostic At(EmittedModule module, IrObject? obj, DiagnosticCode code, string message) =>
        ModuleRun.Rooted(code.At(obj is not null && SourceLocation.TryParse(obj.At, out var at) ? at : null, message), module.Project);
}

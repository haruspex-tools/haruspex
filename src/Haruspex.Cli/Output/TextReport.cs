using System.Globalization;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;

namespace Haruspex.Cli.Output;

/// <summary>
/// A run's report as text, for a person: the summary on standard output, then the errors and warnings in full on standard
/// error, as compilers write them. The command is run many times a day, so by default it says what it did and what needs
/// attention, and counts the rest: each note is a flag away (<c>--verbose</c>). What it prints, mode by mode:
/// docs/command-line.md; why: docs/decisions/0062-the-command-line-s-output-is-designed.md.
/// </summary>
/// <param name="detail">How much to say.</param>
/// <param name="outputStyle">How standard output is styled.</param>
/// <param name="errorStyle">How standard error is styled.</param>
/// <param name="workingDirectory">The folder paths are shown from.</param>
internal sealed class TextReport(OutputDetail detail, TextStyle outputStyle, TextStyle errorStyle, string workingDirectory)
{
    /// <summary>Writes the report of <paramref name="run"/>.</summary>
    public void Write(RunReport run, TextWriter output, TextWriter error)
    {
        foreach (var refused in run.Refused)
            error.WriteLine(Line(refused));

        foreach (var module in run.Modules)
        {
            // Under --quiet a module is named only when it has an error to say, so the error says which module it is in.
            if (run.Several && (detail != OutputDetail.Quiet || module.Diagnostics.Any(d => d.IsError)))
                error.WriteLine($"haruspex: module {Shown(module.Config ?? "")}");
            Write(module, output, error);
        }

        if (run.Several && run.Refused.Count == 0 && detail != OutputDetail.Quiet)
        {
            var failed = run.Modules.Where(m => !m.Succeeded).Select(m => Shown(m.Config ?? "")).ToList();
            var modules = Counted(run.Modules.Count, "module");
            output.WriteLine(failed.Count == 0
                ? $"haruspex: {modules}, none failed."
                : $"haruspex: {modules}; {failed.Count} failed: {string.Join(", ", failed)}.");
        }
    }

    private void Write(ModuleReport module, TextWriter output, TextWriter error)
    {
        if (module.Succeeded && detail != OutputDetail.Quiet && module.Document is { } document)
            output.WriteLine(Summary(module, document));

        var unknownNotes = module.Unknowns.Select(u => u.Note).OfType<Diagnostic>().ToHashSet();
        foreach (var diagnostic in module.Diagnostics.Where(d => !unknownNotes.Contains(d) && Shows(d)))
            error.WriteLine(Line(diagnostic));

        // An acknowledged warning is said only when asked for, with why it is lived with.
        if (detail == OutputDetail.Verbose)
        {
            foreach (var acknowledged in module.Acknowledged)
                error.WriteLine($"{Line(acknowledged.Warning)} (acknowledged: {Visible.Of(acknowledged.By.Why)})");
        }

        // The unknowns' notes are the subject of a check that fails on unknowns, so it says them without being asked.
        if (detail == OutputDetail.Verbose || module.Diagnostics.Any(d => d.Code == CliCodes.UnknownsFail))
            Notes(module, error);

        if (!module.Succeeded && detail != OutputDetail.Quiet && Closing(module) is { } closing)
            error.WriteLine(closing);
    }

    /// <summary>Whether a diagnostic other than an unknown's note is said: errors always, warnings unless quiet, notes when verbose.</summary>
    private bool Shows(Diagnostic diagnostic) => diagnostic.Severity switch
    {
        DiagnosticSeverity.Error => true,
        DiagnosticSeverity.Warning => detail != OutputDetail.Quiet,
        DiagnosticSeverity.Note => detail == OutputDetail.Verbose,
    };

    /// <summary>
    /// Each unknown's note, grouped under the object it is in, the objects in order of their ids: a developer reads them to fix
    /// one procedure at a time. The lines themselves stay as compilers write them, so editors and terminals still go to each.
    /// </summary>
    private void Notes(ModuleReport module, TextWriter error)
    {
        var notes = module.Unknowns.Where(u => u.Note is not null).Select(u => (u.Unknown.Object, Note: u.Note ?? throw new InvalidOperationException()));
        foreach (var group in notes.GroupBy(n => n.Object).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            error.WriteLine(errorStyle.Strong($"{Visible.Of(group.Key)} ({Counted(group.Count(), "unknown")})"));
            foreach (var (_, note) in group)
                error.WriteLine(Line(note));
        }
    }

    /// <summary>
    /// What a module that succeeded says: what it typed and, for <c>generate</c>, where it wrote, then what changed since the last
    /// run; when it can't say that, why, and its unknowns, counted by reason, each reason's code linking its page.
    /// </summary>
    private string Summary(ModuleReport module, IrDocument document)
    {
        var counted = $"{Typed(document)}, {Unknowns(document.Unknowns.Count)}";
        var listed = document.Unknowns.Count > 0 && detail != OutputDetail.Verbose ? " (--verbose lists them)" : "";

        // A check that passes is one line: it runs in CI and before a commit, where its reader wants the verdict. So is a
        // generate that changed nothing, which is most runs of a command run many times a day.
        var acknowledged = module.Acknowledged.Count == 0
            ? ""
            : $" {Counted(module.Acknowledged.Count, "warning")} acknowledged{(detail == OutputDetail.Verbose ? "" : " (--verbose lists them)")}.";
        if (module.Command == CommandName.Check)
            return $"haruspex: {counted}{listed}. {Shown(module.Out ?? "")} is up to date.{acknowledged}";
        if (module.Since is SinceLastRun.Compared { Nothing: true })
            return $"haruspex: nothing changed since the last run: {counted}{listed}.{acknowledged}";

        var lines = new List<string> { $"haruspex: {counted}. Wrote {EnglishList.Of([.. module.Written.Select(Shown)])}." };
        switch (module.Since)
        {
            case SinceLastRun.Compared compared:
                lines.AddRange(Changes(compared, listed));
                break;
            case SinceLastRun.NotCompared { Why: var why }:
                lines.Add($"Nothing to compare with: {why}.");
                lines.AddRange(ByReason(module));
                break;
            default:
                lines.Add($"First output in {Shown(module.Out ?? "")}, so nothing to compare with.");
                lines.AddRange(ByReason(module));
                break;
        }

        if (acknowledged.Length > 0)
            lines.Add(acknowledged.TrimStart());
        return string.Join('\n', lines);
    }

    /// <summary>
    /// What changed since the last run: each change of claim, marked as added, removed or changed, then the unknowns that are new
    /// or resolved, and a count of those that aren't.
    /// </summary>
    private IEnumerable<string> Changes(SinceLastRun.Compared compared, string listed)
    {
        if (compared.Claims.Count > 0)
        {
            yield return outputStyle.Strong($"{Counted(compared.Claims.Count, "change")} since the last run:");
            foreach (var change in compared.Claims)
                yield return $"  {Marked(change.Kind)} {Visible.Of(change.ToString())}";
        }
        else if (compared.Rewritten.Count > 0)
        {
            yield return $"No claim changed since the last run, but the text of {EnglishList.Of([.. compared.Rewritten])} did: a comment written from a reason, or the output of another version.";
        }
        else
        {
            yield return "No claim changed since the last run.";
        }

        if (compared.New.Count > 0 || compared.Resolved.Count > 0)
        {
            yield return outputStyle.Strong($"Unknowns: {compared.New.Count} new, {compared.Resolved.Count} resolved, {compared.Unchanged} as before{listed}.");
            foreach (var added in compared.New)
            {
                var place = added.Note is { } note ? $" ({DiagnosticFormatter.Parts(Relative(note)).Place})" : "";
                yield return $"  {outputStyle.Added("+")} {Visible.Of($"{added.Unknown.Object} {added.Unknown.Path}")}: {Reason(added.Unknown.Reason)}{Visible.Of(place)}";
            }

            foreach (var resolved in compared.Resolved)
                yield return $"  {outputStyle.Removed("-")} {Visible.Of($"{resolved.Object} {resolved.Path}")}: {Reason(resolved.Reason)}";
            if (compared.New.Select(u => u.Unknown).Concat(compared.Resolved).Any(u => InferenceCodes.Of(u.Reason) is not null) && Pages() is { } pages)
                yield return pages;
        }
        else if (compared.Unchanged > 0)
        {
            yield return $"{Counted(compared.Unchanged, "unknown")}, as before{listed}.";
        }
    }

    /// <summary>
    /// Where each code's page is, said once under a list of codes when the codes aren't links themselves: in a log, a pipe, or a
    /// terminal not known to show links. Null when they are links.
    /// </summary>
    private string? Pages() => outputStyle.Links ? null : outputStyle.Faint($"What each code means: {DiagnosticPages.Index}");

    /// <summary>A change's mark: <c>+</c> for what a reader receives that it didn't, <c>-</c> for what it no longer does, <c>~</c> for the rest.</summary>
    private string Marked(IrChangeKind kind) => kind switch
    {
        IrChangeKind.ObjectAdded or IrChangeKind.ParameterAdded or IrChangeKind.ResultAdded or IrChangeKind.FieldAdded
            or IrChangeKind.ValuesAdded or IrChangeKind.ValueSetAdded => outputStyle.Added("+"),
        IrChangeKind.ObjectRemoved or IrChangeKind.ParameterRemoved or IrChangeKind.ResultRemoved or IrChangeKind.FieldRemoved
            or IrChangeKind.ValuesRemoved or IrChangeKind.ValueSetRemoved => outputStyle.Removed("-"),
        _ => outputStyle.Changed("~"),
    };

    /// <summary>An unknown's reason as the report names it: its code, linked, and what the code means.</summary>
    private string Reason(IrUnknownReason reason) => InferenceCodes.Of(reason) is { } code
        ? $"{outputStyle.Link(outputStyle.Note(code.ToString()), DiagnosticPages.Of(code))} {code.Title}"
        : "results only a declaration can give";

    /// <summary>
    /// The unknowns by reason, most first: the count, the code (a link to its page where the terminal shows links) and what the
    /// code means. One only a declaration can resolve has no note of its own; the warnings say what to declare.
    /// </summary>
    private IEnumerable<string> ByReason(ModuleReport module)
    {
        if (module.Unknowns.Count == 0)
            yield break;
        var reasons = module.Unknowns
            .GroupBy(u => u.Note?.Code)
            .Select(g => (Code: g.Key, Count: g.Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Code?.Number ?? int.MaxValue)
            .ToList();
        var width = reasons.Max(r => r.Count).ToString(CultureInfo.InvariantCulture).Length;
        yield return outputStyle.Strong(detail == OutputDetail.Verbose ? "Unknowns by reason:" : "Unknowns by reason (--verbose lists each):");
        foreach (var (code, count) in reasons)
        {
            var number = count.ToString(CultureInfo.InvariantCulture).PadLeft(width);
            yield return code is null
                ? $"  {number}  results only a declaration can give, as the warnings say"
                : $"  {number}  {outputStyle.Link(outputStyle.Note(code.ToString()), DiagnosticPages.Of(code))}  {code.Title}";
        }

        if (reasons.Any(r => r.Code is not null) && Pages() is { } pages)
            yield return pages;
    }

    /// <summary>The line that closes a module that failed, saying what follows from it; null when the errors say it all.</summary>
    private string? Closing(ModuleReport module)
    {
        if (module.Command == CommandName.Check)
        {
            var changes = module.Diagnostics.Count(d => d.Code == CliCodes.ClaimChanged);
            return changes == 0
                ? null
                : $"haruspex: {Counted(changes, "change")} from the module in {Shown(module.Out ?? "")}. If they are meant, run haruspex generate and commit what it writes; if not, change the SQL back.";
        }

        var sql = module.Diagnostics.Count(d => d.IsError && d.At is SourceLocation);
        return sql == 0 ? null : $"haruspex: {Counted(sql, "error")}; nothing was written.";
    }

    /// <summary>A diagnostic as compilers write one, its file shown from the working directory, and its head coloured by severity.</summary>
    private string Line(Diagnostic diagnostic)
    {
        var (place, _, text) = DiagnosticFormatter.Parts(Relative(diagnostic));
        var word = DiagnosticFormatter.Word(diagnostic.Severity);
        var code = errorStyle.Link(diagnostic.Code.ToString(), DiagnosticPages.Of(diagnostic.Code));
        var styled = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => errorStyle.Error(word),
            DiagnosticSeverity.Warning => errorStyle.Warning(word),
            DiagnosticSeverity.Note => errorStyle.Note(word),
        };
        return $"{Visible.Of(place)}: {styled} {code}: {Visible.Of(text)}";
    }

    /// <summary>The diagnostic with its file shown from the working directory (<see cref="Shown"/>).</summary>
    private Diagnostic Relative(Diagnostic diagnostic) => diagnostic.At switch
    {
        SourceLocation at => diagnostic with { At = at with { File = Shown(at.File) } },
        JsonLocation at => diagnostic with { At = at with { File = Shown(at.File) } },
        FileLocation at => diagnostic with { At = at with { File = Shown(at.File) } },
        null => diagnostic,
    };

    /// <summary>
    /// A path as the report shows it: from the working directory when it is below it, which is shorter and what a developer
    /// typed; whole otherwise. With <c>/</c> on every platform (<see cref="DiagnosticFormatter.PathAsShown"/>).
    /// </summary>
    public string Shown(string path) => Visible.Of(DisplayPath.From(workingDirectory, path));

    /// <summary>What a module types: its procedures, and its query files when there are any.</summary>
    internal static string Typed(IrDocument document)
    {
        var procedures = document.Objects.Count(o => o.Kind == IrObjectKind.Procedure);
        var queries = document.Objects.Count - procedures;
        var typed = Counted(procedures, "procedure");
        return queries == 0 ? typed : $"{typed}, {Counted(queries, "query file")}";
    }

    private static string Unknowns(int count) => count == 0 ? "no unknowns" : Counted(count, "unknown");

    /// <summary><paramref name="count"/> and the noun, plural unless it is one: <c>1 procedure</c>, <c>6 procedures</c>.</summary>
    internal static string Counted(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");
}

using System.Globalization;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli.Output;

/// <summary>
/// A run's report as text, for a person: the summary on standard output, then the errors and warnings in full on standard
/// error, as compilers write them. The command is run many times a day, so by default it says what it did and what needs
/// attention, and counts the rest: each note is a flag away (<c>--verbose</c>).
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
            error.WriteLine(errorStyle.Strong($"{group.Key} ({Counted(group.Count(), "unknown")})"));
            foreach (var (_, note) in group)
                error.WriteLine(Line(note));
        }
    }

    /// <summary>
    /// What a module that succeeded says: what it typed and, for <c>generate</c>, where it wrote; then its unknowns, counted by
    /// reason, each reason's code linking its page.
    /// </summary>
    private string Summary(ModuleReport module, IrDocument document)
    {
        var lines = new List<string>();
        var counted = $"{Typed(document)}, {Unknowns(document.Unknowns.Count)}";
        if (module.Command == CommandName.Check)
        {
            // A check that passes is one line: it is run in CI and before a commit, where its reader wants the verdict.
            var listed = document.Unknowns.Count > 0 && detail != OutputDetail.Verbose ? " (--verbose lists them)" : "";
            return $"haruspex: {counted}{listed}. {Shown(module.Out ?? "")} is up to date.";
        }

        lines.Add($"haruspex: {counted}. Wrote {EnglishList.Of([.. module.Written.Select(Shown)])}.");
        if (document.Unknowns.Count > 0)
            lines.AddRange(ByReason(module));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The unknowns by reason, most first: the count, the code (a link to its page where the terminal shows links) and what the
    /// code means. One only a declaration can resolve has no note of its own; the warnings say what to declare.
    /// </summary>
    private IEnumerable<string> ByReason(ModuleReport module)
    {
        var reasons = module.Unknowns
            .GroupBy(u => u.Note?.Code)
            .Select(g => (Code: g.Key, Count: g.Count()))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Code?.Number ?? int.MaxValue)
            .ToList();
        var width = reasons.Max(r => r.Count).ToString(CultureInfo.InvariantCulture).Length;
        yield return detail == OutputDetail.Verbose ? "Unknowns by reason:" : "Unknowns by reason (--verbose lists each):";
        foreach (var (code, count) in reasons)
        {
            var number = count.ToString(CultureInfo.InvariantCulture).PadLeft(width);
            yield return code is null
                ? $"  {number}  results only a declaration can give, as the warnings say"
                : $"  {number}  {outputStyle.Link(outputStyle.Note(code.ToString()), DiagnosticPages.Of(code))}  {code.Title}";
        }
    }

    /// <summary>The line that closes a module that failed, saying what follows from it; null when the errors say it all.</summary>
    private static string? Closing(ModuleReport module)
    {
        if (module.Command == CommandName.Check)
        {
            var changes = module.Diagnostics.Count(d => d.Code == CliCodes.ClaimChanged);
            return changes == 0
                ? null
                : $"haruspex: {Counted(changes, "change")} from what is committed. If they are meant, run haruspex generate and commit what it writes; if not, change the SQL back.";
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
        return $"{place}: {styled} {code}: {text}";
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
    public string Shown(string path) => DisplayPath.From(workingDirectory, path);

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

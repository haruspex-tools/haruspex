using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Cli;

/// <summary>
/// The warnings a config file acknowledges (<c>acknowledge</c>), applied to a run: a warning matched by an entry's code and
/// subject is set aside, to be counted rather than said, and an entry that matches nothing is said, so that one left behind
/// after its cause is fixed doesn't hide the next warning of that subject. Only warnings that carry a subject can be
/// acknowledged: those about the source a user may have read and decided to live with. An error must be fixed, and a warning
/// about the config file is fixed there. Why: docs/decisions/0063-a-warning-is-acknowledged-by-its-code-and-subject.md.
/// </summary>
internal static class Acknowledgements
{
    /// <summary>
    /// The warnings that can be acknowledged, each with a subject: a referenced DACPAC not found (its file name), a referenced
    /// project not found (its file name), a build target that isn't read (its name), and a write to an object the project
    /// doesn't define (the object's name).
    /// </summary>
    public static IReadOnlySet<DiagnosticCode> Codes { get; } = new HashSet<DiagnosticCode>
    {
        SourceCodes.ReferenceNotFound, SourceCodes.ProjectNotFound, SourceCodes.PartNotRead, InferenceCodes.UndefinedWriteTarget,
    };

    /// <summary>Each entry that names a code that can't be acknowledged, as an error at the entry: the config file is wrong.</summary>
    public static IEnumerable<Diagnostic> Refused(HaruspexConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var codes = string.Join(", ", Codes.OrderBy(c => c.Number).Select(c => c.ToString()));
        return config.Acknowledged
            .Where(a => !Codes.Any(c => c.ToString() == a.Code))
            .Select(a => CliCodes.NotAcknowledgeable.At(a.At,
                $"{a.Code} can't be acknowledged: only a warning about the source that names its subject can be, and those are {codes}. Fix what {a.Code} says instead."));
    }

    /// <summary>
    /// The run's diagnostics with each acknowledged warning set aside; and, when <paramref name="sayUnused"/>, a warning for each
    /// entry that matched none. A run whose selection the command line narrowed (<c>--include</c>) doesn't say unused entries: a
    /// warning about a procedure left out of the run isn't there to match.
    /// </summary>
    public static (EquatableArray<Diagnostic> Kept, EquatableArray<AcknowledgedWarning> Acknowledged) Apply(IEnumerable<Diagnostic> diagnostics, HaruspexConfig config, bool sayUnused)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(config);
        var kept = new List<Diagnostic>();
        var acknowledged = new List<AcknowledgedWarning>();
        var used = new HashSet<Acknowledgement>();
        foreach (var diagnostic in diagnostics)
        {
            var by = diagnostic is { Severity: DiagnosticSeverity.Warning, Subject: { } subject }
                ? config.Acknowledged.FirstOrDefault(a => a.Code == diagnostic.Code.ToString() && a.Subject == subject)
                : null;
            if (by is null)
            {
                kept.Add(diagnostic);
                continue;
            }

            acknowledged.Add(new AcknowledgedWarning(diagnostic, by));
            used.Add(by);
        }

        if (sayUnused)
        {
            kept.AddRange(config.Acknowledged.Where(a => !used.Contains(a) && Codes.Any(c => c.ToString() == a.Code)).Select(a => CliCodes.AcknowledgesNothing.At(a.At,
                $"acknowledges {a.Code} for \"{a.Subject}\", but this run gave no such warning. Remove the entry, or correct its code or its subject: {a.Code}'s subject is the name the warning gives.")));
        }

        return ([.. kept], [.. acknowledged]);
    }
}

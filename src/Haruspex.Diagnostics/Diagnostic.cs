namespace Haruspex.Diagnostics;

/// <summary>
/// A problem found by a step of haruspex, in one shape for every step: what kind it is (its code, which gives its
/// severity), what is wrong and what to do, and where.
/// </summary>
/// <param name="Code">The kind of problem, which has a page of its own and decides the severity.</param>
/// <param name="Message">What is wrong, and what to do about it, in a sentence or two.</param>
/// <param name="At">Where, when it has a place.</param>
public sealed record Diagnostic(DiagnosticCode Code, string Message, DiagnosticLocation? At)
{
    /// <summary>
    /// What the problem is about, by the name a config file gives to acknowledge it, for a warning a user may have read and
    /// decided to live with: a package's file name (<c>master.dacpac</c>), a project's, a build target's, an object's. Null for a
    /// problem that can't be acknowledged, which is most: an error must be fixed, and a warning about the config file is fixed
    /// there. Acknowledging is by code and subject together, never by code alone, so one warning read is not every warning of
    /// its kind (docs/decisions/0065-a-warning-is-acknowledged-by-its-code-and-subject.md).
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>How serious it is: its code's severity.</summary>
    public DiagnosticSeverity Severity => Code.Severity;

    /// <summary>Whether it stops the step that found it.</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;

    /// <summary>The diagnostic as <see cref="DiagnosticFormatter"/> writes it, with paths as they are held.</summary>
    public override string ToString() => DiagnosticFormatter.Format(this);
}

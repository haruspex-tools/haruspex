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
    /// <summary>How serious it is: its code's severity.</summary>
    public DiagnosticSeverity Severity => Code.Severity;

    /// <summary>Whether it stops the step that found it.</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;

    /// <summary>The diagnostic as <see cref="DiagnosticFormatter"/> writes it, with paths as they are held.</summary>
    public override string ToString() => DiagnosticFormatter.Format(this);
}

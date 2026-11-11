namespace Haruspex.Diagnostics;

/// <summary>
/// Something found in the source: an error that stops analysis, such as a parse error, or a warning about a gap the
/// results cannot show, such as a table the project does not define.
/// </summary>
/// <remarks>
/// The design gives a diagnostic a code too. None needs one yet, so it is left for the slice that does rather than invented
/// here. The severity came with slice 6d, the first warning.
/// </remarks>
/// <param name="Message">What is wrong, in a sentence.</param>
/// <param name="At">Where, when it is known.</param>
/// <param name="Severity">Whether it stops analysis.</param>
public sealed record Diagnostic(string Message, SourceLocation? At, DiagnosticSeverity Severity = DiagnosticSeverity.Error)
{
    /// <summary><c>file:line:column: error: message</c>, or <c>warning</c>.</summary>
    public override string ToString()
    {
        var severity = Severity == DiagnosticSeverity.Warning ? "warning" : "error";
        return At is null ? $"{severity}: {Message}" : $"{At}: {severity}: {Message}";
    }
}

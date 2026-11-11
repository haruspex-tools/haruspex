namespace Haruspex.Diagnostics;

/// <summary>
/// Something found in the source: an error that stops analysis, such as a parse error, or a warning about a gap the
/// results cannot show, such as a table the project does not define.
/// </summary>
/// <remarks>
/// A diagnostic has no code yet. None needs one, so codes are left until something does, rather than invented here.
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

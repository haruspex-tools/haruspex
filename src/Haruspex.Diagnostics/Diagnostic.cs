namespace Haruspex.Diagnostics;

/// <summary>A position in a source file.</summary>
/// <param name="File">The path relative to the source root, with <c>/</c> separators on every platform.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
public sealed record SourceLocation(string File, int Line, int Column)
{
    /// <summary><c>file:line:column</c>, the form compilers and editors recognise.</summary>
    public override string ToString() => $"{File}:{Line}:{Column}";
}

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

/// <summary>How serious a <see cref="Diagnostic"/> is.</summary>
public enum DiagnosticSeverity
{
    /// <summary>It stops analysis: nothing inferred despite it is trustworthy.</summary>
    Error,

    /// <summary>The results stand, but something they rest on could not be checked.</summary>
    Warning,
}

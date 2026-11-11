namespace Haruspex.Diagnostics;

/// <summary>How serious a <see cref="Diagnostic"/> is.</summary>
public enum DiagnosticSeverity
{
    /// <summary>It stops analysis: nothing inferred despite it is trustworthy.</summary>
    Error,

    /// <summary>The results stand, but something they rest on could not be checked.</summary>
    Warning,
}

namespace Haruspex.Diagnostics;

/// <summary>
/// How serious a <see cref="Diagnostic"/> is, which its code decides. The three levels are SARIF's: <c>error</c>,
/// <c>warning</c> and <c>note</c>.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>The step can't give its value: nothing after it runs, and nothing is written.</summary>
    Error,

    /// <summary>The value stands, but something it rests on could not be checked, or the input could be simpler.</summary>
    Warning,

    /// <summary>
    /// The value stands and says what it is: an unknown in the output, with its reason. A note never changes whether a run
    /// succeeds.
    /// </summary>
    Note,
}

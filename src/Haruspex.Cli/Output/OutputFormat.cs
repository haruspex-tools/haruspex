namespace Haruspex.Cli.Output;

/// <summary>What <c>--format</c> asks for: who reads the report.</summary>
internal enum OutputFormat
{
    /// <summary>A person at a terminal or reading a log (<see cref="TextReport"/>).</summary>
    Text,

    /// <summary>A tool (<see cref="JsonReport"/>).</summary>
    Json,

    /// <summary>A code-scanning service (<see cref="SarifReport"/>).</summary>
    Sarif,

    /// <summary>
    /// GitHub Actions: the text report, then each error and warning as an annotation (<see cref="GitHubAnnotations"/>). The
    /// default when <c>GITHUB_ACTIONS</c> is <c>true</c>.
    /// </summary>
    GitHub,
}

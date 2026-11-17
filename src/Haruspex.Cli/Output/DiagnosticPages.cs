using Haruspex.Diagnostics;

namespace Haruspex.Cli.Output;

/// <summary>
/// Where each diagnostic code's page is published, for a link from the code in a terminal, a SARIF rule's help and the JSON
/// report: the page in the repository's <c>docs/diagnostics</c>, on <c>main</c>. Why there, and not a version's own copy or a site of
/// its own: docs/decisions/0062-the-command-line-s-output-is-designed.md.
/// </summary>
internal static class DiagnosticPages
{
    /// <summary>The folder of the pages, the index included.</summary>
    public const string Folder = "https://github.com/sharpblue/haruspex/blob/main/docs/diagnostics/";

    /// <summary>The index of every code.</summary>
    public const string Index = Folder + "README.md";

    /// <summary>The page of <paramref name="code"/>.</summary>
    public static string Of(DiagnosticCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return $"{Folder}{code}.md";
    }
}

using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli.Output;

/// <summary>
/// Where each diagnostic code's page is published, for a link from the code in a terminal, a SARIF rule's help and the JSON
/// report: <c>https://haruspex.tools/diagnostics/HAR2009</c>, which leads to the page in the repository's <c>docs/diagnostics</c>,
/// on <c>main</c>. Why one page for every version: docs/decisions/0064-the-command-line-s-output-is-designed.md; why on
/// haruspex.tools: docs/decisions/0121-every-address-haruspex-writes-is-served-from-haruspex-tools.md.
/// </summary>
internal static class DiagnosticPages
{
    /// <summary>The folder of the pages, which is also the index of every code.</summary>
    public const string Folder = HaruspexSite.Diagnostics;

    /// <summary>The index of every code.</summary>
    public const string Index = Folder;

    /// <summary>The page of <paramref name="code"/>.</summary>
    public static string Of(DiagnosticCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return $"{Folder}{code}";
    }
}

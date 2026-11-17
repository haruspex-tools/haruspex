using Haruspex.Diagnostics;

namespace Haruspex.Cli.Output;

/// <summary>
/// A run's errors and warnings as GitHub Actions workflow commands (<c>--format github</c>, the default when
/// <c>GITHUB_ACTIONS</c> is <c>true</c>): <c>::error file=db/get_order.sql,line=7,col=5,title=HAR1001 …::message</c>, which the
/// runner reads from standard output and shows on the pull request at the line, and in the job's summary. They follow the text
/// report, which the log still shows as it is. Notes are annotations only with <c>--verbose</c>: a module's hundred unknowns
/// would bury its one error, and GitHub shows only the first few of each kind for a step. A file is named from the repository's
/// root (<c>GITHUB_WORKSPACE</c>), which is how GitHub finds it.
/// </summary>
/// <param name="root">The folder each file is named from.</param>
/// <param name="detail">How much the text report says, which decides whether notes are annotated.</param>
internal sealed class GitHubAnnotations(string root, OutputDetail detail)
{
    /// <summary>Writes an annotation for each error and warning of <paramref name="run"/>, and for each note when verbose.</summary>
    public void Write(RunReport run, TextWriter output)
    {
        foreach (var diagnostic in run.Refused.Concat(run.Modules.SelectMany(m => m.Diagnostics)))
        {
            if (Command(diagnostic.Severity) is { } command)
                output.WriteLine(Annotation(command, diagnostic));
        }
    }

    /// <summary>The workflow command for a severity, or null when it isn't annotated.</summary>
    private string? Command(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Note => detail == OutputDetail.Verbose ? "notice" : null,
    };

    private string Annotation(string command, Diagnostic diagnostic)
    {
        var properties = new List<string>();
        var (file, line, column) = Place.Of(diagnostic);
        if (file is not null)
            properties.Add($"file={Property(DisplayPath.From(root, file))}");
        if (line is { } l)
            properties.Add(FormattableString.Invariant($"line={l}"));
        if (column is { } c)
            properties.Add(FormattableString.Invariant($"col={c}"));
        properties.Add($"title={Property($"{diagnostic.Code} {diagnostic.Code.Title}")}");
        return $"::{command} {string.Join(',', properties)}::{Data(DiagnosticFormatter.Parts(diagnostic).Text)}";
    }

    /// <summary>A message, escaped as the runner reads one: <c>%</c>, carriage return and line feed as <c>%25</c>, <c>%0D</c> and <c>%0A</c>.</summary>
    internal static string Data(string text) =>
        text.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    /// <summary>A property's value, escaped as a message is and also its separators, <c>:</c> and <c>,</c>, as <c>%3A</c> and <c>%2C</c>.</summary>
    internal static string Property(string text) =>
        Data(text).Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);
}

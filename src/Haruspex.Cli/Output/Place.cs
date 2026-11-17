using Haruspex.Diagnostics;

namespace Haruspex.Cli.Output;

/// <summary>
/// Where a diagnostic is, as a machine format records it: a file, a line and a column, each absent when the location has none.
/// The JSON report, SARIF and GitHub's annotations each need it so.
/// </summary>
/// <param name="File">The file, as held; null when the diagnostic has no place.</param>
/// <param name="Line">The line, from 1; null for a file as a whole or no place.</param>
/// <param name="Column">The column, from 1; null when the line is.</param>
internal readonly record struct Place(string? File, int? Line, int? Column)
{
    /// <summary>The place of <paramref name="diagnostic"/>.</summary>
    public static Place Of(Diagnostic diagnostic) => diagnostic.At switch
    {
        SourceLocation at => new Place(at.File, at.Line, at.Column),
        JsonLocation at => new Place(at.File, at.Line, at.Column),
        FileLocation at => new Place(at.File, null, null),
        null => new Place(null, null, null),
    };
}

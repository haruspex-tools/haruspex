using Haruspex.Diagnostics;

namespace Haruspex.Cli.Output;

/// <summary>How every report shows a path the run made full: from the working directory when the path is below it.</summary>
internal static class DisplayPath
{
    /// <summary>
    /// <paramref name="path"/> from <paramref name="workingDirectory"/> when it is below it (<c>src/db/haruspex.ir.json</c>), which is
    /// shorter and what a developer typed; whole otherwise, since a path climbing out (<c>../../shared</c>) is harder to read than
    /// the place itself. A path held relative, as a location inside a DACPAC is, is kept. With <c>/</c> on every platform
    /// (<see cref="DiagnosticFormatter.PathAsShown"/>).
    /// </summary>
    public static string From(string workingDirectory, string path)
    {
        if (path.Length == 0 || !Path.IsPathFullyQualified(path))
            return DiagnosticFormatter.PathAsShown(path);
        var relative = Path.GetRelativePath(workingDirectory, path);
        var below = relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        return DiagnosticFormatter.PathAsShown(below ? relative : path);
    }
}

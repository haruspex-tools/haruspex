namespace Haruspex.TestSupport;

/// <summary>
/// What the CLI printed, as a test compares it: with the test's folder taken off the front of each path, and LF line endings,
/// so that one expected text holds on every platform.
/// </summary>
internal static class CliOutput
{
    /// <summary>
    /// <paramref name="printed"/> with <paramref name="folder"/> left out of each path, whether haruspex prints it (with <c>/</c>,
    /// <c>DiagnosticFormatter.PathAsShown</c>) or it is quoted from the system, as in a file system's error, which writes the
    /// platform's own. A <c>\</c> anywhere else is kept: haruspex quotes a path from a project or a package as it was written
    /// (<c>..\shelf\shelf.sqlproj</c>), on every platform, and a test must see that it does.
    /// </summary>
    public static string Shown(string printed, string folder) =>
        printed.ReplaceLineEndings("\n")
            .Replace(folder + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)
            .Replace(folder.Replace('\\', '/') + "/", "", StringComparison.Ordinal);
}

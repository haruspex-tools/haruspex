namespace Haruspex.TestSupport;

/// <summary>
/// What the CLI printed, as a test compares it: with the test's folder taken off the front of each path, and LF line endings,
/// so that one expected text holds on every platform.
/// </summary>
internal static class CliOutput
{
    /// <summary>
    /// <paramref name="printed"/> with <paramref name="folder"/> left out of each path, whether a location prints it (with <c>/</c>,
    /// <c>DiagnosticFormatter.PathAsShown</c>) or a message holds it as the platform writes it, and on Windows every <c>\</c> made
    /// <c>/</c>, so that a path inside a message reads as a location does.
    /// </summary>
    public static string Shown(string printed, string folder)
    {
        var shown = printed.ReplaceLineEndings("\n")
            .Replace(folder + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)
            .Replace(folder.Replace('\\', '/') + "/", "", StringComparison.Ordinal);
        return OperatingSystem.IsWindows() ? shown.Replace('\\', '/') : shown;
    }
}

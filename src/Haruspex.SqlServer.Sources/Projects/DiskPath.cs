namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// A path as a project file writes it, found on this machine. Projects are written on Windows, with backslashes, and a build
/// there finds a file whatever the case of its name; so a path is read with either slash, and when no file has its exact case,
/// the one that differs only in case is taken (decision 0044).
/// </summary>
internal static class DiskPath
{
    /// <summary>
    /// Whether <paramref name="path"/> is rooted the way only Windows roots one (a drive, <c>C:\…</c>; a share, <c>\\server\…</c>;
    /// the drive's root, <c>\build\…</c>) and this isn't Windows: such a path names no place here, whatever folder it is read
    /// from, and joined to one it would name a file that can't exist.
    /// </summary>
    public static bool IsRootedElsewhere(string path) =>
        !OperatingSystem.IsWindows()
        && ((path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') || path.StartsWith('\\'));

    /// <summary>
    /// The full path <paramref name="written"/> names, relative to <paramref name="folder"/>, with <c>/</c> or <c>\</c>: the
    /// path as written made full, not looked for on disk.
    /// </summary>
    public static string Full(string folder, string written) =>
        Path.GetFullPath(Path.Join(folder, written.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// The file or folder <paramref name="written"/> names, relative to <paramref name="folder"/>, as it is on disk; or null, with
    /// why, when there is none, or more than one whose names differ only in case.
    /// </summary>
    public static string? Find(string folder, string written, out string? problem)
    {
        problem = null;
        if (IsRootedElsewhere(written))
        {
            problem = $"{written} is rooted the way only Windows roots a path, so it names no place on this machine";
            return null;
        }

        return Locate(Full(folder, written), written, out problem);
    }

    /// <summary>
    /// Whether this platform's file systems usually ignore case (NTFS on Windows, and macOS's own), so that a path in any case that
    /// exists says nothing about how its names are written on disk.
    /// </summary>
    private static readonly bool CaseIgnored = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>Every entry of a folder, hidden ones too, whose name is a pattern matched ignoring case.</summary>
    private static readonly EnumerationOptions AnyCase = new() { MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = 0, IgnoreInaccessible = true };

    /// <summary>
    /// The file or folder at the full path <paramref name="full"/> as it is on disk, ignoring case; or null, with why, when there is
    /// none, or more than one. <paramref name="written"/> is how the project wrote it, for the problem.
    /// </summary>
    /// <remarks>
    /// "As it is on disk" holds on every platform, so that a project gives the same locations, and so the same IR, wherever it is
    /// read: on a file system that ignores case, a path that exists as written may still be written in another case than its
    /// names, so each name is read from its folder (<c>A_path_is_found_whatever_the_case_of_its_names_as_a_build_on_windows_finds_it</c>,
    /// which failed on macOS before). On one that tells case apart, a path that exists is exactly as on disk.
    /// </remarks>
    public static string? Locate(string full, string written, out string? problem)
    {
        problem = null;
        if (!CaseIgnored && (File.Exists(full) || Directory.Exists(full)))
            return full;

        // Each part as it is on disk: its own name if a file has it exactly, otherwise the one name that differs only in case.
        var root = Path.GetPathRoot(full) ?? "";
        var found = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var alike = Alike(found, part);
            var exact = alike.Where(e => Path.GetFileName(e) == part).ToList();
            var named = exact.Count == 1 ? exact : alike;
            if (named.Count != 1)
            {
                if (named.Count > 1)
                    problem = $"{written} names {named.Count} files whose names differ only in case, as a build on Windows can't tell apart";
                return null;
            }

            found = named[0];
        }

        return found;
    }

    /// <summary>
    /// The entries of <paramref name="folder"/> whose name is <paramref name="name"/> ignoring case, as they are written on disk. The
    /// name is the pattern the folder is searched with, which Windows looks up rather than lists, unless it holds a wildcard.
    /// </summary>
    private static List<string> Alike(string folder, string name) =>
        Directory.Exists(folder)
            ? [.. Directory.EnumerateFileSystemEntries(folder, name.AsSpan().IndexOfAny('*', '?') >= 0 ? "*" : name, AnyCase)
                .Where(e => string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase))]
            : [];
}

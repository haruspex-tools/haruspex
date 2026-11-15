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
    /// The file or folder at the full path <paramref name="full"/> as it is on disk, ignoring case; or null, with why, when there is
    /// none, or more than one. <paramref name="written"/> is how the project wrote it, for the problem.
    /// </summary>
    public static string? Locate(string full, string written, out string? problem)
    {
        problem = null;
        if (File.Exists(full) || Directory.Exists(full))
            return full;

        // Each part as it is on disk, ignoring case.
        var root = Path.GetPathRoot(full) ?? "";
        var found = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var exact = Path.Join(found, part);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                found = exact;
                continue;
            }

            var alike = Directory.Exists(found)
                ? Directory.EnumerateFileSystemEntries(found).Where(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            if (alike.Count != 1)
            {
                if (alike.Count > 1)
                    problem = $"{written} names {alike.Count} files whose names differ only in case, as a build on Windows can't tell apart";
                return null;
            }

            found = alike[0];
        }

        return found;
    }
}

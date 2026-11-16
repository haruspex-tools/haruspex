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
    /// Where the folder at the full path <paramref name="full"/> really is: every link on its path followed, and each name in the
    /// case it has on disk, so that two paths naming one folder give one string, compared ordinally; or null when links on it lead
    /// round in a circle, so that it is nowhere. Where a link leads to nothing, the path is where it leads, which isn't there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path is read from its root down, as <see cref="Locate"/> reads one, rather than by asking the folder for its link and
    /// then each folder above it in turn, which is how it was first written. That asked the root too, and Windows can't read a
    /// link at a drive's root (<c>C:\</c>) or a share's (<c>\\server\share</c>): .NET reads one through <c>FindFirstFile</c>, which
    /// opens no root, so every project read on Windows failed with <c>DirectoryNotFoundException</c> (7w, the first Windows run).
    /// A root is no link, so it is taken as written, without touching the disk; on Windows in capitals, since its drive letter,
    /// server and share are named in any case.
    /// </para>
    /// <para>
    /// Below the root, each name is found in its folder's listing, so that a path written in another case than the disk's, where
    /// the file system ignores case (Windows, macOS), gives the disk's (<see cref="Named"/>). Each folder is then asked for its
    /// link, one link at a time (<see cref="Followed"/>), and where it leads is made real in turn, since that path may run through
    /// links too. <see cref="FileSystemInfo.ResolveLinkTarget"/> reads a symbolic link on every platform and a junction on
    /// Windows. Asked for the end of a chain instead, it throws for a link that leads to nothing, and for a junction read through a
    /// share (<c>\\localhost\C$\…</c>) it gives a path that has lost its <c>\\</c> and so names a folder under the working one (both
    /// observed, 7w): one link at a time, a junction gives the path it records, and that is all that is read.
    /// </para>
    /// <para>
    /// The root is taken for itself, so a drive that is another folder under a second name (<c>subst</c>, a mapped drive) or a
    /// share of a local folder isn't known to be that folder. A link from under it back to the folder by its own name is then not
    /// seen as a loop, and the walk reads what is under the folder twice, which a build reads once: a script is then defined
    /// twice (HAR1002), never typed wrongly. Telling them apart needs the file system's identity of a folder, which .NET doesn't
    /// give; a known limit.
    /// </para>
    /// <para>
    /// The platform's own call (<c>realpath</c>, <c>GetFinalPathNameByHandle</c>) would do this in one step, but differently on
    /// each platform and through native code; this is one reading on all three, made of calls .NET already makes portable.
    /// </para>
    /// </remarks>
    public static string? Real(string full) => RealAfter(full, 0);

    /// <summary>
    /// Where the entry <paramref name="name"/>, as its folder lists it, of the folder whose real path is <paramref name="folder"/>
    /// really is: <see cref="Real"/> one name further down, for a walk that has the name from the listing already.
    /// </summary>
    public static string? RealEntry(string folder, string name) => Followed(Path.Join(folder, name), 0);

    /// <summary>
    /// How many links a path may pass through before it is taken to go round in a circle, as Linux counts them (at most
    /// 40); Windows allows 63 reparse points on a path.
    /// </summary>
    private const int MostLinks = 40;

    /// <summary><see cref="Real"/>, <paramref name="links"/> having been followed to reach <paramref name="full"/>.</summary>
    private static string? RealAfter(string full, int links)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var real = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            real = Followed(Path.Join(real, Named(real, part)), links);
            if (real is null)
                return null;
        }

        return real;
    }

    /// <summary>
    /// The real path of where the link at <paramref name="path"/> leads, <paramref name="links"/> having been followed to reach it;
    /// <paramref name="path"/> when it is no link or isn't there.
    /// </summary>
    private static string? Followed(string path, int links)
    {
        FileSystemInfo? target;
        try
        {
            target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: false);
        }
        catch (IOException)
        {
            // Nothing is there to be a link: the path a link led to, which a walk then finds missing.
            target = null;
        }

        return target is null ? path : links == MostLinks ? null : RealAfter(target.FullName, links + 1);
    }

    /// <summary>
    /// The name <paramref name="name"/> in <paramref name="folder"/> as it is written on disk: its own when an entry has it exactly
    /// (always, where the file system tells case apart), otherwise the one entry's that differs only in case; as given when no
    /// entry has it or several do.
    /// </summary>
    private static string Named(string folder, string name)
    {
        if (!CaseIgnored)
            return name;
        var alike = Alike(folder, name);
        return alike.Count == 1 ? Path.GetFileName(alike[0]) : name;
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

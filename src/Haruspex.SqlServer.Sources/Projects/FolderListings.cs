namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The entries of each folder a read looks in, each folder listed once: what
/// <see cref="DiskPath.Locate(string, string, FolderListings, out string?)"/> reads a path's names from, to find how each is
/// written on disk: on a file system that ignores case, for every path, and on one that tells case apart, for a path that isn't
/// there as written (a project written on Windows often names a folder in another case than the disk's).
/// </summary>
/// <remarks>
/// <para>
/// A project's scripts share their folders, and every one of them shares the folders above the project, and a deployment script
/// may include thousands of files. Listed afresh for each script, a folder of a few thousand scripts was listed a few thousand
/// times, and the folders above it as often, and each project is read twice, as Debug and as Release: on macOS a project of 3,301
/// scripts in one folder took 25 s of system time to read, against 0.3 s for the same folder read as a folder; on Linux, one that
/// named their folder in another case, 4.9 s against 0.2 s. One read keeps one of these, which the Debug and Release reads of every
/// project, and each deployment script's includes, share, so that each folder is listed once.
/// </para>
/// <para>
/// A folder is listed once and believed for the rest of the read: a file added or renamed while a project is read is seen as the
/// folder was when first listed, as a build that had already expanded its items would see it.
/// </para>
/// </remarks>
/// <param name="caseIgnored">
/// Whether the file systems read ignore case, so that a path in any case that exists says nothing about how its names are written
/// on disk. A test gives true to walk a path name by name on a file system that tells case apart.
/// </param>
internal sealed class FolderListings(bool caseIgnored)
{
    /// <summary>Every entry of a folder, hidden ones too.</summary>
    private static readonly EnumerationOptions Every = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    /// <summary>Each folder listed, by its full path as found on disk: its entries' full paths by name, ignoring case.</summary>
    private readonly Dictionary<string, ILookup<string, string>> listed = new(StringComparer.Ordinal);

    /// <summary>Listings for a read on this machine, whose file systems ignore case where the platform's usually do.</summary>
    public FolderListings()
        : this(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
    {
    }

    /// <summary>
    /// Whether the file systems read usually ignore case (NTFS on Windows, and macOS's own), so that a path in any case that exists
    /// says nothing about how its names are written on disk.
    /// </summary>
    public bool CaseIgnored { get; } = caseIgnored;

    /// <summary>How many folders have been listed: each once, however often it is looked in.</summary>
    public int Listed => listed.Count;

    /// <summary>
    /// The entries of <paramref name="folder"/> whose name is <paramref name="name"/> ignoring case, as full paths written as on
    /// disk; none when the folder isn't there.
    /// </summary>
    public IReadOnlyList<string> Alike(string folder, string name)
    {
        if (!listed.TryGetValue(folder, out var entries))
        {
            entries = (Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder, "*", Every) : [])
                .ToLookup(entry => Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase);
            listed[folder] = entries;
        }

        return [.. entries[name]];
    }
}

using Haruspex.Collections;

namespace Haruspex.Cli;

/// <summary>
/// The files of an output folder that the last run of <c>generate</c> wrote and this one doesn't: what its list
/// (<see cref="OutputList"/>) names, less what this run writes, kept to paths that can only be files the run wrote. Nothing here
/// looks at the folder's other files, so a file the list doesn't name is never a candidate, whatever its name
/// (docs/decisions/0091-generate-removes-only-the-files-its-list-names.md). <c>generate</c> removes them; <c>check</c> reports them.
/// </summary>
internal static class StaleFiles
{
    /// <summary>What the list names that this run doesn't write, sorted out. A class, as each value handed on is: nothing compares two.</summary>
    internal sealed class Found(EquatableArray<string> stale, EquatableArray<Refusal> refused)
    {
        /// <summary>Each entry that is a file inside the folder, reached without a link: the ones to remove.</summary>
        public EquatableArray<string> Stale { get; } = stale;

        /// <summary>Each entry that could name a file outside the folder, with why, never removed.</summary>
        public EquatableArray<Refusal> Refused { get; } = refused;
    }

    /// <summary>An entry of the list that is never removed, and why.</summary>
    /// <param name="Entry">The entry, as the list has it.</param>
    /// <param name="Why">Why, as a clause after the entry: <c>climbs out of the output folder</c>.</param>
    internal sealed record Refusal(string Entry, string Why);

    /// <summary>
    /// Sorts out what <paramref name="listed"/>, the last run's list, names that <paramref name="written"/>, this run's files by
    /// their paths in <paramref name="folder"/>, doesn't. An entry this run writes again is not stale, however its case differs
    /// where the file system ignores case. An entry that is gone, or is a folder, is passed over without a word: it is no file of
    /// the last run's to remove.
    /// </summary>
    public static Found Of(string folder, IEnumerable<string> listed, IEnumerable<string> written)
    {
        var ignoresCase = IgnoresCase(folder);
        var now = written.Select(w => Key(w, ignoresCase)).ToHashSet(StringComparer.Ordinal);
        var stale = new List<string>();
        var refused = new List<Refusal>();
        foreach (var entry in listed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            // The list itself is written anew, never removed, however an edit spells its name.
            if (now.Contains(Key(entry, ignoresCase)) || Key(entry, ignoresCase) == Key(OutputList.FileName, ignoresCase))
                continue;
            if (Unsafe(entry) is { } why)
            {
                refused.Add(new Refusal(entry, why));
                continue;
            }

            switch (Reached(folder, entry))
            {
                case Reach.ThroughLink:
                    refused.Add(new Refusal(entry, "is reached through a symbolic link, which generate never writes and which may lead out of the output folder"));
                    break;
                case Reach.File:
                    stale.Add(entry);
                    break;
                case Reach.Nothing:
                    break;
            }
        }

        return new Found([.. stale], [.. refused]);
    }

    /// <summary>Where an entry is in the folder, on every platform: its names joined by the platform's separator.</summary>
    public static string PathOf(string folder, string entry) => Path.Join([folder, .. entry.Split('/')]);

    /// <summary>
    /// Why an entry can't be a path generate wrote, which the list's own form can say without touching the disk: absolute, or
    /// climbing out, or with a character no name generate writes has. Null for one that can. generate writes names alone, so anything more is an
    /// edit; accepting only plain relative names makes the folder the only place an entry can reach, links aside.
    /// </summary>
    private static string? Unsafe(string entry)
    {
        if (entry.Length == 0)
            return "is empty";
        if (entry.StartsWith('/') || Path.IsPathRooted(entry))
            return "is an absolute path";
        var names = entry.Split('/');
        if (names.Contains(".."))
            return "climbs out of the output folder";

        // generate writes names of ASCII letters, digits, '.', '-' and '_' alone (a module's name is held to them, decision 0074), so
        // any other character is an edit. Holding entries to them also makes the comparison with this run's files exact: a file
        // system that ignores case folds ASCII as ToUpperInvariant does, while other letters (the Kelvin sign, which Windows' and
        // macOS's file systems take for K) or an accent composed another way could name a file this run wrote under a name that
        // compares as another. A backslash, a colon and a name of '.' or nothing are refused by the same rule.
        if (names.Any(n => n.Length == 0 || n == "." || !n.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            return "isn't a path generate writes, names of ASCII letters, digits, '.', '-' and '_' joined by /";
        return null;
    }

    private enum Reach
    {
        /// <summary>Nothing there, or a folder: no file to remove.</summary>
        Nothing,

        /// <summary>A file, every name on the way to it a real folder.</summary>
        File,

        /// <summary>A symbolic link, or a junction, on the way or at the end.</summary>
        ThroughLink,
    }

    /// <summary>
    /// What is at <paramref name="entry"/> in <paramref name="folder"/>, name by name, so that a link on the way is found before it is
    /// followed. A link is refused wherever it points: generate never writes one, and one that points inside today may point out
    /// tomorrow. The folder itself may be reached through links (on macOS the temporary folder is), which are the user's choice.
    /// </summary>
    private static Reach Reached(string folder, string entry)
    {
        var path = folder;
        var names = entry.Split('/');
        for (var i = 0; i < names.Length; i++)
        {
            path = Path.Join(path, names[i]);
            FileSystemInfo info = new FileInfo(path);
            if (!info.Exists)
                info = new DirectoryInfo(path);
            if (info.LinkTarget is not null)
                return Reach.ThroughLink;
            if (!info.Exists)
                return Reach.Nothing;
            var isFolder = info is DirectoryInfo;
            var last = i == names.Length - 1;

            // A folder at the end, or a file on the way, isn't a file of the last run's.
            if (isFolder == last)
                return Reach.Nothing;
        }

        return Reach.File;
    }

    /// <summary>
    /// Whether the folder's file system takes two names differing only in case as one, as macOS's and Windows' do by default. The
    /// IR is there, written by this run or to be checked, so it is looked for under another case. A file system that ignores case
    /// may keep a renamed file's old case, so removing the old name would remove the file this run wrote.
    /// </summary>
    private static bool IgnoresCase(string folder) =>
        File.Exists(Path.Join(folder, HaruspexCli.IrFileName.ToUpperInvariant()));

    /// <summary>
    /// An entry as the file system tells it apart: in upper case where case is ignored. Every name generate writes is ASCII (a
    /// module's name is, docs/decisions/0074-a-config-may-split-its-output-into-modules.md), and an entry with any other character
    /// is refused before it could be removed (<see cref="Unsafe"/>), so how a file system folds other letters, or composes an
    /// accent, never decides whether a file it wrote is written again.
    /// </summary>
    private static string Key(string entry, bool ignoresCase) => ignoresCase ? entry.ToUpperInvariant() : entry;
}

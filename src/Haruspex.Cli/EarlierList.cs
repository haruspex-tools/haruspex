using Haruspex.Collections;

namespace Haruspex.Cli;

/// <summary>The list of files (<see cref="OutputList"/>) an output folder held before a run: absent, unreadable, or read.</summary>
/// <remarks>A closed hierarchy, as <see cref="EarlierIr"/> is.</remarks>
internal abstract record EarlierList
{
    private EarlierList()
    {
    }

    /// <summary>No list: a first run, a folder an earlier version of haruspex wrote, or one whose list was removed.</summary>
    internal sealed record Absent : EarlierList;

    /// <summary>A file that can't be read, or isn't a list of files.</summary>
    /// <param name="Problem">What the reader or the file system said, as a sentence.</param>
    internal sealed record Unreadable(string Problem) : EarlierList;

    /// <summary>The list, read.</summary>
    /// <param name="Entries">Each path it names, as written.</param>
    /// <param name="Text">The file's text.</param>
    internal sealed record Readable(EquatableArray<string> Entries, string Text) : EarlierList;
}

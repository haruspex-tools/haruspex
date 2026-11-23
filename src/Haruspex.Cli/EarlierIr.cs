using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>The IR an output folder held before a run: absent, of another version, unreadable, or read.</summary>
/// <remarks>
/// A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md), though in this assembly the analyser
/// doesn't check a switch over it covers each case: it takes as haruspex's own only the libraries' assemblies, each named for its
/// namespace, and not the command's, named for the command (ClosedHierarchies.IsClosed).
/// </remarks>
internal abstract record EarlierIr
{
    private EarlierIr()
    {
    }

    /// <summary>No IR: a first run, or a folder nothing was committed to.</summary>
    internal sealed record Absent : EarlierIr;

    /// <summary>An IR of another version, which can't be compared claim by claim (docs/decisions/0024-an-ir-reader-reads-only-versions-whose-documents-mean-what-its-own-do.md).</summary>
    /// <param name="Version">Its version.</param>
    internal sealed record OtherVersion(string Version) : EarlierIr;

    /// <summary>A file that can't be read, or can't be read as IR.</summary>
    /// <param name="Problem">What the reader or the file system said.</param>
    /// <param name="AsIr">Whether the file was read but isn't IR, rather than not read at all.</param>
    internal sealed record Unreadable(string Problem, bool AsIr) : EarlierIr;

    /// <summary>The IR, read.</summary>
    /// <param name="Document">The document.</param>
    internal sealed record Readable(IrDocument Document) : EarlierIr;
}

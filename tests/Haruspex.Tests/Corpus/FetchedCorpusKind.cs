namespace Haruspex.Tests.Corpus;

/// <summary>What a fetched corpus's figures measure.</summary>
internal enum FetchedCorpusKind
{
    /// <summary>How much of an application's database haruspex types.</summary>
    Coverage,

    /// <summary>
    /// A tool for administering a server or testing code in it, not an application's database: heavy on dynamic SQL and system
    /// views, so measured by what haruspex reads and whether what it can't type is unknown for a reason that fits.
    /// </summary>
    Tool,
}

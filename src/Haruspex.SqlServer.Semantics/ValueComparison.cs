namespace Haruspex.SqlServer.Semantics;

/// <summary>How a value compares with the items of its set.</summary>
public enum ValueComparison
{
    /// <summary>Numerically: every value is one of the numbers.</summary>
    Exact,

    /// <summary>
    /// Under the column's collation, with trailing spaces ignored, as SQL Server compares strings: every value
    /// equals one of the strings, but may differ from it in case, accents or trailing spaces.
    /// </summary>
    Collation,

    /// <summary>
    /// Exactly, but for trailing spaces, which SQL Server ignores in every comparison: under a binary collation
    /// (<c>_BIN</c>, <c>_BIN2</c>), every value is one of the strings followed by any number of spaces.
    /// </summary>
    Padded,
}

namespace Haruspex.Ir;

/// <summary>How a value relates to the items of its set.</summary>
public enum IrValueMatch
{
    /// <summary>Every value is one of the items, exactly.</summary>
    Exact,

    /// <summary>
    /// Every value equals one of the items under the database's string comparison, which may ignore case, accents
    /// and trailing spaces: <c>'Open '</c> can pass a check for <c>'open'</c>. True of the database, but not an
    /// exact set, so an emitter must not treat it as one.
    /// </summary>
    Collation,

    /// <summary>
    /// Every value is one of the items followed by any number of spaces (U+0020): exact but for the trailing spaces
    /// SQL Server ignores in comparisons, as under a binary collation.
    /// </summary>
    Padded,
}

namespace Haruspex.Ir;

/// <summary>The encoding of a length counted in bytes.</summary>
public enum IrByteEncoding
{
    /// <summary>Not known, or not applicable (a length in UTF-16 code units).</summary>
    Unknown,

    /// <summary>One byte per character, each a single UTF-16 code unit.</summary>
    SingleByte,

    /// <summary>UTF-8: one to four bytes per character.</summary>
    Utf8,

    /// <summary>A double-byte character set: one or two bytes per character.</summary>
    DoubleByte,
}

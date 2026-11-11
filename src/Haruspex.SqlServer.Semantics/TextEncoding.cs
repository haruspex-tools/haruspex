namespace Haruspex.SqlServer.Semantics;

/// <summary>The encoding of <c>char</c> and <c>varchar</c> data, from the collation.</summary>
public enum TextEncoding
{
    /// <summary>Not known: no collation is declared, and a folder of scripts does not record the database's default.</summary>
    Unknown,

    /// <summary>A single-byte code page: each character one byte, and one UTF-16 code unit.</summary>
    SingleByte,

    /// <summary>UTF-8 (a <c>_UTF8</c> collation): one to four bytes per character.</summary>
    Utf8,

    /// <summary>A double-byte code page (932, 936, 949, 950): one or two bytes per character.</summary>
    DoubleByte,
}

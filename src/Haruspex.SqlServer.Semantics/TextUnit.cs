namespace Haruspex.SqlServer.Semantics;

/// <summary>What a string length counts.</summary>
public enum TextUnit
{
    /// <summary>UTF-16 code units: <c>nchar(n)</c> and <c>nvarchar(n)</c>.</summary>
    Utf16,

    /// <summary>Bytes of the collation's encoding: <c>char(n)</c> and <c>varchar(n)</c>.</summary>
    Bytes,
}

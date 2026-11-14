namespace Haruspex.SqlServer.Semantics.Collations;

/// <summary>How a column holds its strings, which decides how its collation compares them.</summary>
public enum StringKind
{
    /// <summary><c>nchar</c> and <c>nvarchar</c>: UTF-16, compared by the collation's Unicode rules.</summary>
    Unicode,

    /// <summary>
    /// <c>char</c> and <c>varchar</c> in the collation's code page: only the code page's characters can be stored, and a
    /// <c>SQL_</c> collation compares them by its own sort order, not by the Unicode rules.
    /// </summary>
    CodePage,

    /// <summary><c>char</c> and <c>varchar</c> under a <c>_UTF8</c> collation: any character, stored as UTF-8.</summary>
    Utf8,
}

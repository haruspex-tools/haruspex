namespace Haruspex.Emit.JsonSchema.Model;

/// <summary>
/// A fact behind many notes, stated once in the document's description rather than on every schema it explains: each note that
/// rests on one names it, and the description lists those its notes use.
/// </summary>
internal enum JsonSchemaCaveat
{
    /// <summary><c>varchar(n)</c> and <c>char(n)</c> count bytes, so <c>n</c> bounds the length exactly only in a single-byte collation.</summary>
    ByteLengths,

    /// <summary>JSON Schema counts a string's length in code points, and SQL Server in UTF-16 code units.</summary>
    CodePoints,

    /// <summary>A value set a CHECK constraint states holds as the database compares strings, so it is noted rather than an enum.</summary>
    CollationValueSets,

    /// <summary><c>contentSchema</c> is an annotation: what JSON text parses to is checked only when a validator is asked to.</summary>
    ContentSchemas,

    /// <summary>A decimal's scale is not <c>multipleOf</c>, which a validator reading doubles tests inexactly.</summary>
    DecimalSteps,

    /// <summary>Many validators read a JSON number as a double, which holds only so many digits.</summary>
    Doubles,
}

namespace Haruspex.Emit.Zod.Model;

/// <summary>
/// A fact behind many notes, stated once in the module's header rather than on every property it explains: each note that
/// rests on one names it, and the header lists those its module's notes use.
/// </summary>
internal enum ZodCaveat
{
    /// <summary><c>varchar(n)</c> and <c>char(n)</c> count bytes, so <c>n</c> bounds the length exactly only in a single-byte collation.</summary>
    ByteLengths,

    /// <summary>A value set a CHECK constraint states holds as the database compares strings, so it is noted rather than an enum.</summary>
    CollationValueSets,

    /// <summary>The mssql driver reads a wide decimal or money as a double, which may round it past its bounds.</summary>
    DriverDecimals,

    /// <summary>JavaScript reads every number, in JSON or in source, as a double, which holds only so many digits.</summary>
    Doubles,
}

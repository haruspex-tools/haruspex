namespace Haruspex.Ir;

/// <summary>
/// Logical scalar kinds. Dates, times and identifiers are kinds of their own, not strings, because how they
/// arrive differs by encoding (a JSON string in a known format, or a driver's native value) and an emitter
/// can only validate the format if it knows which it is.
/// </summary>
public enum IrScalarKind
{
    /// <summary>Text, such as SQL Server's character types give.</summary>
    String,

    /// <summary>A number; <see cref="IrScalar.Format"/> says which (an integer of some width, a decimal, a float).</summary>
    Number,

    /// <summary>True or false: SQL Server's <c>bit</c>.</summary>
    Boolean,

    /// <summary>A calendar date, with no time and no time zone.</summary>
    Date,

    /// <summary>A time of day, with no date and no time zone.</summary>
    Time,

    /// <summary>A date and time; <see cref="IrFormat.Local"/> or <see cref="IrFormat.Offset"/> says whether it has an offset.</summary>
    Datetime,

    /// <summary>A 128-bit identifier written as 8-4-4-4-12 hex digits; not necessarily an RFC 9562 UUID.</summary>
    Uuid,

    /// <summary>
    /// A string of bytes, such as SQL Server's <c>binary</c>, <c>varbinary</c> and <c>rowversion</c> give (0.27): a driver's buffer
    /// of bytes in a rowset, base64 text in JSON. Its length, when known, counts bytes. Why:
    /// docs/decisions/0081-binary-values-are-a-kind-of-their-own-counted-in-bytes.md.
    /// </summary>
    Bytes,
}

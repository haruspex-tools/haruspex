namespace Haruspex.Ir;

/// <summary>What a length counts.</summary>
public enum IrLengthUnit
{
    /// <summary>UTF-16 code units, as JavaScript's <c>length</c> counts them.</summary>
    Utf16,

    /// <summary>Bytes of the stored encoding (see <see cref="IrLength.Encoding"/>).</summary>
    Bytes,

    /// <summary>
    /// Unicode code points (0.28), as JSON Schema's <c>maxLength</c> counts a string's length: a character outside the Basic
    /// Multilingual Plane is one, though it takes two UTF-16 code units, and so is a lone surrogate. Only a declaration gives it.
    /// </summary>
    CodePoints,
}

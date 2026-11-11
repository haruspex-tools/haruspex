namespace Haruspex.Ir;

/// <summary>A string's length.</summary>
/// <param name="Value">The maximum, or the fixed length.</param>
/// <param name="IsFixed">Whether every value has exactly this length (padded), rather than at most it.</param>
/// <param name="Unit">What <paramref name="Value"/> counts.</param>
/// <param name="Encoding">
/// For <see cref="IrLengthUnit.Bytes"/>, the encoding the bytes are in, when it is known. In a single-byte encoding
/// a byte is a UTF-16 code unit, so the length is exact; in UTF-8 or a double-byte encoding a character can take
/// several bytes, so a maximum is only an upper bound on the UTF-16 length, and a fixed length is not one at all.
/// </param>
/// <param name="From">Where it came from.</param>
public sealed record IrLength(int Value, bool IsFixed, IrLengthUnit Unit, IrByteEncoding Encoding, IrFactSource From);

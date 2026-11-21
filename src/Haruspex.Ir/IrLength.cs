namespace Haruspex.Ir;

/// <summary>A string's length.</summary>
/// <param name="Value">The maximum, or the fixed length.</param>
/// <param name="IsFixed">
/// Whether every value has exactly this length (padded), rather than at most it; for UTF-16 code units, every value stored, since
/// one converted under a supplementary-character collation may be one short (<see cref="FewestUtf16Units"/>).
/// </param>
/// <param name="Unit">What <paramref name="Value"/> counts.</param>
/// <param name="Encoding">
/// For <see cref="IrLengthUnit.Bytes"/>, the encoding the bytes are in, when it is known. In a single-byte encoding
/// a byte is a UTF-16 code unit, so the length is exact; in UTF-8 or a double-byte encoding a character can take
/// several bytes, so a maximum is only an upper bound on the UTF-16 length, and a fixed length is not one at all.
/// </param>
/// <param name="From">Where it came from.</param>
/// <remarks>
/// The length is the server's, in the server's unit. A target that counts another unit reads it through
/// <see cref="FewestCodePoints"/> or <see cref="FewestUtf16Units"/>: a maximum of any of these units is a maximum of code points and
/// of UTF-16 code units too, since a code point takes at least one of each, but a fixed length is a minimum only of the fewest a
/// value of it can hold. The differential tests observe each unit
/// (<c>A_string_s_length_is_counted_by_the_server_in_code_units_or_bytes_and_by_zod_in_code_points</c>).
/// </remarks>
public sealed record IrLength(int Value, bool IsFixed, IrLengthUnit Unit, IrByteEncoding Encoding, IrFactSource From)
{
    /// <summary>
    /// The fewest code points a value of a fixed length holds, which a target counting code points (JSON Schema's <c>minLength</c>,
    /// Zod's <c>.min</c>) may claim; 0 for a maximum. A code point is at most four bytes of UTF-8 or two of a double-byte code page,
    /// and a value cut short to a fixed length in bytes is cut between characters and padded, so it is the length divided by that,
    /// rounded up. A fixed length of UTF-16 code units holds at least <see cref="FewestUtf16Units"/> of them, of which a code point
    /// takes at most two, so <c>n / 2</c> rounded down: an <c>nchar(4)</c> holding two emoji is two code points, and an
    /// <c>nchar(1)</c> may be empty.
    /// </summary>
    public int FewestCodePoints => !IsFixed ? 0
        : Unit == IrLengthUnit.Utf16 ? Ceiling(FewestUtf16Units, 2)
        : Ceiling(Value, MostPerCodePoint);

    /// <summary>
    /// The fewest UTF-16 code units a value of a fixed length holds, which <see cref="IrMinLength"/> counts; 0 for a maximum. A
    /// fixed length of UTF-16 code units is one fewer than itself: a value converted to <c>nchar(n)</c> under a
    /// supplementary-character (<c>_SC</c>) collation is cut before a pair that doesn't fit, and not padded, though a stored one is
    /// always <c>n</c>. A code unit is at most three bytes of UTF-8 (a character of the Basic Multilingual Plane; one outside it is
    /// four bytes and two units) or two of a double-byte code page, and <c>n</c> code points are at least <c>n</c> code units.
    /// </summary>
    public int FewestUtf16Units => !IsFixed ? 0
        : Unit == IrLengthUnit.Utf16 ? Math.Max(0, Value - 1)
        : Ceiling(Value, MostPerUtf16Unit);

    private int MostPerCodePoint => (Unit, Encoding) switch
    {
        (IrLengthUnit.Utf16, _) => 2,
        (IrLengthUnit.CodePoints, _) => 1,
        (IrLengthUnit.Bytes, IrByteEncoding.SingleByte) => 1,
        (IrLengthUnit.Bytes, IrByteEncoding.DoubleByte) => 2,
        (IrLengthUnit.Bytes, _) => 4,
    };

    private int MostPerUtf16Unit => (Unit, Encoding) switch
    {
        (IrLengthUnit.Utf16 or IrLengthUnit.CodePoints, _) => 1,
        (IrLengthUnit.Bytes, IrByteEncoding.SingleByte) => 1,
        (IrLengthUnit.Bytes, IrByteEncoding.DoubleByte) => 2,
        (IrLengthUnit.Bytes, _) => 3,
    };

    /// <summary><paramref name="units"/> divided by <paramref name="most"/>, rounded up: the fewest items that many units can be.</summary>
    internal static int Ceiling(int units, int most) => (int)((units + (long)most - 1) / most);
}

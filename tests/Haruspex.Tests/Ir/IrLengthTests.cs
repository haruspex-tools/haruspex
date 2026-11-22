using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// The fewest code points, and the fewest UTF-16 code units, a string's length guarantees (<see cref="IrLength"/>,
/// <see cref="IrMinLength"/>): the minima an emitter that counts either may claim, observed in the differential tests'
/// <c>A_string_s_length_is_counted_by_the_server_in_code_units_or_bytes_and_by_zod_in_code_points</c>.
/// </summary>
public class IrLengthTests
{
    private static readonly IrFactSource Type = new(IrFactSourceKind.Type, "t");

    [Theory]
    // A code point is one or two UTF-16 code units (an emoji is a pair, so nchar(4) may hold two). A fixed length of code units is a
    // stored value's: one converted to nchar(n) is at most n and at least n - 1 (SqlTypes in the front end).
    [InlineData(4, IrLengthUnit.Utf16, IrByteEncoding.Unknown, 2)]
    [InlineData(5, IrLengthUnit.Utf16, IrByteEncoding.Unknown, 3)]
    [InlineData(1, IrLengthUnit.Utf16, IrByteEncoding.Unknown, 1)]
    [InlineData(5, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, 5)]
    [InlineData(5, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, 5)]
    [InlineData(5, IrLengthUnit.Bytes, IrByteEncoding.DoubleByte, 3)]
    // In UTF-8 a code point is one to four bytes, and with no collation known, no code page SQL Server has takes more.
    [InlineData(8, IrLengthUnit.Bytes, IrByteEncoding.Utf8, 2)]
    [InlineData(9, IrLengthUnit.Bytes, IrByteEncoding.Unknown, 3)]
    public void A_fixed_length_holds_at_least_its_units_divided_by_the_most_one_code_point_takes(int n, IrLengthUnit unit, IrByteEncoding encoding, int codePoints)
    {
        Assert.Equal(codePoints, new IrLength(n, IsFixed: true, unit, encoding, Type).FewestCodePoints);
        Assert.Equal(0, new IrLength(n, IsFixed: false, unit, encoding, Type).FewestCodePoints);
    }

    [Theory]
    [InlineData(4, IrLengthUnit.Utf16, IrByteEncoding.Unknown, 4)]
    // n code points are at least n code units.
    [InlineData(5, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, 5)]
    [InlineData(5, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, 5)]
    [InlineData(5, IrLengthUnit.Bytes, IrByteEncoding.DoubleByte, 3)]
    // A code unit of the Basic Multilingual Plane is at most three bytes of UTF-8, and a pair (two units) four.
    [InlineData(8, IrLengthUnit.Bytes, IrByteEncoding.Utf8, 3)]
    [InlineData(9, IrLengthUnit.Bytes, IrByteEncoding.Unknown, 3)]
    public void A_fixed_length_holds_at_least_its_bytes_divided_by_the_most_one_code_unit_takes(int n, IrLengthUnit unit, IrByteEncoding encoding, int units)
    {
        Assert.Equal(units, new IrLength(n, IsFixed: true, unit, encoding, Type).FewestUtf16Units);
        Assert.Equal(0, new IrLength(n, IsFixed: false, unit, encoding, Type).FewestUtf16Units);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    public void A_minimum_in_utf_16_code_units_is_half_as_many_code_points_rounded_up(int units, int codePoints) =>
        Assert.Equal(codePoints, new IrMinLength(units, Type).FewestCodePoints);
}

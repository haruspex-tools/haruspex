using System.Globalization;
using System.Numerics;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// An exact decimal number of any size, as SQL Server's exact numeric types hold them: an integer
/// <see cref="Unscaled"/> with <see cref="Scale"/> digits after the point. Never a double, because the bounds of
/// <c>decimal(38, s)</c> and <c>money</c> are not doubles, and a bound rounded the wrong way would claim too much.
/// </summary>
/// <remarks>Kept normalised (no trailing zeros after the point), so equal numbers are equal values.</remarks>
public readonly record struct ExactDecimal : IComparable<ExactDecimal>
{
    private ExactDecimal(BigInteger unscaled, int scale)
    {
        while (scale > 0 && unscaled % 10 == 0)
        {
            unscaled /= 10;
            scale--;
        }

        Unscaled = unscaled;
        Scale = scale;
    }

    /// <summary>The digits as an integer: <c>12345</c> for <c>123.45</c>.</summary>
    public BigInteger Unscaled { get; }

    /// <summary>The number of digits after the point, with trailing zeros removed: <c>2</c> for <c>123.450</c>.</summary>
    public int Scale { get; }

    /// <summary>The number of digits before the point, not counting leading zeros: <c>3</c> for <c>123.45</c>, <c>0</c> for <c>0.5</c>.</summary>
    public int IntegerDigits
    {
        get
        {
            var integer = BigInteger.Abs(Unscaled) / BigInteger.Pow(10, Scale);
            return integer.IsZero ? 0 : integer.ToString(CultureInfo.InvariantCulture).Length;
        }
    }

    /// <summary>Whether it has no fractional part.</summary>
    public bool IsInteger => Scale == 0;

    /// <summary>The number <c>unscaled × 10^-scale</c>.</summary>
    public static ExactDecimal Of(BigInteger unscaled, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        return new ExactDecimal(unscaled, scale);
    }

    /// <summary>The largest magnitude <c>decimal(precision, scale)</c> holds: <c>999.99</c> for (5, 2).</summary>
    public static ExactDecimal Largest(int precision, int scale) => Of(BigInteger.Pow(10, precision) - 1, scale);

    /// <summary>Reads plain decimal text: an optional sign, digits, and an optional point with more digits. No exponent.</summary>
    public static bool TryParse(string? text, out ExactDecimal value)
    {
        value = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var negative = text[0] == '-';
        var body = text[0] is '-' or '+' ? text[1..] : text;
        var point = body.IndexOf('.', StringComparison.Ordinal);
        var integer = point < 0 ? body : body[..point];
        var fraction = point < 0 ? "" : body[(point + 1)..];
        if (integer.Length + fraction.Length == 0 || !(integer + fraction).All(char.IsAsciiDigit))
            return false;

        var unscaled = BigInteger.Parse("0" + integer + fraction, NumberStyles.None, CultureInfo.InvariantCulture);
        value = new ExactDecimal(negative ? -unscaled : unscaled, fraction.Length);
        return true;
    }

    /// <summary>Reads plain decimal text, or throws.</summary>
    public static ExactDecimal Parse(string text) =>
        TryParse(text, out var value) ? value : throw new FormatException($"Not a plain decimal number: '{text}'.");

    /// <summary>The negated number.</summary>
    public ExactDecimal Negate() => new(-Unscaled, Scale);

    /// <inheritdoc />
    public int CompareTo(ExactDecimal other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return (Unscaled * BigInteger.Pow(10, scale - Scale)).CompareTo(other.Unscaled * BigInteger.Pow(10, scale - other.Scale));
    }

    /// <summary>Whether <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    public static bool operator <(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    public static bool operator >(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is at most <paramref name="right"/>.</summary>
    public static bool operator <=(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is at least <paramref name="right"/>.</summary>
    public static bool operator >=(ExactDecimal left, ExactDecimal right) => left.CompareTo(right) >= 0;

    /// <summary>Plain decimal text, with no exponent and no trailing zeros: <c>-99999999999999999.99</c>.</summary>
    public override string ToString()
    {
        var digits = BigInteger.Abs(Unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(Scale + 1, '0');
        var text = Scale == 0 ? digits : digits[..^Scale] + "." + digits[^Scale..];
        return Unscaled.Sign < 0 ? "-" + text : text;
    }
}

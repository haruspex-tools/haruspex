using System.Globalization;
using System.Numerics;

namespace Haruspex.Ir;

/// <summary>A bound on a number.</summary>
/// <param name="Value">The bound as exact decimal text, such as <c>-99999999999999999.99</c>; never rounded to a double.</param>
/// <param name="Inclusive">Whether the bound itself is allowed.</param>
/// <param name="From">Where it came from.</param>
public sealed record IrBound(string Value, bool Inclusive, IrFactSource From)
{
    /// <summary>
    /// The least value (for a lower bound) or the greatest (for an upper one) that the bound allows among the multiples of
    /// <c>10^-scale</c>, as exact decimal text: the bound's own value when it is inclusive, and otherwise the value one step inside
    /// it, so that <c>&lt; 100</c> on <c>decimal(5,2)</c> is <c>99.99</c>. For the values of a type of that scale, the two say the
    /// same. An emitter whose target checks numbers as doubles needs it, because a value inside an exclusive bound but close to it
    /// can round onto the bound, and an inclusive one can't be crossed by rounding.
    /// </summary>
    /// <param name="scale">The number of decimal places the values have.</param>
    /// <param name="isLower">Whether this is a lower bound.</param>
    /// <returns>The value; null when an exclusive bound has more decimal places than <paramref name="scale"/>, so no step lands on it.</returns>
    public string? InsideAt(int scale, bool isLower)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        if (Inclusive)
            return Value;
        var negative = Value.StartsWith('-');
        var body = negative ? Value[1..] : Value;
        var point = body.IndexOf('.', StringComparison.Ordinal);
        var fraction = point < 0 ? "" : body[(point + 1)..];
        if (fraction.Length > scale)
            return null;

        // In units of the step, where moving one step is adding one, and back to decimal text.
        var digits = (point < 0 ? body : body[..point]) + fraction.PadRight(scale, '0');
        var unscaled = (BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * (negative ? -1 : 1)) + (isLower ? 1 : -1);
        var magnitude = BigInteger.Abs(unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var result = scale == 0 ? magnitude : magnitude[..^scale] + "." + magnitude[^scale..];
        if (scale > 0)
            result = result.TrimEnd('0').TrimEnd('.');
        return unscaled.Sign < 0 ? "-" + result : result;
    }
}

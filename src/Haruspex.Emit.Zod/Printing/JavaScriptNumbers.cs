using System.Globalization;
using System.Numerics;

namespace Haruspex.Emit.Zod.Printing;

/// <summary>
/// A number written as a JavaScript literal. JavaScript reads every number literal as the nearest double, so a decimal the
/// IR states with more precision than a double holds (<c>decimal(19,2)</c>'s maximum, 99999999999999999.99) means the
/// double it rounds to, and writing it in full only misleads: ESLint's <c>no-loss-of-precision</c> rule reports it for that
/// reason. Such a number is written as the double JavaScript reads, in JavaScript's own shortest form
/// (<c>100000000000000000</c>); a number a double holds exactly is written as the IR states it.
/// </summary>
/// <remarks>
/// The check is the same either way: the bound in the generated code was always that double. A bound on a value that
/// <c>JSON.parse</c> reads is still sound, because rounding to the nearest double never reverses an order (the argument is in
/// <c>Facets</c>' remarks).
/// </remarks>
internal static class JavaScriptNumbers
{
    /// <summary>The literal for <paramref name="value"/>, a decimal number as the IR or the lowering writes it (<c>-12.5</c>, <c>1e-2</c>).</summary>
    public static string Literal(string value)
    {
        var exact = Rational(value);
        var nearest = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var shortest = nearest.ToString("R", CultureInfo.InvariantCulture);
        return Rational(shortest) == exact ? value : Format(shortest);
    }

    /// <summary>A decimal number as an exact fraction, unscaled digits and a power of ten, with trailing zeros removed.</summary>
    private static (BigInteger Digits, int Exponent) Rational(string text)
    {
        var e = text.IndexOfAny(['e', 'E']);
        var exponent = e < 0 ? 0 : int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? text : text[..e];
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        if (point >= 0)
        {
            exponent -= mantissa.Length - point - 1;
            mantissa = mantissa.Remove(point, 1);
        }

        var digits = BigInteger.Parse(mantissa, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        while (!digits.IsZero && digits % 10 == 0)
        {
            digits /= 10;
            exponent++;
        }

        return digits.IsZero ? (BigInteger.Zero, 0) : (digits, exponent);
    }

    /// <summary>
    /// A double's shortest round-trip digits (from .NET's <c>R</c> format) as JavaScript's <c>Number.prototype.toString</c>
    /// writes them: plain digits from 1e-7 up to 1e21, and an exponent beyond (ECMAScript, Number::toString).
    /// </summary>
    private static string Format(string shortest)
    {
        var (digits, exponent) = Rational(shortest);
        var negative = digits.Sign < 0;
        var s = BigInteger.Abs(digits).ToString(CultureInfo.InvariantCulture);
        var k = s.Length;
        var n = exponent + k;
        var text = n switch
        {
            _ when k <= n && n <= 21 => s + new string('0', n - k),
            > 0 and <= 21 => s[..n] + "." + s[n..],
            > -6 and <= 0 => "0." + new string('0', -n) + s,
            _ => (k == 1 ? s : s[..1] + "." + s[1..]) + "e" + (n - 1 >= 0 ? "+" : "-") + Math.Abs(n - 1).ToString(CultureInfo.InvariantCulture),
        };
        return negative ? "-" + text : text;
    }
}

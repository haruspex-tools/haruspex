using System.Globalization;
using System.Numerics;

namespace Haruspex.Emit.Zod.Model;

/// <summary>
/// What a number becomes in JavaScript, which reads every number, in JSON or in source, as the nearest double. The lowering
/// asks it whether a bound keeps its digits, to say so in a note; the printer, to write the number as JavaScript reads it
/// (<c>JavaScriptNumbers</c>).
/// </summary>
internal static class Doubles
{
    /// <summary>
    /// Whether the double nearest <paramref name="value"/>, written in its shortest form, is <paramref name="value"/> again:
    /// what ESLint's <c>no-loss-of-precision</c> asks of a literal. Any decimal of up to 15 significant digits does.
    /// </summary>
    public static bool KeepsItsDigits(string value) => Rational(Shortest(value)) == Rational(value);

    /// <summary>The shortest digits that read back as the double nearest <paramref name="value"/> (.NET's <c>R</c> format).</summary>
    public static string Shortest(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A decimal number as an exact fraction, unscaled digits and a power of ten, with trailing zeros removed.</summary>
    public static (BigInteger Digits, int Exponent) Rational(string text)
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
}

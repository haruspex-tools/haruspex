using System.Globalization;
using System.Numerics;
using Haruspex.Emit.Zod.Model;

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
    public static string Literal(string value) => Doubles.KeepsItsDigits(value) ? value : Format(Doubles.Shortest(value));

    /// <summary>
    /// A double's shortest round-trip digits (from .NET's <c>R</c> format) as JavaScript's <c>Number.prototype.toString</c>
    /// writes them: plain digits for a magnitude of at least 10^-6 and below 10^21, and an exponent beyond it (ECMAScript,
    /// Number::toString).
    /// </summary>
    private static string Format(string shortest)
    {
        var (digits, exponent) = Doubles.Rational(shortest);
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

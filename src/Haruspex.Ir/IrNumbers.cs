using System.Globalization;
using System.Numerics;

namespace Haruspex.Ir;

/// <summary>Numbers as the IR writes them in facets, plain decimal text, compared exactly.</summary>
internal static class IrNumbers
{
    /// <summary>Two numbers as exact decimal text, compared exactly, never through a double: below zero, zero or above.</summary>
    public static int Compare(string left, string right)
    {
        var (a, aScale) = Unscaled(left);
        var (b, bScale) = Unscaled(right);
        var scale = Math.Max(aScale, bScale);
        return (a * BigInteger.Pow(10, scale - aScale)).CompareTo(b * BigInteger.Pow(10, scale - bScale));

        static (BigInteger Value, int Scale) Unscaled(string text)
        {
            var point = text.IndexOf('.', StringComparison.Ordinal);
            var digits = point < 0 ? text : text[..point] + text[(point + 1)..];
            return (BigInteger.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), point < 0 ? 0 : text.Length - point - 1);
        }
    }
}

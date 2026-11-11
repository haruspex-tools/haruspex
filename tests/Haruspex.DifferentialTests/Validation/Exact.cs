using System.Data.SqlTypes;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>
/// An exact decimal, for checking bounds and digits without a double in the way. The validator's own, not
/// Semantics' ExactDecimal, so that it can't share that type's mistakes.
/// </summary>
internal readonly record struct Exact(BigInteger Unscaled, int Scale) : IComparable<Exact>
{
    /// <summary>Reads JSON number text or plain decimal text, with an optional exponent; null when it is neither.</summary>
    public static Exact? TryParse(string text)
    {
        var e = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (e >= 0 && !int.TryParse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            return null;
        var mantissa = e >= 0 ? text[..e] : text;
        var negative = mantissa.StartsWith('-');
        var body = negative ? mantissa[1..] : mantissa;
        var parts = body.Split('.');
        if (parts.Length > 2 || parts.Any(p => !p.All(char.IsAsciiDigit)) || parts[0].Length + (parts.Length > 1 ? parts[1].Length : 0) == 0)
            return null;

        var fraction = parts.Length > 1 ? parts[1] : "";
        var unscaled = BigInteger.Parse("0" + parts[0] + fraction, CultureInfo.InvariantCulture);
        var scale = fraction.Length - exponent;
        if (scale < 0)
        {
            unscaled *= BigInteger.Pow(10, -scale);
            scale = 0;
        }

        return Normalise(negative ? -unscaled : unscaled, scale);
    }

    public static Exact Parse(string text) => TryParse(text) ?? throw new FormatException($"Not a number: {text}");

    private static Exact Normalise(BigInteger unscaled, int scale)
    {
        while (scale > 0 && unscaled % 10 == 0)
        {
            unscaled /= 10;
            scale--;
        }

        return new Exact(unscaled, scale);
    }

    /// <summary>Digits before the point, not counting leading zeros.</summary>
    public int IntegerDigits
    {
        get
        {
            var integer = BigInteger.Abs(Unscaled) / BigInteger.Pow(10, Scale);
            return integer.IsZero ? 0 : integer.ToString(CultureInfo.InvariantCulture).Length;
        }
    }

    /// <summary>This moved by <paramref name="direction"/> steps of 10^-<paramref name="scale"/>.</summary>
    public Exact Step(int scale, int direction) =>
        Scale <= scale
            ? Normalise((Unscaled * BigInteger.Pow(10, scale - Scale)) + direction, scale)
            : throw new InvalidOperationException($"{this} has more than {scale} decimal places.");

    public int CompareTo(Exact other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return (Unscaled * BigInteger.Pow(10, scale - Scale)).CompareTo(other.Unscaled * BigInteger.Pow(10, scale - other.Scale));
    }

    public override string ToString() => Scale == 0
        ? Unscaled.ToString(CultureInfo.InvariantCulture)
        : $"{Unscaled}e-{Scale}";
}

/// <summary>A non-null scalar as the facets check it: a string's text, or a number's exact value.</summary>
internal sealed record ScalarValue(string? Text, Exact? Number)
{
    /// <summary>From JSON; null for kinds that carry no facets (dates, GUIDs, booleans).</summary>
    public static ScalarValue? FromJson(IrScalar scalar, JsonElement value) => scalar.Kind switch
    {
        IrScalarKind.String => new ScalarValue(value.GetString(), null),
        IrScalarKind.Number => Exact.TryParse(value.GetRawText()) is { } n ? new ScalarValue(null, n) : null,
        _ => null,
    };

    /// <summary>From a value SqlClient read; null for other types.</summary>
    public static ScalarValue? FromRowset(object value) => value switch
    {
        string s => new ScalarValue(s, null),
        byte or short or int or long or decimal => new ScalarValue(null, Exact.Parse(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture))),
        SqlDecimal d => new ScalarValue(null, Exact.Parse(d.ToString())),
        double d => new ScalarValue(null, Exact.Parse(d.ToString("R", CultureInfo.InvariantCulture))),
        float f => new ScalarValue(null, Exact.Parse(((double)f).ToString("R", CultureInfo.InvariantCulture))),
        _ => null,
    };

    public string Describe() => Text is not null ? JsonSerializer.Serialize(Text) : Number!.Value.ToString();
}

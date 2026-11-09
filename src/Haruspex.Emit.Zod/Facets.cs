using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod;

/// <summary>
/// Strings and numbers with every fact the IR carries about them, as Zod checks: lengths, bounds, steps and value
/// sets. "Claim everything the database guarantees": each check here is a guarantee the IR states, and each one
/// that is left out says why in a comment, because the target cannot hold it exactly.
/// </summary>
/// <remarks>
/// <para><b>Numbers are doubles.</b> <c>JSON.parse</c> rounds a decimal to the nearest double, and rounding never
/// reverses an order, so a value at most a bound parses to a double at most the bound's double: an inclusive bound
/// is always safe. An exclusive one is not, because a value just inside it can round onto it. So <c>.gt</c> and
/// <c>.lt</c> are used only for integers within 2^53, where nothing rounds; any other exclusive bound becomes the
/// inclusive one at the type's step (<c>&lt; 100</c> on <c>decimal(5,2)</c> is <c>.max(99.99)</c>), which says the
/// same of the stored values.</para>
/// <para><b>Steps.</b> <c>decimal(p, s)</c> is <c>.multipleOf(1e-s)</c>. Zod 4.6.7 tests a step by the ratio of the
/// value to the step with a relative tolerance of 4 ε, which the error of parsing both cannot exceed, so it never
/// rejects a true multiple: <c>tests/emit-check/src/multiple-of.mjs</c> checks over a million decimal strings of
/// every precision and scale, including 0.07, 1.1 and 123.45 (a one-off sweep of ten million found none either),
/// and the differential tests run every seeded row through the generated schemas.
/// Past about 2^50 steps the tolerance exceeds half a step and the check accepts anything, which is weaker, never
/// wrong.</para>
/// <para><b>Rowsets.</b> The mssql (tedious) driver builds a decimal or money value as a double from its integer
/// parts and divides by a power of ten. That is the nearest double only while the integer fits in 2^53 (precision
/// 15 or less); beyond it the double may land a little past a bound, so for wider types bounds and values are
/// left out of rowsets. A bigint arrives as a string, so its range can't be checked as a number.</para>
/// <para><b>Strings.</b> <c>nvarchar(n)</c> counts UTF-16 code units, as JavaScript does, so its length is exact.
/// <c>varchar(n)</c> counts bytes, which is the same in a single-byte collation and only an upper bound in UTF-8 or a
/// double-byte one; <c>char(n)</c> pads to <c>n</c> bytes, so <c>.length(n)</c> holds only in a single-byte
/// collation. A set of strings from a CHECK constraint matches under the database's collation (case, accents and
/// trailing spaces may differ), so it is a comment, never <c>z.enum</c>; only an exact set is. Under a binary
/// collation it is exact but for trailing spaces, which <c>.regex(/^(?:a|b)\u0020*$/)</c> says exactly (the type stays
/// <c>string</c>, since TypeScript can't name "a followed by spaces").</para>
/// </remarks>
internal static class Facets
{
    private static readonly BigInteger SafeInteger = BigInteger.Pow(2, 53);

    public static (string Code, string? Comment) String(IrScalar scalar)
    {
        var facets = scalar.Facets;
        if (facets?.Values is { Match: IrValueMatch.Exact } exact && exact.Items.All(i => i.IsString))
            return exact.Items.Count == 0 ? ("z.never()", null) : ("z.enum([" + string.Join(", ", exact.Items.Select(i => Quote(i.Text))) + "])", null);

        var code = "z.string()";
        var notes = new List<string>();
        if (facets?.Values is { Match: IrValueMatch.Collation } set)
        {
            notes.Add("one of " + string.Join(", ", set.Items.Select(i => Quote(i.Text)))
                + " as the database compares strings (case, accents and trailing spaces may differ), so not an enum");
        }

        if (facets?.Values is { Match: IrValueMatch.Padded } padded && padded.Items.All(i => i.IsString))
            code += $".regex(/^(?:{string.Join("|", padded.Items.Select(i => RegexEscape(i.Text)))})\\u0020*$/)";

        if (facets?.MinLength is { } minLength)
            code += $".min({minLength.Value})";

        if (facets?.Length is { } length)
        {
            var exactLength = length.Unit == IrLengthUnit.Utf16 || length.Encoding == IrByteEncoding.SingleByte;
            code += length.IsFixed && exactLength ? $".length({length.Value})" : $".max({length.Value})";
            if (length.Unit == IrLengthUnit.Bytes)
                notes.Add(BytesNote(scalar.Sql, length));
        }

        return (code, notes.Count == 0 ? null : string.Join("; ", notes));
    }

    private static string BytesNote(string sql, IrLength length) => (length.IsFixed, length.Encoding) switch
    {
        (false, IrByteEncoding.SingleByte) => $"{sql} counts bytes, one per character in its single-byte collation: exact",
        (false, IrByteEncoding.Utf8) => $"{sql} counts bytes, and UTF-8 takes up to four per character: an upper bound",
        (false, IrByteEncoding.DoubleByte) => $"{sql} counts bytes, and its double-byte collation takes up to two per character: an upper bound",
        (false, _) => $"{sql} counts bytes: exact in a single-byte collation, an upper bound otherwise; no collation is declared",
        (true, IrByteEncoding.SingleByte) => $"{sql} pads to {length.Value} bytes, one per character in its single-byte collation",
        (true, IrByteEncoding.Utf8) => $"{sql} pads to {length.Value} bytes, and UTF-8 takes up to four per character, so the length is at most {length.Value}",
        (true, IrByteEncoding.DoubleByte) => $"{sql} pads to {length.Value} bytes, and its double-byte collation takes up to two per character, so the length is at most {length.Value}",
        (true, _) => $"{sql} pads to {length.Value} bytes: {length.Value} characters in a single-byte collation, at most {length.Value} otherwise; no collation is declared",
    };

    public static (string Code, string? Comment) Number(IrScalar scalar, bool json)
    {
        var facets = scalar.Facets;
        switch (scalar.Format)
        {
            case IrFormats.Uint8 or IrFormats.Int16 or IrFormats.Int32:
            {
                if (ExactNumbers(facets) is { } values)
                    return (values, null);
                var (min, max) = FormatRange(scalar.Format);
                var lower = facets?.Min ?? new IrBound(min.ToString(CultureInfo.InvariantCulture), true, new IrFactSource(IrFactSources.Type, scalar.Sql));
                var upper = facets?.Max ?? new IrBound(max.ToString(CultureInfo.InvariantCulture), true, new IrFactSource(IrFactSources.Type, scalar.Sql));
                if (lower is { Inclusive: true, Value: "-2147483648" } && upper is { Inclusive: true, Value: "2147483647" })
                    return ("z.int32()", null);
                return ("z.int()" + Lower(lower, 0) + Upper(upper, 0), null);
            }

            case IrFormats.Int64 when !json:
                // The driver delivers a bigint as a string: its values are strings too, and a range isn't checkable.
                if (facets?.Values is { Match: IrValueMatch.Exact } strings && strings.Items.All(i => !i.IsString))
                    return (strings.Items.Count == 0 ? "z.never()" : "z.enum([" + string.Join(", ", strings.Items.Select(i => Quote(i.Text))) + "])", null);
                return ("z.string()", FromChecks(facets) ? "a bigint arrives as a string, so its CHECK range is not checked" : null);

            case IrFormats.Int64:
            {
                const string precision = "int64: exact only up to 2^53 once parsed";
                if (ExactNumbers(facets) is { } values)
                    return (values, precision);

                // The type's own range is left out (keeping the bare number this mapping always had); a CHECK's is kept.
                var code = "z.number()"
                    + (facets?.Min is { From.Kind: IrFactSources.Check } lower ? Lower(lower, 0, exclusiveAllowed: false) : "")
                    + (facets?.Max is { From.Kind: IrFactSources.Check } upper ? Upper(upper, 0, exclusiveAllowed: false) : "");
                return (code, precision);
            }

            case IrFormats.Decimal:
            {
                var digits = facets?.Digits;
                var exactInDriver = json || digits is { Precision: <= 15 };
                if (exactInDriver && ExactNumbers(facets) is { } values)
                    return (values, null);

                var integers = digits is { Scale: 0, Precision: <= 15 };
                var code = integers ? "z.int()" : "z.number()";
                if (exactInDriver)
                    code += Lower(facets?.Min, digits?.Scale, exclusiveAllowed: false) + Upper(facets?.Max, digits?.Scale, exclusiveAllowed: false);
                if (digits is not null && !integers)
                    code += digits.Scale == 0 ? ".multipleOf(1)" : $".multipleOf(1e-{digits.Scale.ToString(CultureInfo.InvariantCulture)})";
                var comment = exactInDriver || (facets?.Min is null && facets?.Max is null && facets?.Values is null)
                    ? null
                    : $"the mssql driver may round {scalar.Sql} past its bounds, so they are not checked in a row";
                return (code, comment);
            }

            default:
                // Floating point, or a number of no stated format: inclusive bounds only, since an exclusive one can't
                // survive rounding (see the remarks).
                return ("z.number()" + Lower(facets?.Min, null, exclusiveAllowed: false) + Upper(facets?.Max, null, exclusiveAllowed: false), null);
        }
    }

    private static bool FromChecks(IrFacets? facets) =>
        facets?.Min is { From.Kind: IrFactSources.Check } || facets?.Max is { From.Kind: IrFactSources.Check };

    /// <summary><c>z.literal([…])</c> for an exact set of numbers; null when there is none.</summary>
    private static string? ExactNumbers(IrFacets? facets) =>
        facets?.Values is { Match: IrValueMatch.Exact } set && set.Items.All(i => !i.IsString)
            ? set.Items.Count == 0 ? "z.never()" : "z.literal([" + string.Join(", ", set.Items.Select(i => i.Text)) + "])"
            : null;

    private static (BigInteger Min, BigInteger Max) FormatRange(string format) => format switch
    {
        IrFormats.Uint8 => (byte.MinValue, byte.MaxValue),
        IrFormats.Int16 => (short.MinValue, short.MaxValue),
        _ => (int.MinValue, int.MaxValue),
    };

    /// <param name="scale">The type's scale, for turning an exclusive bound into an inclusive one; null when unknown.</param>
    /// <param name="exclusiveAllowed">Whether <c>.gt</c> may be used for a safe integer (see the remarks).</param>
    private static string Lower(IrBound? bound, int? scale, bool exclusiveAllowed = true) => Bound(bound, scale, exclusiveAllowed, lower: true);

    private static string Upper(IrBound? bound, int? scale, bool exclusiveAllowed = true) => Bound(bound, scale, exclusiveAllowed, lower: false);

    private static string Bound(IrBound? bound, int? scale, bool exclusiveAllowed, bool lower)
    {
        if (bound is null)
            return "";
        var inclusive = lower ? ".min" : ".max";
        if (bound.Inclusive)
            return $"{inclusive}({bound.Value})";
        if (exclusiveAllowed && scale == 0 && IsSafeInteger(bound.Value))
            return $"{(lower ? ".gt" : ".lt")}({bound.Value})";

        // The nearest value inside the bound at the type's step; without a known scale, the bound itself, inclusive,
        // which is weaker but still true.
        return scale is { } s && Step(bound.Value, s, lower ? 1 : -1) is { } inside
            ? $"{inclusive}({inside})"
            : $"{inclusive}({bound.Value})";
    }

    private static bool IsSafeInteger(string text) =>
        BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && BigInteger.Abs(n) < SafeInteger;

    /// <summary><paramref name="text"/> moved by one step of <c>10^-scale</c>, exactly; null when it has more decimal places than <paramref name="scale"/>.</summary>
    private static string? Step(string text, int scale, int direction)
    {
        var negative = text.StartsWith('-');
        var body = negative ? text[1..] : text;
        var point = body.IndexOf('.', StringComparison.Ordinal);
        var fraction = point < 0 ? "" : body[(point + 1)..];
        if (fraction.Length > scale)
            return null;
        var digits = (point < 0 ? body : body[..point]) + fraction.PadRight(scale, '0');
        var unscaled = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * (negative ? -1 : 1) + direction;

        var magnitude = BigInteger.Abs(unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var result = scale == 0 ? magnitude : magnitude[..^scale] + "." + magnitude[^scale..];
        if (scale > 0)
            result = result.TrimEnd('0').TrimEnd('.');
        return unscaled.Sign < 0 ? "-" + result : result;
    }

    /// <summary>
    /// Text that matches itself in a regular expression literal: anything but a letter, digit or underscore as a
    /// <c>\uXXXX</c> escape, which can't be a metacharacter, close the literal or break the line. Without the
    /// <c>u</c> flag, a character outside the BMP is two such escapes, which match its UTF-16 code units in turn.
    /// </summary>
    private static string RegexEscape(string text) =>
        string.Concat(text.Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c.ToString() : $"\\u{(int)c:x4}"));

    private static string Quote(string text) => "\"" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
}

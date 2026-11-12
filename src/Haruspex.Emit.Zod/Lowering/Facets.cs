using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// Strings and numbers lowered with every fact the IR carries about them, as Zod checks: lengths, bounds, steps and value
/// sets. "Claim everything the database guarantees": each check here is a guarantee the IR states, and each one
/// that is left out says why in a comment, because the target cannot hold it exactly.
/// </summary>
/// <remarks>
/// <para><b>Numbers are doubles.</b> <c>JSON.parse</c> rounds a decimal to the nearest double, and rounding never
/// reverses an order, so a value at most a bound parses to a double at most the bound's double: an inclusive bound
/// is always safe. An exclusive one is not, because a value inside it but close to it can round onto it. So <c>.gt</c> and
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

    /// <summary>A string, with its value set, pattern and length as checks, and a note for each fact left out or weakened.</summary>
    public static ZodExpression String(IrScalar scalar)
    {
        var facets = scalar.Facets;
        if (facets?.Values is { Match: IrValueMatch.Exact } exact && exact.Items.All(i => i.IsString))
            return exact.Items.Count == 0 ? Z("never") : Z("enum", new ArrayLiteral([.. exact.Items.Select(i => new Text(i.Text))]));

        var code = Z("string");
        var notes = new List<ZodNote>();
        if (facets?.Values is { Match: IrValueMatch.Collation } set)
            notes.Add(new ZodNote($"One of {string.Join(", ", set.Items.Select(i => Quoted(i.Text)))}, as the database compares strings.", ZodCaveat.CollationValueSets));

        if (facets?.Values is { Match: IrValueMatch.Padded } padded && padded.Items.All(i => i.IsString))
        {
            // Trimmed again, although the IR stores them trimmed: an item's own trailing spaces would make the regex
            // require them, and a value without them is in the set ('a' = 'a ').
            var items = padded.Items.Select(i => i.Text.TrimEnd(' ')).Distinct(StringComparer.Ordinal);
            code = code.Then("regex", new PaddedValues([.. items]));
        }

        if (facets?.MinLength is { } minLength)
            code = code.Then("min", Count(minLength.Value));

        if (facets?.Length is { } length)
        {
            var exactLength = length.Unit == IrLengthUnit.Utf16 || length.Encoding == IrByteEncoding.SingleByte;
            code = code.Then(length.IsFixed && exactLength ? "length" : "max", Count(length.Value));
            if (length.Unit == IrLengthUnit.Bytes && !exactLength)
                notes.Add(BytesNote(scalar.Sql ?? "The string", length));
        }

        return WithNotes(code, notes);
    }

    /// <summary><paramref name="code"/> with its notes, which its reader meets in the order given.</summary>
    private static ZodExpression WithNotes(ZodExpression code, IEnumerable<ZodNote> notes) =>
        notes.Reverse().Aggregate(code, (inner, note) => new Noted(inner, note));

    /// <summary>
    /// Why a length counted in bytes is only an upper bound on the length in characters: the collation takes more than one
    /// byte a character, or isn't declared. (A single-byte collation's length is exact and needs no note.)
    /// </summary>
    private static ZodNote BytesNote(string sql, IrLength length)
    {
        var counts = length.IsFixed ? $"{sql} pads to {length.Value} bytes" : $"{sql} counts bytes";
        var why = length.Encoding switch
        {
            IrByteEncoding.Utf8 => "UTF-8 takes up to four a character",
            IrByteEncoding.DoubleByte => "its double-byte collation takes up to two a character",
            _ => "its collation isn't declared",
        };
        return new ZodNote($"{counts} and {why}, so the length is at most {length.Value}.", ZodCaveat.ByteLengths);
    }

    /// <summary>A number as it arrives in <paramref name="json"/> or a row, with its range, step and value set as checks.</summary>
    public static ZodExpression Number(IrScalar scalar, bool json)
    {
        var facets = scalar.Facets;
        switch (scalar.Format)
        {
            case IrFormat.Uint8 or IrFormat.Int16 or IrFormat.Int32:
            {
                if (ExactNumbers(facets) is { } values)
                    return values;
                var (min, max) = FormatRange(scalar.Format.Value);
                var lower = facets?.Min ?? new IrBound(min.ToString(CultureInfo.InvariantCulture), true, new IrFactSource(IrFactSourceKind.Type, scalar.Sql ?? IrWireNames.Of(scalar.Format.Value)));
                var upper = facets?.Max ?? new IrBound(max.ToString(CultureInfo.InvariantCulture), true, new IrFactSource(IrFactSourceKind.Type, scalar.Sql ?? IrWireNames.Of(scalar.Format.Value)));

                // z.int32() checks int's own range, so where a bound is only that range, z.int32() says it and the bound
                // goes; where both bounds are tighter, z.int() and the two bounds say it all. Zod has no builder for
                // smallint's or tinyint's range.
                var ownLower = lower is { Inclusive: true, Value: "-2147483648" };
                var ownUpper = upper is { Inclusive: true, Value: "2147483647" };
                if (scalar.Format == IrFormat.Int32 && (ownLower || ownUpper))
                    return Upper(Lower(Z("int32"), ownLower ? null : lower, 0), ownUpper ? null : upper, 0);

                return Upper(Lower(Z("int"), lower, 0), upper, 0);
            }

            case IrFormat.Int64 when !json:
                // The driver delivers a bigint as a string: its values are strings too, and a range isn't checkable.
                if (facets?.Values is { Match: IrValueMatch.Exact } strings && strings.Items.All(i => !i.IsString))
                    return strings.Items.Count == 0 ? Z("never") : Z("enum", new ArrayLiteral([.. strings.Items.Select(i => new Text(i.Text))]));
                return FromChecks(facets) ? new Noted(Z("string"), new ZodNote("A bigint arrives as a string, so its CHECK range is not checked.")) : Z("string");

            case IrFormat.Int64:
            {
                var precision = new ZodNote("A bigint, exact only up to 2^53 once parsed.", ZodCaveat.Doubles);
                if (ExactNumbers(facets) is { } values)
                    return new Noted(values, precision);

                // The type's own range is left out (keeping the bare number this mapping always had); a CHECK's is kept.
                var code = Z("number");
                code = Lower(code, facets?.Min is { From.Kind: IrFactSourceKind.Check } lower ? lower : null, 0, exclusiveAllowed: false);
                code = Upper(code, facets?.Max is { From.Kind: IrFactSourceKind.Check } upper ? upper : null, 0, exclusiveAllowed: false);
                return new Noted(code, precision);
            }

            case IrFormat.Decimal:
            {
                var digits = facets?.Digits;
                var exactInDriver = json || digits is { Precision: <= 15 };
                if (exactInDriver && ExactNumbers(facets) is { } values)
                    return values;

                var integers = digits is { Scale: 0, Precision: <= 15 };
                var code = integers ? Z("int") : Z("number");
                if (exactInDriver)
                {
                    code = Upper(Lower(code, facets?.Min, digits?.Scale, exclusiveAllowed: false), facets?.Max, digits?.Scale, exclusiveAllowed: false);
                    if (WideBounds(code, scalar.Sql) is { } wide)
                        code = new Noted(code, wide);
                }

                if (digits is not null && !integers)
                    code = code.Then("multipleOf", new PowerOfTen(-digits.Scale));
                return exactInDriver || (facets?.Min is null && facets?.Max is null && facets?.Values is null)
                    ? code
                    : new Noted(code, new ZodNote($"The driver may round {scalar.Sql ?? "a decimal"} past its bounds in a row, so they are not checked.", ZodCaveat.DriverDecimals));
            }

            default:
                // Floating point, or a number of no stated format: inclusive bounds only, since an exclusive one can't
                // survive rounding (see the remarks).
                return Upper(Lower(Z("number"), facets?.Min, null, exclusiveAllowed: false), facets?.Max, null, exclusiveAllowed: false);
        }
    }

    /// <summary>
    /// A note keeping the exact bounds, when a double can't hold them: JavaScript reads the bound as the nearest double (the
    /// printer writes it so), which is the check, but the reader of the module should still see what the database states.
    /// </summary>
    private static ZodNote? WideBounds(ZodExpression code, string? sql)
    {
        var bounds = new List<string>();
        for (var link = code; link is Call call; link = call.Target)
        {
            if (call is { Method: "min" or "max", Arguments: [Number bound] })
                bounds.Insert(0, bound.Value);
        }

        return bounds.All(Doubles.KeepsItsDigits)
            ? null
            : new ZodNote(
                $"{sql ?? "The decimal"} {(bounds.Count == 2 ? $"runs from {bounds[0]} to {bounds[1]}" : $"is bounded by {bounds[0]}")}, which JavaScript reads as the nearest doubles.",
                ZodCaveat.Doubles);
    }

    private static bool FromChecks(IrFacets? facets) =>
        facets?.Min is { From.Kind: IrFactSourceKind.Check } || facets?.Max is { From.Kind: IrFactSourceKind.Check };

    /// <summary><c>z.literal([…])</c> for an exact set of numbers; null when there is none.</summary>
    private static ZodExpression? ExactNumbers(IrFacets? facets) =>
        facets?.Values is { Match: IrValueMatch.Exact } set && set.Items.All(i => !i.IsString)
            ? set.Items.Count == 0 ? Z("never") : Z("literal", new ArrayLiteral([.. set.Items.Select(i => new Number(i.Text))]))
            : null;

    private static (BigInteger Min, BigInteger Max) FormatRange(IrFormat format) => format switch
    {
        IrFormat.Uint8 => (byte.MinValue, byte.MaxValue),
        IrFormat.Int16 => (short.MinValue, short.MaxValue),
        IrFormat.Int32 => (int.MinValue, int.MaxValue),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Only a format of 32 bits or fewer has a range here."),
    };

    /// <param name="code">The schema the bound checks.</param>
    /// <param name="bound">The bound; none adds no check.</param>
    /// <param name="scale">The type's scale, for turning an exclusive bound into an inclusive one; null when unknown.</param>
    /// <param name="exclusiveAllowed">Whether <c>.gt</c> may be used for a safe integer (see the remarks).</param>
    private static ZodExpression Lower(ZodExpression code, IrBound? bound, int? scale, bool exclusiveAllowed = true) => Bound(code, bound, scale, exclusiveAllowed, lower: true);

    private static ZodExpression Upper(ZodExpression code, IrBound? bound, int? scale, bool exclusiveAllowed = true) => Bound(code, bound, scale, exclusiveAllowed, lower: false);

    private static ZodExpression Bound(ZodExpression code, IrBound? bound, int? scale, bool exclusiveAllowed, bool lower)
    {
        if (bound is null)
            return code;
        var inclusive = lower ? "min" : "max";
        if (bound.Inclusive)
            return code.Then(inclusive, new Number(bound.Value));
        if (exclusiveAllowed && scale == 0 && IsSafeInteger(bound.Value))
            return code.Then(lower ? "gt" : "lt", new Number(bound.Value));

        // The nearest value inside the bound at the type's step; without a known scale, the bound itself, inclusive,
        // which is weaker but still true.
        return code.Then(inclusive, new Number(scale is { } s && Step(bound.Value, s, lower ? 1 : -1) is { } inside ? inside : bound.Value));
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
        var unscaled = (BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * (negative ? -1 : 1)) + direction;

        var magnitude = BigInteger.Abs(unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var result = scale == 0 ? magnitude : magnitude[..^scale] + "." + magnitude[^scale..];
        if (scale > 0)
            result = result.TrimEnd('0').TrimEnd('.');
        return unscaled.Sign < 0 ? "-" + result : result;
    }

    private static Number Count(int value) => new(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A value quoted inside a note, as it would be written in code, so that spaces and quotes in it stay visible.</summary>
    private static string Quoted(string text) => "\"" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
}

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
/// <para><b>Strings.</b> Zod counts a string's length in code points; <c>nvarchar(n)</c> counts UTF-16 code units, of which a
/// code point is one or two, and <c>varchar(n)</c> bytes, of which it is one in a single-byte collation and up to four in UTF-8 or
/// two in a double-byte one. So <c>n</c> is always a maximum, and a fixed length (<c>nchar(n)</c>, <c>char(n)</c>) or a minimum is
/// a minimum only of the fewest code points it can hold: <c>.length(n)</c> only in a single-byte collation (or for one code unit),
/// and otherwise <c>.min</c> of <c>n</c> divided by the most a code point takes, rounded up. A set of strings from a CHECK constraint matches under the database's collation (case, accents and
/// trailing spaces may differ), so it is a comment, never <c>z.enum</c>; only an exact set is, and a canonical one, whose values are
/// first mapped to the item they equal by the fold the IR gives (<c>z.preprocess(canonicalOf(…), z.enum([…]))</c>). Under a binary
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

        if (facets?.Values is { Match: IrValueMatch.Canonical, Fold: { } fold } canonical && canonical.Items.All(i => i.IsString))
            return new Canonical(fold, [.. canonical.Items.Select(i => i.Text)]);

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

        // Zod counts a string's length in code points (observed: ZodObservationTests), and SQL Server in UTF-16 code units or
        // bytes, of which a code point takes one or more. So the length is a maximum of code points, and a fixed length or a minimum
        // is a minimum only of the fewest code points it can hold: two emoji fill an nchar(4) (observed in the differential tests,
        // A_string_s_length_is_counted_by_the_server_in_code_units_or_bytes_and_by_zod_in_code_points). Where the fewest is the
        // length itself, the length is exact.
        var fewest = Math.Max(facets?.Length?.FewestCodePoints ?? 0, facets?.MinLength?.FewestCodePoints ?? 0);
        if (facets?.Length is { } filled && fewest == filled.Value)
        {
            code = code.Then("length", Count(filled.Value));
        }
        else
        {
            if (fewest > 0)
                code = code.Then("min", Count(fewest));
            if (facets?.Length is { } most)
                code = code.Then("max", Count(most.Value));
        }

        if (facets?.Length is { } length && LengthNote(scalar.Sql ?? "The string", length) is { } lengthNote)
            notes.Add(lengthNote);
        if (facets?.MinLength is { Value: > 1, Unit: IrLengthUnit.Utf16 } minLength)
            notes.Add(new ZodNote($"At least {minLength.Value} UTF-16 code units, which is at least {CodePoints(minLength.FewestCodePoints)}.", ZodCaveat.CodePoints));

        return WithNotes(code, notes);
    }

    /// <summary><paramref name="code"/> with its notes, which its reader meets in the order given.</summary>
    private static ZodExpression WithNotes(ZodExpression code, IEnumerable<ZodNote> notes) =>
        notes.Reverse().Aggregate(code, (inner, note) => new Noted(inner, note));

    /// <summary>
    /// Why a length isn't exact in code points, when it isn't: a fixed length in UTF-16 code units, of which a code point may take
    /// two, or one in bytes of a collation that takes more than one byte a character, or isn't declared. Null for an exact one: a
    /// maximum of code units, or any length in a single-byte collation or in code points.
    /// </summary>
    private static ZodNote? LengthNote(string sql, IrLength length)
    {
        if (length.IsFixed && length.FewestCodePoints == length.Value)
            return null;
        if (length.Unit == IrLengthUnit.Utf16)
        {
            return length.IsFixed
                ? new ZodNote($"{sql} is exactly {length.Value} UTF-16 code units, which is {length.FewestCodePoints} to {length.Value} code points.", ZodCaveat.CodePoints)
                : null;
        }

        if (length.Unit != IrLengthUnit.Bytes || length.Encoding == IrByteEncoding.SingleByte)
            return null;
        var counts = length.IsFixed ? $"{sql} pads to {length.Value} bytes" : $"{sql} counts bytes";
        var why = length.Encoding switch
        {
            IrByteEncoding.Utf8 => "UTF-8 takes up to four a character",
            IrByteEncoding.DoubleByte => "its double-byte collation takes up to two a character",
            _ => "its collation isn't declared",
        };
        var range = length.IsFixed ? $"{length.FewestCodePoints} to {length.Value} code points" : $"at most {length.Value}";
        return new ZodNote($"{counts} and {why}, so the length is {range}.", ZodCaveat.ByteLengths);
    }

    private static string CodePoints(int count) => count == 1 ? "1 code point" : $"{Count(count).Value} code points";

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
                // An exact set of numbers of no format, in JSON: a declaration's enum, a number written into JSON in the SQL's text
                // (json_query('[2024]')), or a bit converted to text. Each is an exact decimal SQL Server hands on as written, so
                // JSON.parse reads a value and the literal written here as the same decimal, so to the same double, and z.literal
                // compares them exactly; the differential tests run every fixture's output through it. Not a float's: FOR JSON
                // writes one with 16 significant digits, which may not read back as its literal's double.
                if (json && scalar.Format is null && ExactNumbers(facets) is { } exact)
                    return exact;

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
        var bounds = new List<(string Method, string Value)>();
        for (var link = code; link is Call call; link = call.Target)
        {
            if (call is { Method: "min" or "max", Arguments: [Number bound] })
                bounds.Insert(0, (call.Method, bound.Value));
        }

        if (bounds.All(b => Doubles.KeepsItsDigits(b.Value)))
            return null;
        var range = bounds switch
        {
            [(_, var low), (_, var high)] => $"runs from {low} to {high}, which JavaScript reads as the nearest doubles",
            [("min", var low)] => $"is at least {low}, which JavaScript reads as the nearest double",
            [(_, var high)] => $"is at most {high}, which JavaScript reads as the nearest double",
            _ => throw new InvalidOperationException("A decimal's bounds are at most a minimum and a maximum."),
        };
        return new ZodNote($"{sql ?? "The decimal"} {range}.", ZodCaveat.Doubles);
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
        return code.Then(inclusive, new Number(scale is { } s && bound.InsideAt(s, lower) is { } inside ? inside : bound.Value));
    }

    private static bool IsSafeInteger(string text) =>
        BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && BigInteger.Abs(n) < SafeInteger;

    /// <summary>
    /// A binary value with its length: in a rowset the Node <c>Buffer</c> the mssql driver hands over for every binary type
    /// (<c>z.instanceof(Buffer)</c>, its length checked with <c>.check(z.maxLength(n))</c>, or <c>z.length(n)</c> when fixed); in JSON
    /// the base64 text SQL Server writes, padded and on one line (<c>z.base64()</c>), as long as <c>n</c> bytes take (four characters
    /// for every three bytes or part of three). Why: docs/decisions/0082-a-binary-value-is-a-buffer-in-a-rowset-and-base64-in-json.md.
    /// </summary>
    /// <remarks>
    /// <c>Buffer</c>, not <c>Uint8Array</c>, which a <c>Buffer</c> is: the driver refuses a plain <c>Uint8Array</c> as a parameter
    /// ("Invalid buffer"), and hands over only <c>Buffer</c>s, so a schema of either direction names what the driver deals in. It
    /// is a global of Node's, as the driver is.
    /// </remarks>
    public static ZodExpression Bytes(IrScalar scalar, bool json)
    {
        var length = scalar.Facets?.Length;
        if (json)
        {
            var code = Z("base64");
            return length is null ? code : code.Then(length.IsFixed ? "length" : "max", Count(Base64Length(length.Value)));
        }

        var buffer = Z("instanceof", new Name("Buffer"));
        return length is null ? buffer : buffer.Then("check", Z(length.IsFixed ? "length" : "maxLength", Count(length.Value)));
    }

    /// <summary>
    /// A parameter's binary value, or a table-valued parameter's column's: a <c>Buffer</c> of at most its length, fixed or not, since
    /// the server pads a shorter one to a fixed length and a longer one is cut short or refused.
    /// </summary>
    public static ZodExpression SentBytes(IrScalar scalar) => Bytes(scalar with { Facets = scalar.Facets?.Length is { IsFixed: true } length ? scalar.Facets with { Length = length with { IsFixed = false } } : scalar.Facets }, json: false);

    /// <summary>The characters base64 takes for <paramref name="bytes"/> bytes, padded: four for every three bytes or part of three.</summary>
    public static int Base64Length(int bytes) => (int)Math.Min(int.MaxValue, 4L * ((bytes + 2L) / 3));

    private static Number Count(int value) => new(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A value quoted inside a note, as it would be written in code, so that spaces and quotes in it stay visible.</summary>
    private static string Quoted(string text) => "\"" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
}

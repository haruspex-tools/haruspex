using System.Numerics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Model.XmlSchemas;

namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>
/// What a node's simple type proves of the value <c>value()</c> reads from it as a SQL type: its facets, where the conversion keeps
/// them. The server converts the value's canonical text as <c>CAST</c> converts <c>nvarchar</c>, and XML's methods require
/// <c>ANSI_WARNINGS</c> (1934), so text the type can't take is an error, never a value (observed:
/// Value_converts_the_canonical_text_as_cast_converts_nvarchar). Why:
/// docs/decisions/0139-xml-is-typed-from-its-schema-collection-where-the-schema-proves-it.md.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>To a whole-number type, text that isn't a whole number fails (245), so a number's bounds and enumeration hold exactly, the
/// enumeration's whole numbers only.</item>
/// <item>To <c>decimal(p,s)</c> the text is rounded to <c>s</c> places, so a bound holds exactly only where the values have no more
/// (a whole-number type, or <c>fractionDigits</c> no more than <c>s</c>); otherwise it is widened to the inclusive bound at <c>s</c>
/// places outside it, which rounding can't cross. An enumeration holds where each of its values has no more places.</item>
/// <item>To <c>nvarchar(n)</c> a string type's lengths and enumeration hold, cut to <c>n</c> as the value is. The server counts
/// XML Schema's lengths in UTF-16 code units, as <c>nvarchar</c> does (observed: A_length_facet_counts_utf_16_code_units).</item>
/// </list>
/// Other targets, other kinds of value, and patterns, which no target here checks, carry nothing.
/// </remarks>
public static class XmlValueFacts
{
    /// <summary>The facts true of <c>value()</c> of <paramref name="node"/> as <paramref name="target"/>: the type's, narrowed by the schema's.</summary>
    /// <param name="node">The node the path reaches.</param>
    /// <param name="target">The type <c>value()</c>'s second argument names.</param>
    /// <param name="collection">The collection the instance is typed by.</param>
    public static ColumnFacts Of(XmlNode node, ResolvedType target, ObjectName collection)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(target);
        var facts = SchemaRules.TypeFacts(target, collation: null);
        if (node.SimpleType is not { Facets: var xsd, Kind: var kind } || target.Scalar is not { } type)
            return facts;

        var from = new XmlSchemaSource(collection, node.Declaration);
        var numeric = kind is XmlValueKind.WholeNumber or XmlValueKind.DecimalNumber;
        ColumnFacts? proved = null;
        if (numeric && type.IsInteger)
            proved = Numbers(xsd, from, exact: true, scale: 0, wholeOnly: true);
        else if (numeric && type.IsDecimal)
            proved = Numbers(xsd, from, exact: kind == XmlValueKind.WholeNumber || xsd.FractionDigits <= type.Scale, type.Scale, wholeOnly: false);
        else if (kind is XmlValueKind.Text or XmlValueKind.NormalisedText && type.Type == BuiltInType.NVarChar)
            proved = Strings(xsd, from, type.IsMax ? null : type.Length);

        return proved is null ? facts : SchemaRules.Narrowed(facts, proved, target);
    }

    private static ColumnFacts Numbers(XmlFacets xsd, FactSource from, bool exact, int scale, bool wholeOnly)
    {
        BoundFact? Bound(string? inclusive, string? exclusive, bool lower)
        {
            var bounds = new List<BoundFact>();
            if (ExactDecimal.TryParse(inclusive, out var i))
                bounds.Add(new BoundFact(i, true, from));
            if (ExactDecimal.TryParse(exclusive, out var e))
                bounds.Add(new BoundFact(e, false, from));
            if (xsd.TotalDigits is { } digits)
            {
                var limit = ExactDecimal.Of(BigInteger.Pow(10, digits), 0);
                bounds.Add(new BoundFact(lower ? limit.Negate() : limit, false, from));
            }

            var tightest = bounds.Aggregate((BoundFact?)null, (a, b) => Bounds.Tighter(a, b, lower ? BoundSide.Lower : BoundSide.Upper, f => f.Bound));
            if (tightest is null || exact || (tightest.Value.Scale <= scale && tightest.Inclusive))
                return tightest;

            // Rounding to the target's places is monotonic, so a value inside the bound rounds no further out than the bound
            // itself rounded outward: an inclusive bound at the places the target has.
            return new BoundFact(Outward(tightest.Value, scale, lower), true, from);
        }

        ValueSetFact? values = null;
        if (xsd.Enumeration is { } items)
        {
            var numbers = items.Select(t => (Ok: ExactDecimal.TryParse(t, out var n), Number: n)).ToList();
            if (numbers.All(n => n.Ok))
            {
                var kept = numbers.Select(n => n.Number).Where(n => !wholeOnly || Whole(n)).ToList();
                if (wholeOnly || kept.All(n => Places(n) <= scale))
                    values = new ValueSetFact([.. kept.Distinct().Order()], null, ValueComparison.Exact, [from]);
            }
        }

        return new ColumnFacts(null, Min: Bound(xsd.MinInclusive, xsd.MinExclusive, lower: true), Max: Bound(xsd.MaxInclusive, xsd.MaxExclusive, lower: false), Values: values);
    }

    private static ColumnFacts Strings(XmlFacets xsd, FactSource from, int? length)
    {
        int? Cut(int? n) => n is { } value && length is { } most ? Math.Min(value, most) : n;
        var max = xsd.MaxLength is { } m && (length is null || m < length) ? new LengthFact(m, IsFixed: false, TextUnit.Utf16, TextEncoding.Unknown, from) : null;
        var min = Cut(xsd.MinLength) is { } least and > 0 ? new MinLengthFact(least, from) : null;
        var values = xsd.Enumeration is { } items && items.All(i => length is null || i.Length <= length)
            ? new ValueSetFact(null, [.. items.Distinct()], ValueComparison.Exact, [from])
            : null;
        return new ColumnFacts(null, Length: max, MinLength: min, Values: values);
    }

    /// <summary>The number of places <paramref name="n"/> has once its trailing zeros are dropped.</summary>
    private static int Places(ExactDecimal n)
    {
        var (unscaled, scale) = (n.Unscaled, n.Scale);
        while (scale > 0 && unscaled % 10 == 0)
        {
            unscaled /= 10;
            scale--;
        }

        return scale;
    }

    private static bool Whole(ExactDecimal n) => Places(n) == 0;

    /// <summary><paramref name="n"/> rounded away from the values a bound admits, to <paramref name="scale"/> places: down for a lower bound, up for an upper one.</summary>
    private static ExactDecimal Outward(ExactDecimal n, int scale, bool lower)
    {
        if (n.Scale <= scale)
            return n;
        var divisor = BigInteger.Pow(10, n.Scale - scale);
        var quotient = BigInteger.DivRem(n.Unscaled, divisor, out var remainder);
        if (remainder != 0 && (lower ? n.Unscaled < 0 : n.Unscaled > 0))
            quotient += lower ? -1 : 1;
        return ExactDecimal.Of(quotient, scale);
    }
}

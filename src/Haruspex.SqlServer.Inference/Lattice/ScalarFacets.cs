using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>
/// The lattice of one fact: a scalar's facets (<see cref="IrFacets"/>), what is known of its non-null values beyond its type.
/// The guide's chapter <c>docs/internals/05-values-as-a-lattice.md</c>, "One lattice per fact", explains the idea.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order.</b> Facets that prove more allow fewer values, so they are lower. No facets at all (<c>null</c>) proves nothing
/// and is the top. Each facet is its own small lattice, and the facets are ordered facet by facet: a shorter maximum length
/// is below a longer one, a higher lower bound below a lower one, a smaller value set below a larger one, and a facet one
/// side lacks is dropped.
/// </para>
/// <para>
/// <b>Join.</b> Facet by facet, the looser of the two, and a facet only one side has is dropped (<see cref="Join"/>).
/// </para>
/// <para>
/// <b>Witnesses.</b> Each facet carries where it came from (<see cref="IrFactSource"/>). Of two equal facts from different
/// sources the join keeps the fact, with the left one's source; the source is a witness to the fact, not part of it, and either justifies
/// it for both sides, since both sides' values are bounded by the same value. A value set's items and sources are kept in
/// the order they were found. So the join is commutative only up to <see cref="Same"/>, which compares sources and value
/// sets as sets. One caveat: <see cref="TypeOnly"/> reads the source after a join, and keeps a fact only when its source is
/// the type. A type-sourced fact holds for every value of the type, and the join only meets facts of the same SQL type, so
/// keeping the left one's source never makes a claim false. When the left one is a check's, the fact is kept by the join
/// and dropped by a later <see cref="TypeOnly"/>, which only claims less.
/// </para>
/// </remarks>
internal static class ScalarFacets
{
    /// <summary>
    /// The facets true of a value of either side: a fact only one side has is dropped, and of two the looser is kept (the
    /// longer maximum length, the shorter minimum, the lower minimum and the higher maximum, the union of two sets).
    /// </summary>
    public static IrFacets? Join(IrFacets? a, IrFacets? b)
    {
        if (a is null || b is null)
            return null;

        var length = a.Length is { } la && b.Length is { } lb && la == lb with { From = la.From } ? la
            : a.Length is { IsFixed: false } x && b.Length is { IsFixed: false } y && x.Unit == y.Unit && x.Encoding == y.Encoding
                ? x.Value >= y.Value ? x : y
                : null;
        // The minimum both allow: the one the other implies, or, of two in different units that don't imply each other, the fewer
        // code units both hold. Its source is the left's, which implies it as the right does: with minima in two units, keeping the
        // weaker one's own source would make the join's witness depend on how three are grouped.
        var minLength = a.MinLength is { } ma && b.MinLength is { } mb
            ? (mb.Implies(ma) ? ma : ma.Implies(mb) ? mb : new IrMinLength(Math.Min(ma.FewestUtf16Units, mb.FewestUtf16Units), ma.From)) with { From = ma.From }
            : null;
        var digits = a.Digits is { } da && b.Digits is { } db && da == db with { From = da.From } ? da : null;
        var joined = new IrFacets(length, minLength, digits, Bounds.Looser(a.Min, b.Min, BoundSide.Lower, BoundOf), Bounds.Looser(a.Max, b.Max, BoundSide.Upper, BoundOf), Union(a.Values, b.Values));
        return joined.IsEmpty ? null : joined;
    }

    /// <summary>
    /// Whether <paramref name="a"/> proves at least what <paramref name="b"/> does, facet by facet, so that every value it
    /// allows <paramref name="b"/> allows: each facet <paramref name="b"/> has, <paramref name="a"/> has too, as tight or
    /// tighter. A length no longer (a fixed length is below a maximum as long), a minimum length no shorter, digits the
    /// same, a lower bound no lower and an upper bound no higher (at the same value, closed only where the other is), a value
    /// set a subset of the other's, of the same match. Sources are not compared. Written apart from <see cref="Join"/>, so
    /// that the law tests can check the join against it; the join is not always least in it (a fixed length beside a maximum
    /// is dropped, where the maximum would do).
    /// </summary>
    public static bool Below(IrFacets? a, IrFacets? b)
    {
        if (b is null)
            return true;
        if (a is null)
            return false;
        return Holds(a.Length, b.Length, (x, y) => x.Unit == y.Unit && x.Encoding == y.Encoding && (y.IsFixed ? x.IsFixed && x.Value == y.Value : x.Value <= y.Value))
            && Holds(a.MinLength, b.MinLength, (x, y) => x.Implies(y))
            && Holds(a.Digits, b.Digits, (x, y) => x.Precision == y.Precision && x.Scale == y.Scale)
            && Holds(a.Min, b.Min, (x, y) => BoundBelow(x, y, BoundSide.Lower))
            && Holds(a.Max, b.Max, (x, y) => BoundBelow(x, y, BoundSide.Upper))
            && Holds(a.Values, b.Values, (x, y) => x.Match == y.Match && x.Fold == y.Fold && x.Items.All(y.Items.Contains));

        static bool Holds<T>(T? x, T? y, Func<T, T, bool> tighter) where T : class => y is null || (x is not null && tighter(x, y));

        static bool BoundBelow(IrBound x, IrBound y, BoundSide side) =>
            BoundOf(x) is { } p && BoundOf(y) is { } q && Bounds.Below(p, q, side);
    }

    /// <summary>
    /// Whether two facets say the same of a value: the same facts with the same values, each fact's source being a witness
    /// that is not compared, and a value set's items and sources compared as sets. Structural equality compares all of it.
    /// </summary>
    public static bool Same(IrFacets? a, IrFacets? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        return Same(a.Length, b.Length, (x, y) => x with { From = y.From } == y)
            && Same(a.MinLength, b.MinLength, (x, y) => (x.Value, x.Unit) == (y.Value, y.Unit))
            && Same(a.Digits, b.Digits, (x, y) => x with { From = y.From } == y)
            && Same(a.Min, b.Min, (x, y) => x with { From = y.From } == y)
            && Same(a.Max, b.Max, (x, y) => x with { From = y.From } == y)
            && Same(a.Values, b.Values, (x, y) => x.Match == y.Match && x.Fold == y.Fold && x.Items.ToHashSet().SetEquals(y.Items) && x.From.ToHashSet().SetEquals(y.From));

        static bool Same<T>(T? x, T? y, Func<T, T, bool> same) where T : class =>
            x is null || y is null ? x is null && y is null : same(x, y);
    }

    /// <summary>
    /// The facets true of a value drawn from any of <paramref name="each"/> (as <see cref="Join"/>), and where none is left,
    /// the type's own (<paramref name="type"/>), which every value has. For a set operation's column whose queries' values
    /// convert unchanged.
    /// </summary>
    public static IrFacets? Widened(IReadOnlyList<IrFacets?> each, IrFacets? type)
    {
        var lub = each.Count == 0 ? null : each.Skip(1).Aggregate(each[0], Join);
        if (lub is null)
            return type;
        if (type is null)
            return lub;
        return new IrFacets(lub.Length ?? type.Length, lub.MinLength ?? type.MinLength, lub.Digits ?? type.Digits, lub.Min ?? type.Min, lub.Max ?? type.Max, lub.Values ?? type.Values);
    }

    /// <summary>The facets a type alone proves: every value of the type has them, whatever constraint it came through.</summary>
    public static IrFacets? TypeOnly(IrFacets? facets)
    {
        if (facets is null)
            return null;
        static bool FromType(IrFactSource from) => from.Kind == IrFactSourceKind.Type;
        var kept = new IrFacets(
            Length: facets.Length is { } l && FromType(l.From) ? l : null,
            MinLength: facets.MinLength is { } m && FromType(m.From) ? m : null,
            Digits: facets.Digits is { } d && FromType(d.From) ? d : null,
            Min: facets.Min is { } min && FromType(min.From) ? min : null,
            Max: facets.Max is { } max && FromType(max.From) ? max : null,
            Values: null);
        return kept.IsEmpty ? null : kept;
    }

    /// <summary>
    /// The facets that hold of a masked value: what its type proves (<see cref="TypeOnly"/>), less a fixed length and any minimum
    /// length. A caller without <c>UNMASK</c> reads the mask in place of a masked column's value, and in place of any value computed
    /// from one: <c>0</c>, <c>'xxxx'</c>, <c>1900-01-01</c>, a value <c>random()</c> draws or what <c>email()</c> and <c>partial()</c>
    /// write, never longer than the type and never outside its range (a <c>random()</c> range outside it is refused, 16005), but
    /// not padded to a <c>char</c>'s or <c>binary</c>'s length, and as short as the mask, however long the value
    /// (observed: <c>MaskingObservationTests</c>; docs/decisions/0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md).
    /// </summary>
    /// <param name="facets">The value's facets.</param>
    /// <param name="type">
    /// What its type alone proves, for a fact a narrower one replaced: a CHECK's range takes the place of the type's, and the mask
    /// keeps the type's in its place. A fact the value doesn't have is not added, which would claim more than it did.
    /// </param>
    public static IrFacets? Masked(IrFacets? facets, IrFacets? type)
    {
        if (facets is null)
            return null;
        var own = TypeOnly(facets) ?? new IrFacets();
        var typed = TypeOnly(type) ?? new IrFacets();
        var length = own.Length ?? (facets.Length is null ? null : typed.Length);
        var kept = new IrFacets(
            length is null ? null : length with { IsFixed = false },
            null,
            own.Digits ?? (facets.Digits is null ? null : typed.Digits),
            own.Min ?? (facets.Min is null ? null : typed.Min),
            own.Max ?? (facets.Max is null ? null : typed.Max));
        return kept.IsEmpty ? null : kept;
    }

    /// <summary>An IR bound as the bound lattice orders it, or null when its value isn't a plain decimal.</summary>
    private static Bound? BoundOf(IrBound bound) => ExactDecimal.TryParse(bound.Value, out var value) ? new Bound(value, bound.Inclusive) : null;

    private static IrValueSet? Union(IrValueSet? a, IrValueSet? b)
    {
        if (a is null || b is null || a.Match != b.Match || a.Fold != b.Fold || a.Items.Any(i => i.IsString) != b.Items.Any(i => i.IsString))
            return null;

        // A canonical set's items are in ordinal order, as the IR README says; two may fold alike now, which the README allows.
        var items = a.Items.Concat(b.Items).Distinct();
        return new IrValueSet([.. a.Match == IrValueMatch.Canonical ? items.OrderBy(i => i.Text, StringComparer.Ordinal) : items], a.Match, [.. a.From.Concat(b.From).Distinct()], a.Fold);
    }
}

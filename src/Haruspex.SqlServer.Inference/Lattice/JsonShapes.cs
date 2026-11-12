using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>
/// The lattice of JSON built here: what a variable, a column or a <c>CASE</c> over JSON holds when it may hold the JSON of
/// any of several writes or branches. The guide's chapter <c>docs/internals/05-values-as-a-lattice.md</c>, "One lattice per
/// fact", explains the idea.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order.</b> A shape below another allows no JSON the other doesn't. An unknown is the top. A union allows what any of
/// its variants allows, so JSON content, unlike a SQL value, can always be joined exactly: two different shapes become a
/// union of both.
/// </para>
/// <para>
/// <b>Witnesses.</b> A union lists its variants in the order they were found, the left side's first, and that order reaches
/// the IR and the Zod, which tries variants in order. The order is presentation, deterministic for a given body, so the
/// join is commutative only up to variants compared as a multiset. Of two unknowns the left one is kept.
/// </para>
/// </remarks>
internal static class JsonShapes
{
    /// <summary>
    /// JSON that is either <paramref name="a"/> or <paramref name="b"/>: what a variable or a column holds after
    /// several writes of JSON built here, and what <c>COALESCE</c>, <c>CASE</c> or <c>ISNULL</c> over JSON holds. The same shape
    /// is itself; an unknown stays unknown; arrays and tuples become an array of the items of both (the empty tuple <c>[]</c>
    /// adds no item, so <c>COALESCE(@lines, '[]')</c> keeps the lines' items and only loses their least count); objects with the
    /// same keys in the same order join key by key; scalars of one type keep the facts both prove; anything else is a union of
    /// both, which is exact.
    /// </summary>
    public static Shape Join(Shape a, Shape b)
    {
        if (a == b)
            return a;
        if (a is UnknownShape)
            return a;
        if (b is UnknownShape)
            return b;

        switch (a, b)
        {
            case (TupleShape x, TupleShape y) when x.Items.Count == y.Items.Count:
                return new TupleShape([.. x.Items.Zip(y.Items, (p, q) => new ElementShape(Join(p.Type, q.Type), NullReasons.Join(p.NullableBecause, q.NullableBecause)))]);
            case (TupleShape or ArrayShape, TupleShape or ArrayShape):
                return Items(a, b);
            case (ObjectShape x, ObjectShape y) when Objects(x, y) is { } joined:
                return joined;
            case (ScalarShape { Type: var x }, ScalarShape { Type: var y }) when x.Kind == y.Kind && x.Format == y.Format && x.Sql == y.Sql:
                return new ScalarShape(x with { Facets = ScalarFacets.Join(x.Facets, y.Facets) });
            case (RecordShape x, RecordShape y):
                return new RecordShape(Join(x.Values, y.Values), NullReasons.Join(x.ValuesNullableBecause, y.ValuesNullableBecause));
        }

        // A union: each variant of either once, a variant joining the first of its own kind it can (an array with an array).
        var variants = new List<Shape>();
        foreach (var variant in Variants(a).Concat(Variants(b)))
        {
            var i = variants.FindIndex(v => Mergeable(v, variant));
            if (i < 0)
                variants.Add(variant);
            else
                variants[i] = Join(variants[i], variant);
        }

        return variants.Count == 1 ? variants[0] : new UnionShape([.. variants]);

        static IEnumerable<Shape> Variants(Shape s) => s is UnionShape u ? u.Variants : [s];
    }

    /// <summary>Whether two variants of a union join into one without a union: arrays and tuples, or the same shape.</summary>
    private static bool Mergeable(Shape a, Shape b) =>
        (a is TupleShape or ArrayShape && b is TupleShape or ArrayShape) || a == b
        || (a is ObjectShape x && b is ObjectShape y && Objects(x, y) is not null)
        || (a is ScalarShape { Type: var s } && b is ScalarShape { Type: var t } && s.Kind == t.Kind && s.Format == t.Format && s.Sql == t.Sql);

    /// <summary>
    /// Two objects with the same keys in the same order, key by key; null when they differ, or when a key is present as null
    /// on one side and left out when null on the other, which one field cannot say (a union of both objects can).
    /// </summary>
    private static ObjectShape? Objects(ObjectShape x, ObjectShape y)
    {
        if (!x.Fields.Select(f => f.Name).SequenceEqual(y.Fields.Select(f => f.Name), StringComparer.Ordinal))
            return null;
        var fields = new List<FieldShape>();
        foreach (var (f, g) in x.Fields.Zip(y.Fields))
        {
            if (f.Presence != g.Presence && (f.Presence == IrPresence.Always ? f : g).NullableBecause.Count > 0)
                return null;
            fields.Add(new FieldShape(f.Name, Join(f.Type, g.Type), f.Presence == g.Presence ? f.Presence : IrPresence.OmittedWhenNull, NullReasons.Join(f.NullableBecause, g.NullableBecause)));
        }

        return new ObjectShape([.. fields]);
    }

    /// <summary>An array of the items of two arrays or tuples: each item of either, between the fewest and most of both.</summary>
    private static ArrayShape Items(Shape a, Shape b)
    {
        static (List<Shape> Items, List<IrNullReason> Nulls, int Min, int? Max) Of(Shape s) => s switch
        {
            TupleShape t => ([.. t.Items.Select(e => e.Type)], [.. t.Items.SelectMany(e => e.NullableBecause)], t.Items.Count, t.Items.Count),
            ArrayShape r => ([r.Items], [.. r.ItemsNullableBecause], r.MinItems ?? 0, r.MaxItems),
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        };

        var (x, y) = (Of(a), Of(b));
        var all = x.Items.Concat(y.Items).ToList();
        var items = all.Count == 0 ? null : all.Skip(1).Aggregate(all[0], Join);
        var nulls = NullReasons.Join([.. x.Nulls.Distinct()], [.. y.Nulls.Distinct()]);
        var min = Math.Min(x.Min, y.Min);
        int? max = x.Max is { } p && y.Max is { } q ? Math.Max(p, q) : null;

        // Two empty tuples are the same shape, handled before; an empty one beside another empty has no items to type.
        return new ArrayShape(items!, nulls, min == 0 ? null : min, max);
    }
}

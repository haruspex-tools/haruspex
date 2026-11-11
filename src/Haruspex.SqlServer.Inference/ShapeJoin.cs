using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Joins of values: what a place holds when it may hold either of two values. Used where paths meet after a branch, where
/// a write adds values to a column of a temporary table, and where a recursive CTE's members add rows to its anchor's.
/// Each join is a least upper bound in what it claims, so it never claims more than either side does.
/// </summary>
internal static class ShapeJoin
{
    /// <summary>
    /// A value that is either <paramref name="a"/> or <paramref name="b"/>: null if either may be, of a shape both fit.
    /// Shapes that cannot be joined (two different objects, a string and a number) give an unknown.
    /// </summary>
    /// <param name="at">Where the two meet, for an unknown.</param>
    public static Value Either(Value a, Value b, SourceLocation at, string what)
    {
        // Either one's constant digits may be counted (Value.Digits): the fewest of both, up to 10.
        if (ShapeText.Of(a) == ShapeText.Of(b))
            return a.Digits == b.Digits ? a : a with { Digits = IntDigits.Foldable([a.Digits ?? new IntDigits(10, 10), b.Digits ?? new IntDigits(10, 10)]) };

        var because = Reasons(a.NullableBecause, b.NullableBecause);
        if (a.Shape is UnknownShape known)
            return new Value(known, WithUnknown(because, known));
        if (b.Shape is UnknownShape other)
            return new Value(other, WithUnknown(because, other));

        if (a.IsLiveJson == b.IsLiveJson && Scalars(a.Shape, b.Shape) is { } scalar)
            return new Value(scalar, because, a.IsLiveJson);
        if (a.IsLiveJson == b.IsLiveJson && ShapeText.Of(a.Shape) == ShapeText.Of(b.Shape))
            return new Value(a.Shape, because, a.IsLiveJson);

        var unknown = new UnknownShape(IrUnknownReasons.UnsupportedExpression, $"{what} holds values of different shapes on different paths, which are not joined yet.", at);
        return new Value(unknown, WithUnknown(because, unknown));
    }

    /// <summary>
    /// A value of <paramref name="typed"/>'s SQL type that is either <paramref name="typed"/> or <paramref name="other"/>
    /// converted to that type: what a column holds after a write converts a value to the column's type, and what a
    /// recursive CTE's column holds, whose type is its anchor's exactly (SQL Server refuses any other, error 240, observed).
    /// A scalar keeps the facets both sides prove, and at least those its type proves; any other shape stays only when
    /// both are the same.
    /// </summary>
    public static Value Converted(Value typed, Value other, SourceLocation at, string what)
    {
        var because = Reasons(typed.NullableBecause, other.NullableBecause);
        if (typed.Shape is ScalarShape scalar && !typed.IsLiveJson)
        {
            var joined = other.Shape is ScalarShape { Type: var o } && !other.IsLiveJson && SameType(scalar.Type, o)
                ? new ScalarShape(scalar.Type with { Facets = Lub(scalar.Type.Facets, o.Facets) })
                : new ScalarShape(scalar.Type with { Facets = TypeOnly(scalar.Type.Facets) });
            return new Value(joined, because);
        }

        return Either(typed, other, at, what);
    }

    /// <summary>
    /// The facets true of a value drawn from any of <paramref name="each"/> (as <see cref="Lub"/>: a fact one lacks is dropped,
    /// of two the looser kept), and where none is left, the type's own (<paramref name="type"/>), which every value has. For a
    /// set operation's column whose queries' values convert unchanged (slice 6k).
    /// </summary>
    public static IrFacets? Widened(IReadOnlyList<IrFacets?> each, IrFacets? type)
    {
        var lub = each.Count == 0 ? null : each.Skip(1).Aggregate(each[0], Lub);
        if (lub is null)
            return type;
        if (type is null)
            return lub;
        return new IrFacets(lub.Length ?? type.Length, lub.MinLength ?? type.MinLength, lub.Digits ?? type.Digits, lub.Min ?? type.Min, lub.Max ?? type.Max, lub.Values ?? type.Values);
    }

    /// <summary>The reasons of both, each once, in order.</summary>
    public static IReadOnlyList<IrNullReason> Reasons(IReadOnlyList<IrNullReason> a, IReadOnlyList<IrNullReason> b) =>
        b.Count == 0 ? a : a.Count == 0 ? b : [.. a.Concat(b).Distinct()];

    /// <summary>A value whose shape is unknown proves nothing, so it may be null, which its reasons say.</summary>
    private static IReadOnlyList<IrNullReason> WithUnknown(IReadOnlyList<IrNullReason> because, UnknownShape unknown) =>
        Reasons(because, [new IrNullReason(IrNullRules.Unknown, unknown.At.ToString())]);

    private static ScalarShape? Scalars(Shape a, Shape b) =>
        a is ScalarShape { Type: var x } && b is ScalarShape { Type: var y } && SameType(x, y)
            ? new ScalarShape(x with { Facets = Lub(x.Facets, y.Facets) })
            : null;

    private static bool SameType(IrScalar a, IrScalar b) => a.Kind == b.Kind && a.Format == b.Format && a.Sql == b.Sql;

    /// <summary>The facets a type alone proves: every value of the type has them, whatever constraint it came through.</summary>
    private static IrFacets? TypeOnly(IrFacets? facets)
    {
        if (facets is null)
            return null;
        static bool FromType(IrFactSource from) => from.Kind == IrFactSources.Type;
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
    /// The facets true of a value of either side: a fact only one side has is dropped, and of two the looser is kept (the
    /// longer maximum length, the shorter minimum, the lower minimum and the higher maximum, the union of two sets).
    /// </summary>
    private static IrFacets? Lub(IrFacets? a, IrFacets? b)
    {
        if (a is null || b is null)
            return null;

        var length = a.Length == b.Length ? a.Length
            : a.Length is { IsFixed: false } x && b.Length is { IsFixed: false } y && x.Unit == y.Unit && x.Encoding == y.Encoding
                ? x.Value >= y.Value ? x : y
                : null;
        var minLength = a.MinLength is { } ma && b.MinLength is { } mb ? (ma.Value <= mb.Value ? ma : mb) : null;
        var digits = a.Digits == b.Digits ? a.Digits : null;
        var joined = new IrFacets(length, minLength, digits, Looser(a.Min, b.Min, lower: true), Looser(a.Max, b.Max, lower: false), Union(a.Values, b.Values));
        return joined.IsEmpty ? null : joined;
    }

    private static IrBound? Looser(IrBound? a, IrBound? b, bool lower)
    {
        if (a is null || b is null || !ExactDecimal.TryParse(a.Value, out var x) || !ExactDecimal.TryParse(b.Value, out var y))
            return null;
        var order = x.CompareTo(y);
        if (order == 0)
            return a.Inclusive ? a : b; // an inclusive bound at the same value is the looser one
        return (order < 0) == lower ? a : b;
    }

    private static IrValueSet? Union(IrValueSet? a, IrValueSet? b)
    {
        if (a is null || b is null || a.Match != b.Match || a.Items.Any(i => i.IsString) != b.Items.Any(i => i.IsString))
            return null;
        return new IrValueSet([.. a.Items.Concat(b.Items).Distinct()], a.Match, [.. a.From.Concat(b.From).Distinct()]);
    }
}

using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The inferred type of an expression.
/// </summary>
/// <param name="Shape">Its shape when not null.</param>
/// <param name="NullableBecause">Why it can be null; empty when it is proved non-null.</param>
/// <param name="IsLiveJson">
/// Whether it is JSON that <c>FOR JSON</c> and the JSON constructors embed as JSON rather than escaping as a string:
/// the output of a <c>FOR JSON</c> subquery with its array wrapper, of a constructor (<c>json_object</c>, <c>json_array</c>,
/// <c>json_arrayagg</c>, <c>json_objectagg</c>), a value of the <c>json</c> type, <c>json_query</c> over any of them,
/// or a scalar subquery returning one. <see cref="Shape"/> is then the JSON's shape.
/// </param>
/// <param name="Digits">
/// For an <c>int</c> the server may treat as a constant, the integer digits it counts beside a decimal, when not the type's 10
/// (observed: An_int_constant_counts_its_own_digits_beside_a_decimal): a literal its own, carried through a derived
/// table, view column or CTE that does not recurse (one that does is never a constant, observed:
/// A_recursive_ctes_column_is_not_a_constant_even_when_every_member_is_one_literal); a NULL cast to int 1; a CASE, IIF, CHOOSE, COALESCE or ISNULL that the server may fold to one
/// of its literals a range, from the fewest of them to 10. Null for anything else, which counts as its type.
/// </param>
/// <remarks>
/// JSON text whose shape is known (a <c>FOR JSON</c> subquery without its array wrapper, a call of a function returning
/// JSON) has a <see cref="JsonTextShape"/> with its content, which <c>json_query</c> makes live again.
/// </remarks>
/// <param name="JsonWhy">
/// For a string read from a place that holds JSON built here on some writes but may hold other text (<see cref="JsonSlot"/>), why
/// its JSON's shape is not known, naming the write: what <c>json_query</c> over it reports. Null otherwise.
/// </param>
internal sealed record Value(Shape Shape, EquatableArray<IrNullReason> NullableBecause, bool IsLiveJson = false, IntDigits? Digits = null, UnknownShape? JsonWhy = null)
{
    public static Value Unknown(UnknownShape shape) =>
        new(shape, [new IrNullReason(IrNullRule.Unknown, shape.At.ToString())]);

    // ---- the knowledge join --------------------------------------------------------------------------------------

    /// <summary>
    /// The knowledge join: a value that is either <paramref name="a"/> or <paramref name="b"/>, null if either may be, of a
    /// shape both fit. What a place holds where paths meet after a branch, where a write adds values to a column of a
    /// temporary table, and where a recursive CTE's members add rows to its anchor's. Shapes that cannot be joined (two
    /// different objects, a string and a number) give an unknown. It is not the server's rule for the type of a <c>CASE</c>
    /// or a set operation, which is <see cref="ScalarType.Chain"/>.
    /// </summary>
    /// <param name="at">Where the two meet, for an unknown.</param>
    /// <param name="what">What holds the two, for an unknown's detail.</param>
    public static Value Join(Value a, Value b, SourceLocation at, string what)
    {
        // The same value but for the digits a constant counts and why its JSON isn't known, which are not compared: either
        // one's digits may be counted (Value.Digits), so the fewest of both, up to 10; the first one's why is kept.
        if (a.Shape == b.Shape && a.NullableBecause == b.NullableBecause && a.IsLiveJson == b.IsLiveJson)
            return a.Digits == b.Digits ? a : a with { Digits = IntDigits.Foldable([a.Digits ?? new IntDigits(10, 10), b.Digits ?? new IntDigits(10, 10)]) };

        var because = NullReasons.Join(a.NullableBecause, b.NullableBecause);
        if (a.Shape is UnknownShape known)
            return new Value(known, WithUnknown(because, known));
        if (b.Shape is UnknownShape other)
            return new Value(other, WithUnknown(because, other));

        // JSON text built here on both: text of either's content. Text beside a string of its own type, nvarchar(max), is
        // that string, which may not be JSON; json_query over it reports why.
        if (!a.IsLiveJson && !b.IsLiveJson)
        {
            switch (a.Shape, b.Shape)
            {
                case (JsonTextShape x, JsonTextShape y):
                    return new Value(new JsonTextShape(x.Content is null || y.Content is null ? null : JsonShapes.Join(x.Content, y.Content)), because);
                case (JsonTextShape, ScalarShape { Type.Sql: "nvarchar(max)" } text):
                    return new Value(new ScalarShape(text.Type with { Facets = ScalarFacets.TypeOnly(text.Type.Facets) }), because, JsonWhy: b.JsonWhy ?? NotJson(at, what));
                case (ScalarShape { Type.Sql: "nvarchar(max)" } text, JsonTextShape):
                    return new Value(new ScalarShape(text.Type with { Facets = ScalarFacets.TypeOnly(text.Type.Facets) }), because, JsonWhy: a.JsonWhy ?? NotJson(at, what));
            }
        }

        if (a.IsLiveJson == b.IsLiveJson && Scalars(a.Shape, b.Shape) is { } scalar)
            return new Value(scalar, because, a.IsLiveJson);
        if (a.IsLiveJson == b.IsLiveJson && a.Shape == b.Shape)
            return new Value(a.Shape, because, a.IsLiveJson);

        var unknown = new UnknownShape(IrUnknownReason.UnsupportedExpression, $"{what} holds values of different shapes on different paths, which are not joined yet.", at);
        return new Value(unknown, WithUnknown(because, unknown));
    }

    private static UnknownShape NotJson(SourceLocation at, string what) =>
        new(IrUnknownReason.JsonShapeUnknown, $"{what} holds JSON built here on some paths and other text on others, which meet {at}.", at);

    /// <summary>
    /// A value of <paramref name="typed"/>'s SQL type that is either <paramref name="typed"/> or <paramref name="other"/>
    /// converted to that type: what a column holds after a write converts a value to the column's type, and what a
    /// recursive CTE's column holds, whose type is its anchor's exactly (SQL Server refuses any other, error 240, observed).
    /// A scalar keeps the facets both sides prove, and at least those its type proves; any other shape stays only when
    /// both are the same.
    /// </summary>
    public static Value JoinConverted(Value typed, Value other, SourceLocation at, string what)
    {
        var because = NullReasons.Join(typed.NullableBecause, other.NullableBecause);
        if (typed.Shape is ScalarShape scalar && !typed.IsLiveJson)
        {
            var joined = other.Shape is ScalarShape { Type: var o } && !other.IsLiveJson && SameType(scalar.Type, o)
                ? new ScalarShape(scalar.Type with { Facets = ScalarFacets.Join(scalar.Type.Facets, o.Facets) })
                : new ScalarShape(scalar.Type with { Facets = ScalarFacets.TypeOnly(scalar.Type.Facets) });
            return new Value(joined, because);
        }

        return Join(typed, other, at, what);
    }

    /// <summary>A value whose shape is unknown proves nothing, so it may be null, which its reasons say.</summary>
    private static EquatableArray<IrNullReason> WithUnknown(EquatableArray<IrNullReason> because, UnknownShape unknown) =>
        NullReasons.Join(because, [new IrNullReason(IrNullRule.Unknown, unknown.At.ToString())]);

    private static ScalarShape? Scalars(Shape a, Shape b) =>
        a is ScalarShape { Type: var x } && b is ScalarShape { Type: var y } && SameType(x, y)
            ? new ScalarShape(x with { Facets = ScalarFacets.Join(x.Facets, y.Facets) })
            : null;

    private static bool SameType(IrScalar a, IrScalar b) => a.Kind == b.Kind && a.Format == b.Format && a.Sql == b.Sql;
}

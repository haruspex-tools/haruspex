using Haruspex.Ir;

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
/// (slice 6k, observed: An_int_constant_counts_its_own_digits_beside_a_decimal): a literal its own, carried through a derived
/// table, CTE or view column; a NULL cast to int 1; a CASE, IIF, CHOOSE, COALESCE or ISNULL that the server may fold to one
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
internal sealed record Value(Shape Shape, IReadOnlyList<IrNullReason> NullableBecause, bool IsLiveJson = false, IntDigits? Digits = null, UnknownShape? JsonWhy = null)
{
    public static Value Unknown(UnknownShape shape) =>
        new(shape, [new IrNullReason(IrNullRules.Unknown, shape.At.ToString())]);
}

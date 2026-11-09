using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

// The shapes inference works with. They are the IR's types before naming: objects are anonymous here, and
// unknowns carry their own location. IrBuilder turns them into the IR.

internal abstract record Shape;

internal sealed record ScalarShape(IrScalar Type) : Shape;

/// <param name="ItemsNullableBecause">Why an item can be null (only under <c>NULL ON NULL</c>); null or empty when never.</param>
/// <param name="MinItems">The fewest items, when more than none is proved.</param>
/// <param name="MaxItems">The most items, when bounded.</param>
internal sealed record ArrayShape(Shape Items, IReadOnlyList<IrNullReason>? ItemsNullableBecause = null, int? MinItems = null, int? MaxItems = null) : Shape;

/// <summary>A fixed-length array, each position with its own type: what <c>json_array</c> builds.</summary>
internal sealed record TupleShape(IReadOnlyList<ElementShape> Items) : Shape;

internal sealed record ElementShape(Shape Type, IReadOnlyList<IrNullReason> NullableBecause);

/// <summary>An object whose keys are data: what <c>json_objectagg</c> builds.</summary>
internal sealed record RecordShape(Shape Values, IReadOnlyList<IrNullReason> ValuesNullableBecause) : Shape;

/// <summary>One of several types, at least two and all different.</summary>
internal sealed record UnionShape(IReadOnlyList<Shape> Variants) : Shape;

internal sealed record ObjectShape(IReadOnlyList<FieldShape> Fields) : Shape;

/// <summary>A string holding JSON: what a JSON value becomes once a driver returns it as a column.</summary>
internal sealed record JsonTextShape : Shape;

internal sealed record UnknownShape(string Reason, string Detail, SourceLocation At) : Shape;

internal sealed record FieldShape(string Name, Shape Type, IrPresence Presence, IReadOnlyList<IrNullReason> NullableBecause);

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
/// <param name="TextOf">
/// For JSON text whose shape is known, the shape: what <c>json_query</c> makes of it. A <c>FOR JSON</c> subquery without
/// its array wrapper is such text (<see cref="Shape"/> is then <see cref="JsonTextShape"/>).
/// </param>
internal sealed record Value(Shape Shape, IReadOnlyList<IrNullReason> NullableBecause, bool IsLiveJson = false, Shape? TextOf = null)
{
    public static Value Unknown(UnknownShape shape) =>
        new(shape, [new IrNullReason(IrNullRules.Unknown, shape.At.ToString())]);
}

/// <summary>A result column before it is shaped into a rowset or JSON.</summary>
internal sealed record RowColumn(string? Name, Value Value, SourceLocation At);

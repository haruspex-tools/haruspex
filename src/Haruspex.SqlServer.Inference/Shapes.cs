using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

// The shapes inference works with. They are the IR's types before naming: objects are anonymous here, and
// unknowns carry their own location. IrBuilder turns them into the IR.

internal abstract record Shape;

internal sealed record ScalarShape(IrScalar Type) : Shape;

internal sealed record ArrayShape(Shape Items) : Shape;

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
/// Whether it is JSON that <c>FOR JSON</c> embeds as JSON rather than escaping as a string: the output of a
/// <c>FOR JSON</c> subquery, or <c>json_query</c> over one. <see cref="Shape"/> is then the JSON's shape.
/// </param>
internal sealed record Value(Shape Shape, IReadOnlyList<IrNullReason> NullableBecause, bool IsLiveJson = false)
{
    public static Value Unknown(UnknownShape shape) =>
        new(shape, [new IrNullReason(IrNullRules.Unknown, shape.At.ToString())]);
}

/// <summary>A result column before it is shaped into a rowset or JSON.</summary>
internal sealed record RowColumn(string? Name, Value Value, SourceLocation At);

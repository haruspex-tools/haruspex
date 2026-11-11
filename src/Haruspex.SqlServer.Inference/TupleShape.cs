namespace Haruspex.SqlServer.Inference;

/// <summary>A fixed-length array, each position with its own type: what <c>json_array</c> builds.</summary>
internal sealed record TupleShape(IReadOnlyList<ElementShape> Items) : Shape;

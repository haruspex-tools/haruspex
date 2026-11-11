namespace Haruspex.SqlServer.Inference;

/// <summary>One of several types, at least two and all different.</summary>
internal sealed record UnionShape(IReadOnlyList<Shape> Variants) : Shape;

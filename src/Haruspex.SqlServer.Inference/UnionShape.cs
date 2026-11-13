using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>One of several types, at least two and all different.</summary>
internal sealed record UnionShape(EquatableArray<Shape> Variants) : Shape;

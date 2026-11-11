namespace Haruspex.Ir;

/// <summary>One position: a tuple's element, or one of an object's results: <c>[0]</c>.</summary>
public sealed record IrPathIndex(int Index) : IrPathSegment;

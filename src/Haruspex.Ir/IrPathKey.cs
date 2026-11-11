namespace Haruspex.Ir;

/// <summary>An object's key, or a top-level part such as <c>results</c>: <c>.key</c>, or <c>["a key"]</c> when not an identifier.</summary>
public sealed record IrPathKey(string Key) : IrPathSegment;

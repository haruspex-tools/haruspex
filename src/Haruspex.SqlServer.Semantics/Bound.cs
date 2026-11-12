namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A limit on a number's values at one end: <c>&gt;= 0</c> or <c>&lt; 100</c>, with which end in a <see cref="BoundSide"/>
/// beside it. What a type's range, a CHECK constraint and the IR's bounds all say, ordered and combined by <see cref="Bounds"/>.
/// </summary>
/// <param name="Value">The limit.</param>
/// <param name="Inclusive">Whether the limit itself is allowed.</param>
public readonly record struct Bound(ExactDecimal Value, bool Inclusive);

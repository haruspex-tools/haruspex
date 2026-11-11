namespace Haruspex.SqlServer.Semantics;

/// <param name="Value">The bound.</param>
/// <param name="Inclusive">Whether the bound itself is allowed.</param>
/// <param name="From">The type or constraint it came from.</param>
public sealed record BoundFact(ExactDecimal Value, bool Inclusive, FactSource From);

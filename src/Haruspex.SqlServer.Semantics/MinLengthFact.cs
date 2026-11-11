namespace Haruspex.SqlServer.Semantics;

/// <param name="Value">The minimum length in UTF-16 code units.</param>
/// <param name="From">The constraint.</param>
public sealed record MinLengthFact(int Value, FactSource From);

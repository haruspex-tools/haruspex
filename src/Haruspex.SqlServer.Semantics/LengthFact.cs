namespace Haruspex.SqlServer.Semantics;

/// <param name="Value">The maximum, or the fixed length.</param>
/// <param name="IsFixed">Whether values are padded to exactly <paramref name="Value"/> (<c>char</c>, <c>nchar</c>).</param>
/// <param name="Unit">What <paramref name="Value"/> counts.</param>
/// <param name="Encoding">For bytes, the encoding they are in.</param>
/// <param name="From">The type.</param>
public sealed record LengthFact(int Value, bool IsFixed, TextUnit Unit, TextEncoding Encoding, FactSource From);

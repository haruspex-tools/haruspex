namespace Haruspex.SqlServer.Semantics;

/// <param name="Precision">Digits in all.</param>
/// <param name="Scale">Digits after the point.</param>
/// <param name="From">The type.</param>
public sealed record DigitsFact(int Precision, int Scale, FactSource From);

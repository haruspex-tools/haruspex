namespace Haruspex.Ir;

/// <summary>A decimal's digits: at most <paramref name="Precision"/> in all, <paramref name="Scale"/> of them after the point.</summary>
public sealed record IrDigits(int Precision, int Scale, IrFactSource From);

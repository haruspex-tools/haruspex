namespace Haruspex.Ir;

/// <param name="Value">The minimum length, in UTF-16 code units.</param>
/// <param name="From">Where it came from.</param>
public sealed record IrMinLength(int Value, IrFactSource From);

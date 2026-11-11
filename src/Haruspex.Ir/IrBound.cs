namespace Haruspex.Ir;

/// <summary>A bound on a number.</summary>
/// <param name="Value">The bound as exact decimal text, such as <c>-99999999999999999.99</c>; never rounded to a double.</param>
/// <param name="Inclusive">Whether the bound itself is allowed.</param>
/// <param name="From">Where it came from.</param>
public sealed record IrBound(string Value, bool Inclusive, IrFactSource From);

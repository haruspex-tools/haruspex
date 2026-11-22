namespace Haruspex.Ir;

/// <param name="Value">The minimum length, in UTF-16 code units.</param>
/// <param name="From">Where it came from.</param>
public sealed record IrMinLength(int Value, IrFactSource From)
{
    /// <summary>
    /// The fewest code points a value of at least <see cref="Value"/> UTF-16 code units holds, which a target counting code points
    /// may claim: a code point is one or two code units, so half as many, rounded up.
    /// </summary>
    public int FewestCodePoints => IrLength.Ceiling(Value, 2);
}

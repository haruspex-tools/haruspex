namespace Haruspex.Ir;

/// <summary>A string's minimum length, in UTF-16 code units or (0.29) in code points.</summary>
/// <param name="Value">The minimum length, in <see cref="Unit"/>.</param>
/// <param name="From">Where it came from.</param>
public sealed record IrMinLength(int Value, IrFactSource From)
{
    /// <summary>
    /// What <see cref="Value"/> counts: UTF-16 code units, as the server counts most lengths, or code points (0.29), as a JSON
    /// Schema's <c>minLength</c> and <c>LEN</c> under a supplementary-character collation count them. Never bytes.
    /// Why: docs/decisions/0095-a-length-is-the-server-s-and-a-target-claims-the-fewest-code-points-it-can-hold.md.
    /// </summary>
    public IrLengthUnit Unit { get; init; } = IrLengthUnit.Utf16;

    /// <summary>
    /// The fewest code points a value of at least <see cref="Value"/> holds, which a target counting code points may claim: in code
    /// points, the value itself; in UTF-16 code units, of which a code point is one or two, half as many, rounded up.
    /// </summary>
    public int FewestCodePoints => Unit == IrLengthUnit.CodePoints ? Value : IrLength.Ceiling(Value, 2);

    /// <summary>The fewest UTF-16 code units a value of at least <see cref="Value"/> holds: as many, since a code point is at least one.</summary>
    public int FewestUtf16Units => Value;

    /// <summary>
    /// Whether every value this minimum allows is allowed by <paramref name="other"/>: its fewest of <paramref name="other"/>'s unit
    /// reach <paramref name="other"/>'s value. Two minima in different units may each fail to imply the other (4 code units and
    /// 3 code points).
    /// </summary>
    public bool Implies(IrMinLength other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return (other.Unit == IrLengthUnit.CodePoints ? FewestCodePoints : FewestUtf16Units) >= other.Value;
    }
}

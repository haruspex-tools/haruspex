namespace Haruspex.SqlServer.Semantics;

/// <param name="Value">The minimum length, in UTF-16 code units, or in code points where <see cref="InCodePoints"/> says so.</param>
/// <param name="From">The constraint.</param>
public sealed record MinLengthFact(int Value, FactSource From)
{
    /// <summary>
    /// Whether <see cref="Value"/> counts code points: a <c>LEN</c> under a supplementary-character collation, which counts a
    /// surrogate pair as one character (<see cref="CheckReader"/>). Otherwise it counts UTF-16 code units.
    /// </summary>
    public bool InCodePoints { get; init; }

    /// <summary>Whether every value of at least this length is of at least <paramref name="other"/>: n code points are at least n code units, and n code units at least half as many code points.</summary>
    public bool Implies(MinLengthFact other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var fewest = other.InCodePoints && !InCodePoints ? (Value + 1) / 2 : Value;
        return fewest >= other.Value;
    }
}

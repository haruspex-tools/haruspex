namespace Haruspex.Ir;

/// <summary>
/// Facts about a scalar's non-null values, each with its source. Every one is a guarantee, never a guess: a fact
/// that is not proved is left out, which only leaves the type wider. None of them says anything about null;
/// nullability is the field's.
/// </summary>
/// <param name="Length">The string's length: a maximum, or a fixed length.</param>
/// <param name="MinLength">The string's minimum length in UTF-16 code units.</param>
/// <param name="Digits">The decimal's precision and scale.</param>
/// <param name="Min">The number's lower bound.</param>
/// <param name="Max">The number's upper bound.</param>
/// <param name="Values">A set the value is drawn from.</param>
public sealed record IrFacets(
    IrLength? Length = null,
    IrMinLength? MinLength = null,
    IrDigits? Digits = null,
    IrBound? Min = null,
    IrBound? Max = null,
    IrValueSet? Values = null)
{
    /// <summary>Whether no fact is known.</summary>
    public bool IsEmpty => Length is null && MinLength is null && Digits is null && Min is null && Max is null && Values is null;
}

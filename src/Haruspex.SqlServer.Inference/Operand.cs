using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// An operand of one of the server's type rules (<see cref="ScalarType"/>): its type, and how many integer digits the server
/// counts for it beside a decimal. A column, a variable or a result counts its type's (<see cref="OfType"/>); an integer
/// literal, or an int the server folds to one, its own (observed: <c>An_int_constant_counts_its_own_digits_beside_a_decimal</c>).
/// </summary>
/// <remarks>
/// The count is a fact about an operand, not about a type, so it lives here and on <see cref="Value.Digits"/> (where it may
/// be a range, whose ends are worked out one at a time), never on <see cref="ScalarType"/>: a rule's result is a type, and
/// cannot carry an operand's count by accident. Every operand is built with its count named, <see cref="OfType"/> or a
/// number, so no default can stand in for a forgotten one. The guide's chapter <c>docs/internals/05-values-as-a-lattice.md</c>,
/// "Facts dropped by default", says why.
/// </remarks>
/// <param name="Type">The operand's type.</param>
/// <param name="Digits">The integer digits the server counts for it; null for its type's own.</param>
internal sealed record Operand(ScalarType Type, int? Digits)
{
    /// <summary>An operand that counts its type's digits: anything the server doesn't fold to an integer literal.</summary>
    public static Operand OfType(ScalarType type) => new(type, null);

    /// <summary>
    /// The decimal an exact number is converted to beside a decimal: its own precision and scale; an integer's digits, its
    /// type's or a literal's own (observed); money as decimal(19, 4).
    /// </summary>
    public (int Precision, int Scale) AsDecimal() => Type switch
    {
        { IsDecimal: true } or { IsMoney: true } => (Type.Precision, Type.Scale),
        _ when Digits is { } digits => (digits, 0),
        _ => (Type.IntegerDigits, 0),
    };
}

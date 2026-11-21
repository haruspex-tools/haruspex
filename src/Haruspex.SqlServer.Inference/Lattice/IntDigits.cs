namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>
/// The lattice of one fact: how many integer digits SQL Server counts for an <c>int</c> beside a decimal. A column, a variable
/// or the result of arithmetic counts its type's 10; a constant the server folds counts its own, so <c>1</c> beside a
/// <c>decimal(38, 38)</c> gives <c>decimal(38, 37)</c> (observed: <c>An_int_constant_counts_its_own_digits_beside_a_decimal</c>).
/// The guide's chapter <c>docs/internals/05-values-as-a-lattice.md</c>, "One lattice per fact", explains the idea.
/// </summary>
/// <remarks>
/// <para>
/// <b>Values.</b> Exactly <see cref="Least"/> digits when it equals <see cref="Most"/>; otherwise any number from
/// <see cref="Least"/> to 10, depending on whether the server folds the expression to one of its literals. <see cref="OfType"/>
/// is exactly 10, what every expression the server doesn't fold counts. <see cref="Unknown"/> is any number from 1 to 10.
/// </para>
/// <para>
/// <b>Order and join.</b> A range allows every count in it, so an exact count is below every range that holds it, and a range
/// is below a wider one. <see cref="Unknown"/> is the top: a type rule over it is then worked out at both ends, and its
/// type is claimed only where both agree. The join of two different counts is the range from the fewer to 10. There is no
/// bottom: each exact count is a minimal element, and the join is only ever taken of counts that exist.
/// </para>
/// <para>
/// <b>Dropped by default.</b> A <see cref="Value"/> made with <c>with</c> has <see cref="Unknown"/> digits unless the rule making
/// it passes the digits on or states them, and a value built new has them too unless its rule says otherwise. So a rule that
/// forgets the digits claims less, never something false: see <see cref="Value"/>.
/// </para>
/// </remarks>
internal sealed record IntDigits
{
    private IntDigits(int least, int most)
    {
        if (least is < 1 or > 10 || (most != least && most != 10))
            throw new ArgumentOutOfRangeException(nameof(least), $"An int counts 1 to 10 digits, exactly or up to 10: not {least} to {most}.");
        Least = least;
        Most = most;
    }

    /// <summary>The fewest digits the server may count.</summary>
    public int Least { get; }

    /// <summary>The most digits the server may count: <see cref="Least"/>, or 10.</summary>
    public int Most { get; }

    /// <summary>Exactly the type's 10 digits: what the server counts for anything it doesn't fold to a constant.</summary>
    public static IntDigits OfType { get; } = new(10, 10);

    /// <summary>Any number from 1 to 10: nothing is known, the top.</summary>
    public static IntDigits Unknown { get; } = new(1, 10);

    /// <summary>An integer literal's own digits, which the server counts exactly.</summary>
    public static IntDigits Literal(int digits) => new(digits, digits);

    /// <summary>
    /// The digits the server counts for an int literal written <paramref name="text"/> (digits, perhaps with a sign and leading
    /// zeros), negated or not: its own, at least one, except that a negative one of five digits, <c>-10000</c> to <c>-99999</c>,
    /// counts six.
    /// </summary>
    /// <remarks>
    /// The exception is observed, not documented: in every arithmetic operator, on either side, past 38 digits, beside a decimal
    /// in <c>COALESCE</c> and <c>UNION</c>, folded from <c>NULLIF</c>, and at every compatibility level, while every other length
    /// from one to ten, negative or not, counts its own (A_negative_int_literal_counts_its_own_digits_but_six_when_it_has_five,
    /// A_negative_five_digit_literal_counts_six_digits_at_every_level_alone_or_folded_from_nullif). Counting five there gave a
    /// scale one too large, a claim looser than the server's but not its type.
    /// </remarks>
    public static int CountOf(string text, bool negative)
    {
        var own = text.TrimStart('-', '+').TrimStart('0').Length is var d and > 0 ? d : 1;
        return own == 5 && negative != text.StartsWith('-') ? 6 : own;
    }

    /// <summary>Whether the count may be fewer than 10, depending on whether the server folds the expression to a constant.</summary>
    public bool IsUncertain => Least != Most;

    /// <summary>The digits of a value drawn from either side: the same count, or the range from the fewer to 10.</summary>
    public static IntDigits Join(IntDigits a, IntDigits b) => a == b ? a : new(Math.Min(a.Least, b.Least), 10);

    /// <summary>Whether <paramref name="a"/> allows no count that <paramref name="b"/> doesn't.</summary>
    public static bool Below(IntDigits a, IntDigits b) => a == b || (b.Most == 10 && b.Least <= a.Least);

    /// <summary>
    /// The server's rule for a <c>CASE</c>-like expression over these values: it may fold the expression to one of them, or
    /// leave it as an int of 10 digits, so the count runs from the fewest of them to 10. It is not the join: one value of
    /// <c>1</c> folds to 1 or stays 10.
    /// </summary>
    public static IntDigits Foldable(IEnumerable<IntDigits> results) => new(results.Select(d => d.Least).DefaultIfEmpty(10).Min(), 10);
}

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The lattice of one bound on a number's values (<see cref="Bound"/>), at one end (<see cref="BoundSide"/>): the one order
/// in which every bound haruspex holds is compared, tightened and loosened. The guide's chapter
/// <c>docs/internals/05-values-as-a-lattice.md</c>, "One lattice per fact", explains the idea.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order.</b> A bound is below another when it allows no value the other doesn't: at the lower end a higher value, at the
/// upper end a lower one, and at the same value an exclusive bound below an inclusive one (<c>&gt; 0</c> below <c>&gt;= 0</c>).
/// No bound at all allows every value and is the top.
/// </para>
/// <para>
/// <b>Meet and join.</b> Two facts that both hold, such as a type's range and a CHECK constraint's, give the tighter bound,
/// their meet (<see cref="Tighter"/>). A value that may come from either of two places has the looser, their join
/// (<see cref="Looser"/>), and none when either side has none.
/// </para>
/// <para>
/// <b>Witnesses.</b> A bound the IR or the schema holds carries where it came from. Of two that say the same, the meet keeps
/// the right one, the constraint read last, and the join the left one, as every join does (chapter 5, "Witnesses"). Either
/// justifies the bound, since both hold it.
/// </para>
/// </remarks>
public static class Bounds
{
    /// <summary>
    /// How <paramref name="a"/> compares with <paramref name="b"/> at <paramref name="side"/>: below zero when it allows fewer
    /// values, zero when it allows the same, above zero when it allows more.
    /// </summary>
    public static int Compare(Bound a, Bound b, BoundSide side)
    {
        var order = a.Value.CompareTo(b.Value);
        if (order != 0)
            return side == BoundSide.Lower ? -order : order;
        return a.Inclusive == b.Inclusive ? 0 : a.Inclusive ? 1 : -1;
    }

    /// <summary>Whether <paramref name="a"/> allows no value that <paramref name="b"/> doesn't.</summary>
    public static bool Below(Bound a, Bound b, BoundSide side) => Compare(a, b, side) <= 0;

    /// <summary>Whether <paramref name="value"/> is within <paramref name="bound"/> at <paramref name="side"/>.</summary>
    public static bool Admits(Bound bound, ExactDecimal value, BoundSide side) => Below(new Bound(value, true), bound, side);

    /// <summary>
    /// The meet: the tighter of two bounds that both hold, or the one there is; of two that say the same, the right one.
    /// </summary>
    /// <param name="a">One bound, or null for none.</param>
    /// <param name="b">The other.</param>
    /// <param name="side">The end they limit.</param>
    /// <param name="of">Each one's bound, so that a bound with a witness keeps it.</param>
    public static T? Tighter<T>(T? a, T? b, BoundSide side, Func<T, Bound> of) where T : class
    {
        ArgumentNullException.ThrowIfNull(of);
        if (a is null || b is null)
            return a ?? b;
        return Compare(of(a), of(b), side) < 0 ? a : b;
    }

    /// <summary>The meet of two bounds without witnesses: the tighter, or the one there is.</summary>
    public static Bound? Tighter(Bound? a, Bound? b, BoundSide side)
    {
        if (a is not { } x || b is not { } y)
            return a ?? b;
        return Compare(x, y, side) < 0 ? x : y;
    }

    /// <summary>
    /// The join: the looser of two bounds, or none when either side has none, or either can't be read (<paramref name="of"/>
    /// gives null); of two that say the same, the left one.
    /// </summary>
    /// <param name="a">One bound, or null for none.</param>
    /// <param name="b">The other.</param>
    /// <param name="side">The end they limit.</param>
    /// <param name="of">Each one's bound, or null when it can't be read.</param>
    public static T? Looser<T>(T? a, T? b, BoundSide side, Func<T, Bound?> of) where T : class
    {
        ArgumentNullException.ThrowIfNull(of);
        if (a is null || b is null || of(a) is not { } x || of(b) is not { } y)
            return null;
        return Compare(x, y, side) >= 0 ? a : b;
    }
}

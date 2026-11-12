using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>
/// The lattice of one fact: why a value may be null. The fact is a set of reasons, each a rule at a place in the source; the
/// set is kept as a list in the order its reasons were found, each once, which is the order the IR writes them in. The
/// guide's chapter <c>docs/internals/05-values-as-a-lattice.md</c>, "One lattice per fact", explains the idea.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order.</b> A set of reasons allows at most what a larger one allows: the empty set (proved never null) is the bottom,
/// and adding a reason only allows more. There is no top short of every reason there could be, and none is needed.
/// </para>
/// <para>
/// <b>Join.</b> The union, in order: the left side's reasons, then the right side's that it lacks. It is associative and
/// idempotent as lists, and commutative as sets: the two orders of a union list the same reasons, and only the order they
/// are written in differs. That order is presentation, not knowledge (<see cref="Same"/>).
/// </para>
/// </remarks>
internal static class NullReasons
{
    /// <summary>No reason: the value is proved never null, the bottom of this lattice.</summary>
    public static EquatableArray<IrNullReason> None => [];

    /// <summary>Why a value of either side may be null: each reason of both, once, the left side's first.</summary>
    public static EquatableArray<IrNullReason> Join(EquatableArray<IrNullReason> a, EquatableArray<IrNullReason> b) =>
        b.Count == 0 ? a : a.Count == 0 ? b : [.. a.Concat(b).Distinct()];

    /// <summary>Whether <paramref name="a"/> allows no more than <paramref name="b"/>: each of its reasons is one of <paramref name="b"/>'s.</summary>
    public static bool Below(IEnumerable<IrNullReason> a, IEnumerable<IrNullReason> b) => a.All(b.Contains);

    /// <summary>Whether two lists say the same: the same reasons, in any order. Structural equality also compares the order.</summary>
    public static bool Same(IEnumerable<IrNullReason> a, IEnumerable<IrNullReason> b) => Below(a, b) && Below(b, a);
}

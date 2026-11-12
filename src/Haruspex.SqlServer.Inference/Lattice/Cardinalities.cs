using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference.Lattice;

/// <summary>
/// The lattice of how many rows a result set has (<see cref="IrCardinality"/>): exactly one, at most one, or any number. The
/// guide's chapter <c>docs/internals/05-values-as-a-lattice.md</c>, "One lattice per fact", explains the idea.
/// </summary>
/// <remarks>
/// The three are a chain, ordered by the counts each allows: {1} within {0, 1} within every count. So <c>One</c> is below
/// <c>AtMostOne</c>, which is below <c>Many</c>, the top, which claims nothing. The order is written here, by rank, rather than
/// read from the enum's values, which are an accident of declaration order: <c>Many</c> is declared first.
/// </remarks>
internal static class Cardinalities
{
    private static int Rank(IrCardinality rows) => rows switch
    {
        IrCardinality.One => 0,
        IrCardinality.AtMostOne => 1,
        IrCardinality.Many => 2,
    };

    /// <summary>The fewest rows either side proves: the higher of the two in the chain.</summary>
    public static IrCardinality Join(IrCardinality a, IrCardinality b) => Rank(a) >= Rank(b) ? a : b;

    /// <summary>Whether <paramref name="a"/> allows no more counts than <paramref name="b"/>.</summary>
    public static bool Below(IrCardinality a, IrCardinality b) => Rank(a) <= Rank(b);
}

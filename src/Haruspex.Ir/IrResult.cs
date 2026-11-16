using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>One result set.</summary>
/// <param name="Encoding">How the result is delivered: as rows, or as JSON text to be parsed.</param>
/// <param name="Type">The shape of the result: for a rowset, an array of row objects.</param>
/// <param name="AbsentBecause">
/// Why the result can arrive with no value at all. A top-level <c>FOR JSON</c> query that matches no rows
/// returns no row, not an empty array or object. Readers follow one convention, stated in README.md: an
/// absent JSON result reads as <c>null</c>, whatever its type, and an absent rowset as <c>[]</c>.
/// </param>
public sealed record IrResult(IrEncoding Encoding, IrType Type, EquatableArray<IrNullReason> AbsentBecause)
{
    /// <summary>Whether the result can arrive with no value at all, which <see cref="AbsentBecause"/> explains.</summary>
    public bool MayBeAbsent => AbsentBecause.Count > 0;

    /// <summary>
    /// How many rows the result's query returns when it runs: a rowset's rows, or the objects of a FOR JSON result (the
    /// items of its array, or its one object without the array wrapper). Whether the result set may be missing is
    /// <see cref="AbsentBecause"/>, not this. <see cref="IrCardinality.Many"/> claims nothing.
    /// </summary>
    public IrCardinality Cardinality { get; init; } = IrCardinality.Many;
}

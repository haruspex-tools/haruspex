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

    /// <summary>
    /// The fewest items an array result has when it is read by the reader conventions in README.md ("Results"), or null when
    /// none is proved or the result is not an array: one for a JSON array, since <c>FOR JSON</c> over no rows returns no row,
    /// which reads as null rather than <c>[]</c>; one for a rowset that returns one row and is never absent, since an absent
    /// rowset reads as <c>[]</c>; and never fewer than the array's own <see cref="IrArray.MinItems"/>. A union of arrays is not
    /// bounded, which only claims less.
    /// </summary>
    public int? FewestItems => Type is IrArray array
        ? Tighter(array.MinItems, Encoding == IrEncoding.Json || (Cardinality == IrCardinality.One && !MayBeAbsent) ? 1 : null, Math.Max)
        : null;

    /// <summary>
    /// The most items an array result has, or null when it is unbounded or the result is not an array: one when the query returns
    /// at most one row, and never more than the array's own <see cref="IrArray.MaxItems"/>.
    /// </summary>
    public int? MostItems => Type is IrArray array
        ? Tighter(array.MaxItems, Cardinality is IrCardinality.One or IrCardinality.AtMostOne ? 1 : null, Math.Min)
        : null;

    private static int? Tighter(int? own, int? fromRows, Func<int, int, int> pick) =>
        own is { } a && fromRows is { } b ? pick(a, b) : own ?? fromRows;
}

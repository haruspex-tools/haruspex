using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>The columns of a relation and what each holds, or why they are not known.</summary>
/// <param name="Names">The columns' names; null when not known.</param>
/// <param name="Values">What each holds, by position; null when not known.</param>
/// <param name="Why">Why they are not known.</param>
internal sealed record LocalRelationValues(EquatableArray<string>? Names, EquatableArray<Value>? Values, UnknownShape? Why)
{
    public static LocalRelationValues Unknown(UnknownShape why) => new(null, null, why);

    /// <summary>
    /// How many rows it has where it is read (for an APPLY's right side, for each outer row): set for derived tables,
    /// VALUES lists and views from their query (Cardinality.cs); any number for the others, which claims nothing.
    /// </summary>
    public IrCardinality Rows { get; init; } = IrCardinality.Many;

    /// <summary>
    /// Whether it always has a row: a query of exactly one row, or a VALUES list. On an OUTER APPLY's right side it is never
    /// null-extended (observed: An_ungrouped_aggregate_applied_gives_one_row_for_every_outer_row).
    /// </summary>
    public bool AtLeastOne { get; init; }

    /// <summary>Its unique keys, which only a view has here (see <see cref="ProcedureAnalyzer"/>'s <c>ViewKeys</c>).</summary>
    public EquatableArray<RelationKey> Keys { get; init; } = [];
}

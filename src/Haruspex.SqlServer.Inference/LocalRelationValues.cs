using Haruspex.Collections;
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

    /// <summary>
    /// For each column, the table columns its value is read from (<c>Typer.Reads.cs</c>), so that a query over the relation traces
    /// them through it; null where the relation doesn't trace them (a temporary table, a table-valued function).
    /// </summary>
    public EquatableArray<EquatableArray<IrSourceColumn>>? From { get; init; }

    /// <summary>
    /// Its unique keys, which only a view (see <see cref="Typer"/>'s <c>ViewKeys</c>), a table variable (its <c>TableVariableValues</c>),
    /// a temporary table (<c>TemporaryKeys</c>) and a multi-statement function's return table have here.
    /// </summary>
    public EquatableArray<RelationKey> Keys { get; init; } = [];
}

using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>A field of an object type.</summary>
/// <param name="Name">The key or column name.</param>
/// <param name="Type">The value's type when it is not null.</param>
/// <param name="Presence">Whether the key is always there, or left out when the value is null.</param>
/// <param name="NullableBecause">
/// Why the value can be null. Empty means it is proved never null; that is the only way to say non-null,
/// so a nullable flag can never disagree with its reasons.
/// </param>
public sealed record IrField(string Name, IrType Type, IrPresence Presence, EquatableArray<IrNullReason> NullableBecause)
{
    /// <summary>Whether the value can be null: true exactly when <see cref="NullableBecause"/> gives a reason.</summary>
    public bool Nullable => NullableBecause.Count > 0;

    /// <summary>
    /// The table columns the value is read from, in the order the SQL first reads them (0.25): the start of its derivation, which
    /// <see cref="NullableBecause"/> continues. Empty when it reads none directly: a literal, a parameter, a count of rows, or a
    /// column of a view, a derived table or a CTE, which is not traced through. Where a claim came from, not a claim, so
    /// <see cref="IrComparison"/> reports no change in it.
    /// </summary>
    public EquatableArray<IrSourceColumn> From { get; init; } = [];

    /// <summary>
    /// In a document combining two databases (<see cref="IrSource.Baseline"/>), the field as each gave it, before they were
    /// combined (0.25); null in a document of one database. Where a claim came from, not a claim.
    /// </summary>
    public IrFieldSides? Sides { get; init; }
}

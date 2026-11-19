using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>A field of an object shape, as inference works with it before naming (<see cref="IrField"/>).</summary>
/// <remarks>
/// Two fields are equal when they claim the same: their columns (<see cref="From"/>) are left out, since where a value came from
/// is no claim. Otherwise two shapes the same but for the columns they read would stay apart where a union or a join merges equal
/// shapes, and the IR would claim more variants than the SQL gives.
/// </remarks>
internal sealed record FieldShape(string Name, Shape Type, IrPresence Presence, EquatableArray<IrNullReason> NullableBecause)
{
    /// <summary>The table columns its value is read from (<see cref="IrField.From"/>): the first's, where equal fields are merged.</summary>
    public EquatableArray<IrSourceColumn> From { get; init; } = [];

    /// <summary>Whether the two claim the same, whatever columns they read.</summary>
    public bool Equals(FieldShape? other) =>
        other is not null && Name == other.Name && Type == other.Type && Presence == other.Presence && NullableBecause == other.NullableBecause;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Name, Type, Presence, NullableBecause);
}

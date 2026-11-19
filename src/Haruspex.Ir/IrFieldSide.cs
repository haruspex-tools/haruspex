using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// A field as one of the two databases a combined document describes gives it (0.25): what <see cref="IrField.Sides"/> holds,
/// so that a reader can say how the combined field was made from the two, without either database's own document.
/// </summary>
/// <param name="Presence">Whether that database always sends the key, or leaves it out when null.</param>
/// <param name="NullableBecause">Why that database's value can be null; empty when it is never null there.</param>
/// <param name="Sql">The value's SQL type there, when it is a scalar (<see cref="IrScalar.Sql"/>); null for any other type.</param>
public sealed record IrFieldSide(IrPresence Presence, EquatableArray<IrNullReason> NullableBecause, string? Sql)
{
    /// <summary>The table columns the value is read from there (<see cref="IrField.From"/>).</summary>
    public EquatableArray<IrSourceColumn> From { get; init; } = [];

    /// <summary>The field of one database as a side: the side of a combined field it was combined from.</summary>
    public static IrFieldSide Of(IrField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new IrFieldSide(field.Presence, field.NullableBecause, (field.Type as IrScalar)?.Sql) { From = field.From };
    }
}

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
}

namespace Haruspex.Ir;

/// <summary>One position of a <see cref="IrTuple"/>.</summary>
/// <param name="Type">The element's type when it is not null.</param>
/// <param name="NullableBecause">Why it can be null; empty when it is proved never null.</param>
public sealed record IrElement(IrType Type, IReadOnlyList<IrNullReason> NullableBecause)
{
    /// <summary>Whether the element can be null: true exactly when <see cref="NullableBecause"/> gives a reason.</summary>
    public bool Nullable => NullableBecause.Count > 0;
}

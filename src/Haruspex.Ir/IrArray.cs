namespace Haruspex.Ir;

/// <summary>An array of items.</summary>
/// <param name="Items">The items' type when they are not null.</param>
public sealed record IrArray(IrType Items) : IrType
{
    /// <summary>
    /// Why an item can be null. Empty, the default, means no item is ever null: FOR JSON and the constructors' default
    /// <c>ABSENT ON NULL</c> leave nulls out of arrays, so only <c>NULL ON NULL</c> puts them in.
    /// </summary>
    public IReadOnlyList<IrNullReason> ItemsNullableBecause { get; init; } = [];

    /// <summary>Whether an item can be null: true exactly when <see cref="ItemsNullableBecause"/> gives a reason.</summary>
    public bool ItemsNullable => ItemsNullableBecause.Count > 0;

    /// <summary>The fewest items a non-null array has, when more than none is proved.</summary>
    public int? MinItems { get; init; }

    /// <summary>The most items it has, when that is bounded.</summary>
    public int? MaxItems { get; init; }
}

namespace Haruspex.Ir;

/// <summary>A set the value is drawn from.</summary>
/// <param name="Items">The values, in a stable order. Numbers are exact decimal text.</param>
/// <param name="Match">How a value relates to the items.</param>
/// <param name="From">Every source that narrowed the set.</param>
public sealed record IrValueSet(IReadOnlyList<IrValue> Items, IrValueMatch Match, IReadOnlyList<IrFactSource> From);

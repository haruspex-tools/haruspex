using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <param name="ItemsNullableBecause">Why an item can be null (only under <c>NULL ON NULL</c>); null or empty when never.</param>
/// <param name="MinItems">The fewest items, when more than none is proved.</param>
/// <param name="MaxItems">The most items, when bounded.</param>
internal sealed record ArrayShape(Shape Items, IReadOnlyList<IrNullReason>? ItemsNullableBecause = null, int? MinItems = null, int? MaxItems = null) : Shape;

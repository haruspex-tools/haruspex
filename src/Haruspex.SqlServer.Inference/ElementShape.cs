using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

internal sealed record ElementShape(Shape Type, IReadOnlyList<IrNullReason> NullableBecause);

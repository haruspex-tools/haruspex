using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

internal sealed record FieldShape(string Name, Shape Type, IrPresence Presence, EquatableArray<IrNullReason> NullableBecause);

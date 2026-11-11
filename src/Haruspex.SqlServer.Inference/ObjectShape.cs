using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

internal sealed record ObjectShape(EquatableArray<FieldShape> Fields) : Shape;

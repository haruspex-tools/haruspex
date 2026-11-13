using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

internal sealed record ObjectShape(EquatableArray<FieldShape> Fields) : Shape;

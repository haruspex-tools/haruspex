namespace Haruspex.SqlServer.Inference;

internal sealed record ObjectShape(IReadOnlyList<FieldShape> Fields) : Shape;

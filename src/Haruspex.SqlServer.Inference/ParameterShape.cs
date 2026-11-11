namespace Haruspex.SqlServer.Inference;

internal sealed record ParameterShape(string Name, Shape Type, bool HasDefault, bool IsOutput);

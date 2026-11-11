namespace Haruspex.Config;

internal sealed record JsonStringValue(string Value, int Line, int Column) : JsonValue(Line, Column);

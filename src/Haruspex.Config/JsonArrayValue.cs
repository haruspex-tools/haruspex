namespace Haruspex.Config;

internal sealed record JsonArrayValue(IReadOnlyList<JsonValue> Items, int Line, int Column) : JsonValue(Line, Column);

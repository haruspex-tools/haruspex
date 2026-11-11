namespace Haruspex.Config;

internal sealed record JsonObjectValue(IReadOnlyList<JsonMember> Members, int Line, int Column) : JsonValue(Line, Column);

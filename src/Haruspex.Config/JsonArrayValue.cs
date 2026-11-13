using Haruspex.Collections;

namespace Haruspex.Config;

internal sealed record JsonArrayValue(EquatableArray<JsonValue> Items, int Line, int Column) : JsonValue(Line, Column);

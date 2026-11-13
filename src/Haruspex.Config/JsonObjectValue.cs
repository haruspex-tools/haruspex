using Haruspex.Collections;

namespace Haruspex.Config;

internal sealed record JsonObjectValue(EquatableArray<JsonMember> Members, int Line, int Column) : JsonValue(Line, Column);

using System.Text.Json;

namespace Haruspex.Config;

internal sealed record JsonOtherValue(JsonTokenType Kind, string Text, int Line, int Column) : JsonValue(Line, Column);

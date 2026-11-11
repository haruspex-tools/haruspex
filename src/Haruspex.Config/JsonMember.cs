namespace Haruspex.Config;

/// <param name="Line">Where the key is written.</param>
internal sealed record JsonMember(string Name, JsonValue Value, int Line, int Column);

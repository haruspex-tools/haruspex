namespace Haruspex.SqlServer.Model;

/// <summary>One level of what an extended property is set on: its type (<c>TABLE</c>) and its name (<c>orders</c>).</summary>
/// <param name="Type">The level's type, as written; the server compares it ignoring case.</param>
/// <param name="Name">The name at that level.</param>
public readonly record struct PropertyLevel(string Type, string Name);

namespace Haruspex.Emit.JsonSchema.Model;

/// <summary>A named schema: a property of an object, or a definition in <c>$defs</c>.</summary>
/// <param name="Name">The key, as it is in the JSON.</param>
/// <param name="Schema">Its schema.</param>
internal sealed record SchemaProperty(string Name, Schema Schema);

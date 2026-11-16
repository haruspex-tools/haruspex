using Haruspex.Collections;

namespace Haruspex.Emit.JsonSchema.Model;

/// <summary>The document the printer writes: what the reader is told first, and the definitions, in order.</summary>
/// <param name="Description">The document's description, a line each: what made it, what it assumes, and the facts its notes rest on.</param>
/// <param name="Definitions">The definitions in <c>$defs</c>, by name, in order.</param>
internal sealed record JsonSchemaDocument(EquatableArray<string> Description, EquatableArray<SchemaProperty> Definitions);

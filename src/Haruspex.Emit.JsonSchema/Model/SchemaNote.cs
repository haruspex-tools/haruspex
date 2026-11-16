namespace Haruspex.Emit.JsonSchema.Model;

/// <summary>
/// A sentence for the reader of the schema, written into its <c>description</c>: where a fact came from, why a value may be null,
/// or what was widened because JSON Schema can't say it.
/// </summary>
/// <param name="Text">The sentence, starting with a capital and ending with a full stop.</param>
/// <param name="Caveat">The shared fact the note rests on, which the document's description states once; null for none.</param>
internal sealed record SchemaNote(string Text, JsonSchemaCaveat? Caveat = null);

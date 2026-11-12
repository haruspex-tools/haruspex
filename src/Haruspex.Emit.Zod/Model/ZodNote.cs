namespace Haruspex.Emit.Zod.Model;

/// <summary>A note for the reader of the generated file: one sentence on what a check means or why one is left out.</summary>
/// <param name="Text">The sentence, starting with a capital and ending with a full stop.</param>
/// <param name="Caveat">The shared fact the note rests on, which the module's header states once; null for none.</param>
internal sealed record ZodNote(string Text, ZodCaveat? Caveat = null);

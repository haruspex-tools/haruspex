namespace Haruspex.Emit.Zod.Model;

/// <summary>One exported schema and the type inferred from it: <c>export const NameSchema = …</c> and <c>export type Name</c>.</summary>
/// <param name="Name">The type's name; the schema's is the same with <c>Schema</c> after it.</param>
/// <param name="Summary">A sentence for the reader of the generated file, written as a documentation comment above it; null for none.</param>
/// <param name="Schema">The schema.</param>
internal sealed record ZodDeclaration(string Name, string? Summary, ZodExpression Schema);

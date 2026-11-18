namespace Haruspex.Emit.Zod.Model;

/// <summary>
/// One schema the module declares: exported with the type inferred from it, <c>export const NameSchema = …</c> and
/// <c>export type Name</c>; or, for a shape several exported names share, unexported, <c>const name = …</c>.
/// </summary>
/// <param name="Name">The type's name, the schema's being the same with <c>Schema</c> after it; or an unexported schema's own name.</param>
/// <param name="Summary">A sentence for the reader of the generated file, written as a documentation comment above it; null for none.</param>
/// <param name="Schema">The schema.</param>
/// <param name="Exported">Whether the module exports it, with its type; a shared shape that no declared name holds is not.</param>
internal sealed record ZodDeclaration(string Name, string? Summary, ZodExpression Schema, bool Exported = true);

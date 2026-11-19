using Haruspex.Collections;

namespace Haruspex.Emit.Zod.Model;

/// <summary>
/// One schema the module declares: exported with the type inferred from it, <c>export const NameSchema = …</c> and
/// <c>export type Name</c>; or, for a shape several exported names share, unexported, <c>const name = …</c>.
/// </summary>
/// <param name="Name">The type's name, the schema's being the same with <c>Schema</c> after it; or an unexported schema's own name.</param>
/// <param name="Summary">A sentence for the reader of the generated file, written as a documentation comment above it; null for none.</param>
/// <param name="Schema">The schema.</param>
/// <param name="Exported">Whether the module exports it, with its type; a shared shape that no declared name holds is not.</param>
/// <param name="Internal">
/// Whether an unexported shape is exported after all, for the modules that import it from a shared file, marked
/// <c>@internal</c> and with no type, since it is no public name
/// (<c>docs/decisions/0075-what-several-modules-share-is-written-once-in-a-shared-file.md</c>).
/// </param>
internal sealed record ZodDeclaration(string Name, string? Summary, ZodExpression Schema, bool Exported = true, bool Internal = false)
{
    /// <summary>The name the module binds its schema to: <c>OrderSchema</c> for an exported type, an unexported shape's own name.</summary>
    public string Binding => Exported ? ZodModule.SchemaOf(Name) : Name;

    /// <summary>
    /// Why the fields of a type exported as a shape another type shares are what they are, a line each, field by field: written
    /// above the export, since the shared schema's own properties can't say it for each type that shares it.
    /// </summary>
    public EquatableArray<string> Explanation { get; init; } = [];
}

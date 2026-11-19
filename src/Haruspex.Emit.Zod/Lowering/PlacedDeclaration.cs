using Haruspex.Collections;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// A declaration of the whole module with what it rests on: the object it belongs to, or the named types it declares.
/// </summary>
/// <param name="Declaration">The declaration.</param>
/// <param name="Object">The procedure or query file whose parameters or results it is; null for a named type's.</param>
/// <param name="Types">The named types it declares the schema of: one, or every type of a shape several share.</param>
internal sealed record PlacedDeclaration(ZodDeclaration Declaration, IrObject? Object, EquatableArray<string> Types);

using Haruspex.Emit.Zod.Lowering;
using Haruspex.Emit.Zod.Planning;
using Haruspex.Emit.Zod.Printing;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod;

/// <summary>
/// Emits one TypeScript module from an <see cref="IrDocument"/>: a Zod schema and an inferred type for every named object
/// type and every procedure. It reads only the IR; it knows nothing about SQL.
/// </summary>
/// <remarks>
/// <para>
/// Three stages, each with one job: <see cref="ZodPlanner"/> decides what is declared, in what order and under what names;
/// <see cref="ZodLowering"/> turns each IR type into a small model of Zod expressions; <see cref="ZodPrinter"/> writes the
/// model as TypeScript, and is the only stage that knows its syntax. The guide argues for the split:
/// <c>docs/internals/11-emitting-code.md</c>.
/// </para>
/// <para>
/// Targets Zod 4 (<c>z.int()</c>, <c>z.guid()</c>, <c>z.iso.*</c>). The output is deterministic, with LF line endings, so it
/// can be committed and checked in CI.
/// </para>
/// </remarks>
public static class ZodEmitter
{
    /// <summary>Emits the module with the default options.</summary>
    /// <exception cref="ZodEmitException">The document cannot be emitted: a name that is not an identifier, or an import that clashes.</exception>
    public static string Emit(IrDocument document) => Emit(document, new ZodOptions());

    /// <summary>Emits the module.</summary>
    /// <exception cref="ZodEmitException">The document cannot be emitted: a name that is not an identifier, or an import that clashes.</exception>
    public static string Emit(IrDocument document, ZodOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return ZodPrinter.Print(ZodLowering.Lower(ZodPlanner.Plan(document, options)));
    }
}

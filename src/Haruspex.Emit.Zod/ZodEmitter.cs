using Haruspex.Collections;
using Haruspex.Diagnostics;
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
    /// <summary>
    /// The least Zod the module is sound on, and the release every check of what Zod accepts was observed on: the emit check's
    /// pinned <c>zod</c>. An earlier release isn't: 4.4.3 counts a string's length in UTF-16 code units, so a declared maximum
    /// in code points refuses values the declaration allows. The npm package's peer range, the README, the quick start and the
    /// header's note on lengths all state it, and <c>NamedVersionsTests</c> holds them to this one.
    /// </summary>
    public const string LeastZod = "4.6.7";

    /// <summary>Emits the module with the default options.</summary>
    /// <returns>The module's text; or, when the document can't be written as TypeScript, every refusal (<see cref="ZodCodes"/>).</returns>
    public static Outcome<string> Emit(IrDocument document) => Emit(document, new ZodOptions());

    /// <summary>Emits the module.</summary>
    /// <returns>
    /// The module's text; or, when the document can't be written as TypeScript (a name that is not an identifier, an import
    /// that clashes), every refusal (<see cref="ZodCodes"/>). The plan refuses before anything is built.
    /// </returns>
    public static Outcome<string> Emit(IrDocument document, ZodOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return ZodPlanner.Plan(document, options).Select(plan => ZodPrinter.Print(ZodLowering.Lower(plan)));
    }

    /// <summary>
    /// Emits the module split into a file per module of <paramref name="modules"/>, with what two or more of them share in one
    /// more file, under <see cref="IrModules.Shared"/>, which each module imports from by <see cref="ZodOptions.SharedModule"/>.
    /// Every name is the one the unsplit module gives
    /// (<c>docs/decisions/0075-what-several-modules-share-is-written-once-in-a-shared-file.md</c>).
    /// </summary>
    /// <returns>Each file's text by its module; or every refusal, as for one module.</returns>
    public static Outcome<EquatableMap<string, string>> Emit(IrDocument document, ZodOptions options, IrModules modules)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modules);
        return ZodPlanner.Plan(document, options)
            .Select(plan => ZodSplit.Lower(plan, modules, options.SharedModule).ToEquatableMap(m => m.Key, m => ZodPrinter.Print(m.Value)));
    }
}

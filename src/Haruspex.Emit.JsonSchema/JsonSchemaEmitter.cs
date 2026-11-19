using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Emit.JsonSchema.Lowering;
using Haruspex.Emit.JsonSchema.Planning;
using Haruspex.Emit.JsonSchema.Printing;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema;

/// <summary>
/// Emits one JSON Schema (draft 2020-12) document from an <see cref="IrDocument"/>: a definition in <c>$defs</c> for every
/// procedure's results and every named object type, by the IR's names. It reads only the IR; it knows nothing about SQL.
/// </summary>
/// <remarks>
/// <para>
/// Three stages, as the Zod emitter has: <see cref="JsonSchemaPlanner"/> decides what is defined and in what order, and refuses a
/// document it can't write; <see cref="JsonSchemaLowering"/> turns each IR type into a small model of schemas; and
/// <see cref="JsonSchemaPrinter"/> writes the model as JSON, and is the only stage that knows its syntax. The guide argues for the
/// split: <c>docs/internals/11-emitting-code.md</c>. How the document is laid out, and how a consumer finds a procedure's schema,
/// is decision 0051.
/// </para>
/// <para>
/// The output is deterministic, with LF line endings, so it can be committed and checked in CI.
/// </para>
/// </remarks>
public static class JsonSchemaEmitter
{
    /// <summary>Emits the document with the default options.</summary>
    /// <returns>The document's text; or, when the IR can't be written as one JSON Schema, every refusal (<see cref="JsonSchemaCodes"/>).</returns>
    public static Outcome<string> Emit(IrDocument document) => Emit(document, new JsonSchemaOptions());

    /// <summary>Emits the document.</summary>
    /// <returns>The document's text; or, when the IR can't be written as one JSON Schema, every refusal (<see cref="JsonSchemaCodes"/>).</returns>
    public static Outcome<string> Emit(IrDocument document, JsonSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return JsonSchemaPlanner.Plan(document, options).Select(plan => JsonSchemaPrinter.Print(JsonSchemaLowering.Lower(plan)));
    }

    /// <summary>
    /// Emits the document split into a document per module of <paramref name="modules"/>, with the named types two or more of them
    /// reach in one more, under <see cref="IrModules.Shared"/>, which the modules refer to by <see cref="JsonSchemaOptions.SharedDocument"/>
    /// (<c>docs/decisions/0075-what-several-modules-share-is-written-once-in-a-shared-file.md</c>).
    /// </summary>
    /// <returns>Each document's text by its module; or every refusal, as for one document.</returns>
    public static Outcome<EquatableMap<string, string>> Emit(IrDocument document, JsonSchemaOptions options, IrModules modules)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modules);
        return JsonSchemaPlanner.Plan(document, options)
            .Select(plan => JsonSchemaLowering.Lower(plan, modules).ToEquatableMap(m => m.Key, m => JsonSchemaPrinter.Print(m.Value)));
    }
}

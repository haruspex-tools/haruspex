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
}

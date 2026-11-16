using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Planning;

/// <summary>
/// What the document defines and in what order, decided before any schema is built: the first of the emitter's three stages
/// (<see cref="JsonSchemaPlanner"/>). A stage's output, handed to the next once: nothing compares two plans.
/// </summary>
/// <param name="document">The document emitted.</param>
/// <param name="options">The emitter's settings.</param>
/// <param name="entries">The definitions, in order.</param>
internal sealed class JsonSchemaPlan(IrDocument document, JsonSchemaOptions options, EquatableArray<JsonSchemaPlanEntry> entries)
{
    /// <summary>The document emitted.</summary>
    public IrDocument Document { get; } = document;

    /// <summary>The emitter's settings.</summary>
    public JsonSchemaOptions Options { get; } = options;

    /// <summary>The definitions, in order.</summary>
    public EquatableArray<JsonSchemaPlanEntry> Entries { get; } = entries;
}

using Haruspex.Collections;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Planning;

/// <summary>
/// What a module declares, in what order and under what names, decided before any schema is built: the first of the
/// emitter's three stages (<see cref="ZodPlanner"/>). A stage's output, handed to the next once: nothing compares two plans.
/// </summary>
/// <param name="document">The document emitted.</param>
/// <param name="graph">Its named types as a graph, which the later stages ask about cycles.</param>
/// <param name="options">The emitter's settings.</param>
/// <param name="imports">The application's schemas the module imports, by module, each module's names sorted.</param>
/// <param name="usesJsonText">Whether any value is JSON text of known content, which needs the module's helper.</param>
/// <param name="entries">The schemas declared, in order.</param>
internal sealed class ZodPlan(
    IrDocument document,
    IrTypeGraph graph,
    ZodOptions options,
    EquatableArray<ZodImport> imports,
    bool usesJsonText,
    EquatableArray<ZodPlanEntry> entries)
{
    /// <summary>The document emitted.</summary>
    public IrDocument Document { get; } = document;

    /// <summary>Its named types as a graph, which the later stages ask about cycles.</summary>
    public IrTypeGraph Graph { get; } = graph;

    /// <summary>The emitter's settings.</summary>
    public ZodOptions Options { get; } = options;

    /// <summary>The application's schemas the module imports, by module, each module's names sorted.</summary>
    public EquatableArray<ZodImport> Imports { get; } = imports;

    /// <summary>Whether any value is JSON text of known content, which needs the module's helper.</summary>
    public bool UsesJsonText { get; } = usesJsonText;

    /// <summary>The schemas declared, in order.</summary>
    public EquatableArray<ZodPlanEntry> Entries { get; } = entries;
}

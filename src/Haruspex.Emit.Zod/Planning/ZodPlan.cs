using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Planning;

/// <summary>
/// What a module declares, in what order and under what names, decided before any schema is built: the first of the
/// emitter's three stages (<see cref="ZodPlanner"/>).
/// </summary>
/// <param name="Document">The document emitted.</param>
/// <param name="Graph">Its named types as a graph, which the later stages ask about cycles.</param>
/// <param name="Options">The emitter's settings.</param>
/// <param name="Imports">The application's schemas the module imports, by module, each module's names sorted.</param>
/// <param name="UsesJsonText">Whether any value is JSON text of known content, which needs the module's helper.</param>
/// <param name="Entries">The schemas declared, in order.</param>
internal sealed record ZodPlan(
    IrDocument Document,
    IrTypeGraph Graph,
    ZodOptions Options,
    EquatableArray<ZodImport> Imports,
    bool UsesJsonText,
    EquatableArray<ZodPlanEntry> Entries);

using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Planning;

/// <summary>
/// The first stage: decides what the document defines and in what order, and refuses a document it can't write before anything
/// is built. It reads only the IR.
/// </summary>
internal static class JsonSchemaPlanner
{
    /// <summary>The plan for <paramref name="document"/>.</summary>
    /// <returns>The plan; or, when one name would define two things, every such refusal.</returns>
    public static Outcome<JsonSchemaPlan> Plan(IrDocument document, JsonSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        // Every named type is defined, whether or not a result reaches it, as the IR's table holds it; and every procedure. Both
        // are in one $defs, sorted by name, so a procedure's schema sits beside the types named after it (GetOrder, GetOrderRow)
        // and the order never depends on the IR's. JSON Schema needs no order for references, recursive ones included.
        var entries = document.Types.Select(t => (JsonSchemaPlanEntry)new JsonSchemaPlanEntry.NamedType(t.Key, t.Value))
            .Concat(document.Objects.Select(o => new JsonSchemaPlanEntry.Procedure(o)))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e is JsonSchemaPlanEntry.Procedure)
            .ThenBy(e => (e as JsonSchemaPlanEntry.Procedure)?.Object.Id, StringComparer.Ordinal)
            .ToList();

        // The config file settles names that never clash, but the IR is a file that anything can write, so a name that would key
        // two definitions is refused rather than one of them dropped.
        var refusals = entries.GroupBy(e => e.Name, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => JsonSchemaCodes.NamedTwice.At(null, $"The IR name '{g.Key}' names {string.Join(" and ", g.Select(Described))}, which one $defs can't hold."))
            .ToList();
        return Outcome.Of(new JsonSchemaPlan(document, options, [.. entries]), refusals);
    }

    private static string Described(JsonSchemaPlanEntry entry) => entry switch
    {
        JsonSchemaPlanEntry.NamedType => "a type",
        JsonSchemaPlanEntry.Procedure procedure => $"the procedure {procedure.Object.Id}",
    };
}

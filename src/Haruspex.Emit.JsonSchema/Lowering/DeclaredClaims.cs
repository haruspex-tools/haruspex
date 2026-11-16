using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// The claims the config file made rather than inference (<see cref="IrDocument.Overrides"/>), as notes at their places: what the
/// database proves and what a person asserts stay distinguishable in the schema, as they are in the IR.
/// </summary>
internal sealed class DeclaredClaims(IrDocument document)
{
    private readonly ILookup<(string Object, string Path), IrOverride> byPlace =
        document.Overrides.ToLookup(o => (o.Object, o.Path));

    /// <summary>A note for each claim declared at <paramref name="place"/>, with the reason the file gives.</summary>
    public IEnumerable<SchemaNote> At(Place? place) =>
        place is null
            ? []
            : byPlace[(place.Object, place.Path)].Select(o =>
                new SchemaNote($"Declared by the config file ({IrWireNames.Of(o.Setting)}{(o.Via is { } via ? $", through {via}" : "")}){(o.Why is { } why ? ": " + why : "")}."));
}

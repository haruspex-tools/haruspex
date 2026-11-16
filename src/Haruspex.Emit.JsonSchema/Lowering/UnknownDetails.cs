using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// What an unknown's schema says: its reason, and the sentence the document's unknowns give for its place, so a reader of the
/// schema learns why without opening the IR.
/// </summary>
internal sealed class UnknownDetails(IrDocument document)
{
    private readonly ILookup<string, IrUnknown> byPath = document.Unknowns.ToLookup(u => u.Path, StringComparer.Ordinal);

    /// <summary>The note for an unknown of <paramref name="reason"/> at <paramref name="place"/>.</summary>
    public SchemaNote Note(IrUnknownReason reason, Place? place)
    {
        var details = At(place).Where(u => u.Reason == reason).Select(u => u.Detail).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var name = IrWireNames.Of(reason);
        return details.Count == 0
            ? new SchemaNote($"Not known ({name}); the IR's unknowns say why.")
            : new SchemaNote($"Not known ({name}): {string.Join(" ", details)}");
    }

    /// <summary>The note for results that can't be listed, from every unknown the document gives for them.</summary>
    public SchemaNote Results(Place place)
    {
        var unknowns = At(place).ToList();
        if (unknowns.Count == 0)
            return new SchemaNote("Its result sets can't be listed; the IR's unknowns say why.");
        var reasons = unknowns.Select(u => IrWireNames.Of(u.Reason)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var details = unknowns.Select(u => u.Detail).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return new SchemaNote($"Its result sets can't be listed ({string.Join(", ", reasons)}): {string.Join(" ", details)}");
    }

    /// <summary>The unknowns at <paramref name="place"/>: of its object, or of any object for a shared type's.</summary>
    private IEnumerable<IrUnknown> At(Place? place) =>
        place is null
            ? []
            : byPath[place.Path].Where(u => place.IsShared || string.Equals(u.Object, place.Object, StringComparison.Ordinal));
}

namespace Haruspex.Ir;

/// <summary>
/// One thing combining two documents (<see cref="IrCombination"/>) has to say: a change it can't bridge, a key that may have
/// been renamed, or an object in only one of them.
/// </summary>
/// <remarks>A value the operation yields, not part of the format: nothing in the IR file is one.</remarks>
/// <param name="Kind">What it says.</param>
/// <param name="Object">The object's id.</param>
/// <param name="Path">Where in the object, as <see cref="IrPath"/> renders it (<c>parameters["@channel"]</c>); empty for the whole object.</param>
/// <param name="What">The change and what to do about it, in words: <c>gained the required parameter @channel, …</c>.</param>
public readonly record struct IrCombinationFinding(IrCombinationKind Kind, string Object, string Path, string What)
{
    /// <summary>The finding as one line: <c>unbridgeable: shop.place_order parameters["@channel"]: gained …</c>.</summary>
    public override string ToString()
    {
        var kind = Kind switch
        {
            IrCombinationKind.Unbridgeable => "unbridgeable",
            IrCombinationKind.PossibleRename => "possible rename",
            IrCombinationKind.OnlyInCandidate => "only in the candidate",
            IrCombinationKind.OnlyInBaseline => "only in the baseline",
        };
        return $"{kind}: {Object}{(Path.Length == 0 ? "" : " " + Path)}: {What}";
    }
}

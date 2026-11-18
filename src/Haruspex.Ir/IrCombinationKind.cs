namespace Haruspex.Ir;

/// <summary>What a finding of combining two documents (<see cref="IrCombination"/>) says.</summary>
public enum IrCombinationKind
{
    /// <summary>
    /// A change no one function bridges, so the object is left out: a new required parameter, one removed that the baseline
    /// requires, types with nothing in common, a result read another way.
    /// </summary>
    Unbridgeable,

    /// <summary>A key went and another appeared in the same object type: perhaps a rename, which the combined type allows neither of.</summary>
    PossibleRename,

    /// <summary>A procedure only the candidate has, which the old database can't run, so it is left out.</summary>
    OnlyInCandidate,

    /// <summary>A procedure only the baseline has, which the new database can't run, so it is left out.</summary>
    OnlyInBaseline,
}

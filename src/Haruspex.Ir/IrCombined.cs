using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>Two documents combined (<see cref="IrCombination"/>): the combined document, and what combining them found.</summary>
/// <remarks>A value the operation yields, not part of the format: the IR file holds the document alone.</remarks>
/// <param name="Document">The combined document, naming the baseline's source; without the objects a finding leaves out.</param>
/// <param name="Findings">What was found, by object id, each object's in the order the combination met them.</param>
public readonly record struct IrCombined(IrDocument Document, EquatableArray<IrCombinationFinding> Findings);

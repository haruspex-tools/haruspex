namespace Haruspex.Ir;

/// <summary>
/// Whether a key is present. Kept apart from nullability because each construct decides it separately:
/// <c>FOR JSON</c> leaves a null key out unless asked to keep it, while a rowset always has every column.
/// </summary>
public enum IrPresence
{
    /// <summary>The key is always there, with a null value when the value is null: every column of a rowset.</summary>
    Always,

    /// <summary>The key is left out when its value is null, as <c>FOR JSON</c> does without <c>INCLUDE_NULL_VALUES</c>.</summary>
    OmittedWhenNull,

    /// <summary>
    /// The key may be left out whatever its value, and when it is there its value is null only for a reason (0.24). Only a
    /// document combining two databases has it (<see cref="IrSource.Baseline"/>): one of the two doesn't return the key, or one
    /// leaves it out when null where the other sends the null.
    /// </summary>
    Optional,
}

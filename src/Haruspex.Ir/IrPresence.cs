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
}

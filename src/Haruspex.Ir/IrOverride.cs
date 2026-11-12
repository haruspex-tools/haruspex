namespace Haruspex.Ir;

/// <summary>A claim the config file made rather than inference: see <see cref="IrDocument.Overrides"/>.</summary>
/// <param name="Object">The id of the object it applies to.</param>
/// <param name="Path">Where in the object, in the IR's path syntax, as an unknown's path is written.</param>
/// <param name="Setting">One of <see cref="IrOverrideSetting"/>.</param>
/// <param name="Why">The reason given in the config file, or null when none was needed.</param>
public sealed record IrOverride(string Object, string Path, IrOverrideSetting Setting, string? Why)
{
    /// <summary>
    /// The procedure whose declaration the claim rests on, when it is not <see cref="Object"/> itself: a caller's results
    /// that include the declared results of a procedure it calls (<see cref="IrOverrideSetting.Results"/>). Null when the
    /// claim is about the object's own declaration.
    /// </summary>
    public string? Via { get; init; }
}

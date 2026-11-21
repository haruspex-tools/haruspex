namespace Haruspex.Ir;

/// <summary>A claim the config file made rather than inference: see <see cref="IrDocument.Overrides"/>.</summary>
/// <param name="Object">The id of the object it applies to.</param>
/// <param name="Path">Where in the object, in the IR's path syntax, as an unknown's path is written.</param>
/// <param name="Setting">One of <see cref="IrOverrideSetting"/>.</param>
/// <param name="Why">The reason given in the config file, or null when none was needed.</param>
public sealed record IrOverride(string Object, string Path, IrOverrideSetting Setting, string? Why)
{
    /// <summary>
    /// What the claim rests on, when it is not <see cref="Object"/>'s own declaration: the procedure whose declared results a
    /// caller's results include (<see cref="IrOverrideSetting.Results"/>), or the column declared to hold JSON of a schema, as
    /// <c>schema.table.column</c>, that the object reads (<see cref="IrOverrideSetting.ColumnJson"/>, 0.28). Null when the claim
    /// is about the object's own declaration.
    /// </summary>
    public string? Via { get; init; }
}

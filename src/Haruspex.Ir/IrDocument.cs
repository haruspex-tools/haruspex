namespace Haruspex.Ir;

// The IR v0 model. It is closed: every type node is one of the records below, so an emitter can handle
// all of it. See README.md in this folder for the format and the reasons behind it.

/// <summary>The inferred shapes of a set of database objects.</summary>
/// <param name="Source">Where the shapes were inferred from.</param>
/// <param name="Objects">The objects typed: procedures, in this version.</param>
/// <param name="Types">The named object types, referenced from <see cref="IrRef"/>.</param>
/// <param name="Unknowns">Everything that could not be typed, with the reason.</param>
public sealed record IrDocument(
    IrSource Source,
    IReadOnlyList<IrObject> Objects,
    IReadOnlyDictionary<string, IrObjectType> Types,
    IReadOnlyList<IrUnknown> Unknowns)
{
    /// <summary>
    /// The IR format version. It is versioned separately from the tool, because the IR is a file that other
    /// programs read: a change that would break a reader changes this.
    /// </summary>
    public const string CurrentVersion = "0.15";

    /// <summary>
    /// The claims made by the config file rather than inferred: a nullability, a type or a value set an override
    /// declared. Empty when nothing was overridden. Kept so that what the database proves and what a person asserts stay
    /// distinguishable in the file, though an emitter needs neither: the types already carry the result.
    /// </summary>
    public IReadOnlyList<IrOverride> Overrides { get; init; } = [];
}

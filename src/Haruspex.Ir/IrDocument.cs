using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>The inferred shapes of a set of database objects.</summary>
/// <remarks>
/// Two documents are equal when the IR's JSON says the same of them: the same source, objects, named types, unknowns, overrides
/// and folds, in any order where the file's order is a sort (<see cref="IrDocumentOrder"/>), with each record compared by
/// value. So a document read back from its file equals the one written, and that is the round trip the format promises.
/// </remarks>
/// <param name="Source">Where the shapes were inferred from.</param>
/// <param name="Objects">The objects typed: procedures and query files.</param>
/// <param name="Types">The named object types, referenced from <see cref="IrRef"/>.</param>
/// <param name="Unknowns">Everything that could not be typed, with the reason.</param>
public sealed record IrDocument(
    IrSource Source,
    EquatableArray<IrObject> Objects,
    IReadOnlyDictionary<string, IrObjectType> Types,
    EquatableArray<IrUnknown> Unknowns)
{
    /// <summary>
    /// The IR format version. It is versioned separately from the tool, because the IR is a file that other
    /// programs read: a change that would break a reader changes this.
    /// </summary>
    public const string CurrentVersion = "0.22";

    /// <summary>
    /// The versions a reader reads, as decision 0024 says: the current one only. 0.22 added query files, and
    /// <see cref="IrObject.ReturnsCode"/>, whether a procedure may end with a code other than 0, which a 0.21 file doesn't say, and
    /// which decides the shape of the procedure's function; so no 0.21 file means what a 0.22 file of its source does.
    /// </summary>
    public static IReadOnlyList<string> ReadableVersions { get; } = [CurrentVersion];

    /// <summary>
    /// The claims made by the config file rather than inferred: a nullability, a type or a value set an override
    /// declared. Empty when nothing was overridden. Kept so that what the database proves and what a person asserts stay
    /// distinguishable in the file, though an emitter needs neither: the types already carry the result.
    /// </summary>
    public EquatableArray<IrOverride> Overrides { get; init; } = [];

    /// <summary>
    /// The folds the canonical value sets name, by name: how each collation the document relies on compares strings (0.20).
    /// Empty when no set is canonical.
    /// </summary>
    public EquatableMap<string, IrFold> Folds { get; init; } = [];

    /// <summary>Whether <paramref name="other"/> says the same, as the remarks above define it.</summary>
    public bool Equals(IrDocument? other) =>
        other is not null
        && Source == other.Source
        && IrDocumentOrder.Objects(this).SequenceEqual(IrDocumentOrder.Objects(other))
        && Types.Count == other.Types.Count
        && Types.All(t => other.Types.TryGetValue(t.Key, out var type) && t.Value == type)
        && IrDocumentOrder.Unknowns(this).SequenceEqual(IrDocumentOrder.Unknowns(other))
        && IrDocumentOrder.Overrides(this).SequenceEqual(IrDocumentOrder.Overrides(other))
        && Folds == other.Folds;

    /// <summary>A hash that ignores order, as equality does: from what the document holds, not where.</summary>
    public override int GetHashCode() =>
        HashCode.Combine(Source, Objects.Count, Types.Count, Unknowns.Count, Overrides.Count);
}

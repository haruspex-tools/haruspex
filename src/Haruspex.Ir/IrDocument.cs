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
    /// The newest IR format version, the one a document that says where a field's value came from is written in (0.25): a
    /// field's source columns (<see cref="IrField.From"/>) and, combined, each database's field (<see cref="IrField.Sides"/>).
    /// The IR is versioned separately from the tool, because it is a file that other programs read: a change that would break a
    /// reader changes this.
    /// </summary>
    public const string CurrentVersion = "0.25";

    /// <summary>
    /// The version a document that combines two databases and says nothing of where its fields came from is written in: 0.24
    /// added only what a combined document says (a source's <see cref="IrSource.Baseline"/>, the presence
    /// <see cref="IrPresence.Optional"/> and the null rule <see cref="IrNullRule.NotInBoth"/>).
    /// Why: docs/decisions/0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md.
    /// </summary>
    public const string CombinedVersion = "0.24";

    /// <summary>
    /// The version a document of one database that says nothing of where its fields came from is written in: a 0.24 or 0.25
    /// document without what those added means what it meant in 0.23, and is labelled so, so that a 0.23 reader still reads it.
    /// Why: docs/decisions/0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md.
    /// </summary>
    public const string OneDatabaseVersion = "0.23";

    /// <summary>
    /// The versions a reader reads, as decision 0024 says: the current one, and 0.24 and 0.23, every document of which is a 0.25
    /// document that says nothing of where its fields came from, meaning the same. 0.23 added a result set's name
    /// (<see cref="IrResult.Name"/>), which decides the shape of the procedure's function, and whether a type's name was declared
    /// (<see cref="IrObjectType.NameDeclared"/>), which a 0.22 file doesn't say; so no 0.22 file means what a 0.23 file of its
    /// source does.
    /// </summary>
    public static IReadOnlyList<string> ReadableVersions { get; } = [CurrentVersion, CombinedVersion, OneDatabaseVersion];

    /// <summary>
    /// The version this document is written in, the oldest whose reader reads all it says (decision 0072's labelling by content):
    /// <see cref="CurrentVersion"/> when a field says where its value came from, which a document inferred from SQL almost always
    /// does; else <see cref="CombinedVersion"/> when it combines two databases; else <see cref="OneDatabaseVersion"/>.
    /// Why: docs/decisions/0078-the-ir-says-where-each-field-came-from.md.
    /// </summary>
    public string Version =>
        Types.Values.Any(t => t.Fields.Any(f => f.From.Count > 0 || f.Sides is not null)) || Source.Files.Count > 0 || Source.Baseline?.Files.Count > 0
            ? CurrentVersion
        : Source.Baseline is null ? OneDatabaseVersion
        : CombinedVersion;

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

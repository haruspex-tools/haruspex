using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>The inferred shapes of a set of database objects.</summary>
/// <remarks>
/// Two documents are equal when the IR's JSON says the same of them: the same source, objects, named types, unknowns, overrides,
/// folds and table types, in any order where the file's order is a sort (<see cref="IrDocumentOrder"/>), with each record compared by
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
    /// The newest IR format version, the one a document that defines a table type is written in (<see cref="TableTypes"/>). The
    /// IR is versioned separately from the tool, because it is a file that other programs read: a change that would break a
    /// reader changes this.
    /// </summary>
    public const string CurrentVersion = "0.25";

    /// <summary>
    /// The version a document that combines two databases and defines no table type is written in: 0.24 added only what a
    /// combined document says (a source's <see cref="IrSource.Baseline"/>, the presence <see cref="IrPresence.Optional"/> and the
    /// null rule <see cref="IrNullRule.NotInBoth"/>).
    /// Why: docs/decisions/0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md.
    /// </summary>
    public const string CombinedVersion = "0.24";

    /// <summary>
    /// The version a document of one database is written in: 0.24 added only what a combined document says (a source's
    /// <see cref="IrSource.Baseline"/>, the presence <see cref="IrPresence.Optional"/> and the null rule
    /// <see cref="IrNullRule.NotInBoth"/>), so a document without them is a 0.23 document that means the same, and is labelled
    /// so: a run without a baseline writes what it wrote before, and a 0.23 reader still reads it.
    /// Why: docs/decisions/0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md.
    /// </summary>
    public const string OneDatabaseVersion = "0.23";

    /// <summary>
    /// The versions a reader reads, as decision 0024 says: the current one, 0.24 and 0.23, every document of each of which is a
    /// 0.25 document without a table type that means the same. 0.23 added a result set's name (<see cref="IrResult.Name"/>),
    /// which decides the shape of the procedure's function, and whether a type's name was declared
    /// (<see cref="IrObjectType.NameDeclared"/>), which a 0.22 file doesn't say; so no 0.22 file means what a 0.23 file of its
    /// source does.
    /// </summary>
    public static IReadOnlyList<string> ReadableVersions { get; } = [CurrentVersion, CombinedVersion, OneDatabaseVersion];

    /// <summary>
    /// The version this document is written in, the oldest whose reader reads all it says: <see cref="CurrentVersion"/> when it
    /// defines a table type, <see cref="CombinedVersion"/> when it combines two databases, and <see cref="OneDatabaseVersion"/>
    /// otherwise, so that a run that uses neither writes what it wrote before.
    /// Why: docs/decisions/0077-the-ir-carries-a-table-type-s-definition.md.
    /// </summary>
    public string Version => TableTypes.Count > 0 ? CurrentVersion : Source.Baseline is null ? OneDatabaseVersion : CombinedVersion;

    /// <summary>
    /// The table types the document's table-valued parameters take, by the key a parameter names (<see cref="IrParameter.TableType"/>),
    /// the type's name as the source writes it (<c>sales.line_list</c>) (0.25). Empty when no parameter is of one.
    /// </summary>
    public EquatableMap<string, IrTableType> TableTypes { get; init; } = [];

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
        && Folds == other.Folds
        && TableTypes == other.TableTypes;

    /// <summary>A hash that ignores order, as equality does: from what the document holds, not where.</summary>
    public override int GetHashCode() =>
        HashCode.Combine(Source, Objects.Count, Types.Count, Unknowns.Count, Overrides.Count);
}

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
    /// The newest IR format version, the one a document that records packages the config file declared builds is written in (0.31,
    /// <see cref="IrSource.Builds"/>). The IR is versioned separately from the tool, because it is a file that other programs read:
    /// a change that would break a reader changes this. Why: docs/decisions/0122-the-ir-records-the-packages-declared-builds.md.
    /// </summary>
    public const string CurrentVersion = "0.31";

    /// <summary>
    /// The version a document that declares no build but names the build of the tool that wrote it is written in (0.30,
    /// <see cref="IrSource.WrittenBy"/>), which is every other document haruspex writes. A version says which properties a document
    /// uses; the build it names says whose rules made its claims, since a fix to a rule may change a claim without changing the
    /// format. Why: docs/decisions/0117-an-ir-document-names-the-build-that-made-its-claims.md.
    /// </summary>
    public const string StampedVersion = "0.30";

    /// <summary>
    /// The version a document that names no build but explains a null reason, or counts a minimum length in code points, is
    /// written in (0.29): one in which a reason says, in words, what makes it so (<see cref="IrNullReason.Detail"/>), as a value
    /// read from JSON at a path does, or a minimum's unit is code points (<see cref="IrMinLength.Unit"/>).
    /// Why: docs/decisions/0097-a-path-into-json-of-known-shape-is-walked-step-by-step.md.
    /// </summary>
    public const string ExplainedVersion = "0.29";

    /// <summary>
    /// The version a document that explains no reason but declares a column's JSON is written in (0.28): one that lists a
    /// declaration in <see cref="Columns"/>, or holds what only a declaration gives, any JSON (<see cref="IrAnyJson"/>) or a fact
    /// a declaration states (<see cref="IrFactSourceKind.Declared"/>).
    /// Why: docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md.
    /// </summary>
    public const string DeclaredVersion = "0.28";

    /// <summary>
    /// The version a document that declares nothing but holds a binary value is written in (0.27, a scalar of kind
    /// <see cref="IrScalarKind.Bytes"/>). Why: docs/decisions/0081-binary-values-are-a-kind-of-their-own-counted-in-bytes.md.
    /// </summary>
    public const string BytesVersion = "0.27";

    /// <summary>
    /// The version a document that holds no binary value but defines a table type is written in (0.26, <see cref="TableTypes"/>).
    /// Why: docs/decisions/0077-the-ir-carries-a-table-type-s-definition.md.
    /// </summary>
    public const string TableTypesVersion = "0.26";

    /// <summary>
    /// The version a document that defines no table type but says where a field's value came from is written in (0.25): a
    /// field's source columns (<see cref="IrField.From"/>) and, combined, each database's field (<see cref="IrField.Sides"/>).
    /// Why: docs/decisions/0078-the-ir-says-where-each-field-came-from.md.
    /// </summary>
    public const string SourcesVersion = "0.25";

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
    /// The versions a reader reads, newest first: the current one, and each older one every document of which is a current document
    /// that means the same (0.30 back to 0.23, each a document that says nothing a later version added). Any other is refused.
    /// Why: docs/decisions/0024-an-ir-reader-reads-only-versions-whose-documents-mean-what-its-own-do.md, which says why 0.22 is not one.
    /// </summary>
    public static IReadOnlyList<string> ReadableVersions { get; } = [CurrentVersion, StampedVersion, ExplainedVersion, DeclaredVersion, BytesVersion, TableTypesVersion, SourcesVersion, CombinedVersion, OneDatabaseVersion];

    /// <summary>
    /// Whether <paramref name="version"/> is <paramref name="since"/> or later, both of <see cref="ReadableVersions"/>: by their order
    /// there, newest first, since a version's text doesn't sort (0.3 is before 0.23).
    /// </summary>
    internal static bool IsSince(string version, string since) => IndexOf(version) <= IndexOf(since);

    private static int IndexOf(string version) =>
        ReadableVersions.Select((v, i) => (v, i)).Single(p => string.Equals(p.v, version, StringComparison.Ordinal)).i;

    /// <summary>
    /// The version this document is written in, the oldest whose reader reads all it says (decision 0072's labelling by content):
    /// <see cref="CurrentVersion"/> when it records a package declared a build (<see cref="DeclaresBuilds"/>); else
    /// <see cref="StampedVersion"/> when it names the build that wrote it (<see cref="NamesItsWriter"/>); else
    /// <see cref="ExplainedVersion"/> when it explains a reason (<see cref="Explains"/>); else <see cref="DeclaredVersion"/> when it
    /// declares a column's JSON (<see cref="Declares"/>); else <see cref="BytesVersion"/> when it
    /// holds a binary value (<see cref="HoldsBytes"/>); else <see cref="TableTypesVersion"/>
    /// when it defines a table type; else <see cref="SourcesVersion"/> when a field says where its
    /// value came from, which a document inferred from SQL almost always does; else <see cref="CombinedVersion"/> when it combines
    /// two databases; else <see cref="OneDatabaseVersion"/>.
    /// Why: docs/decisions/0078-the-ir-says-where-each-field-came-from.md and
    /// docs/decisions/0077-the-ir-carries-a-table-type-s-definition.md.
    /// </summary>
    public string Version =>
        DeclaresBuilds ? CurrentVersion
        : NamesItsWriter ? StampedVersion
        : Explains ? ExplainedVersion
        : Declares ? DeclaredVersion
        : HoldsBytes ? BytesVersion
        : TableTypes.Count > 0 ? TableTypesVersion
        : SaysSources ? SourcesVersion
        : Source.Baseline is null ? OneDatabaseVersion
        : CombinedVersion;

    /// <summary>
    /// Whether it says anything only a declaration of a column's JSON gives (0.28): a declaration in <see cref="Columns"/>, any
    /// JSON (<see cref="IrAnyJson"/>) anywhere, a fact a declaration states (<see cref="IrFactSourceKind.Declared"/>), or an open
    /// object type (<see cref="IrObjectType.Open"/>).
    /// </summary>
    internal bool Declares => Columns.Count > 0 || IrDocumentDeclared.First(this) is not null;

    /// <summary>
    /// Whether it says what 0.25 added: where a field's value came from (<see cref="IrField.From"/>, <see cref="IrField.Sides"/>), or
    /// where a source was read (<see cref="IrSource.Path"/>, <see cref="IrSource.Files"/>), its own or its baseline's.
    /// </summary>
    internal bool SaysSources =>
        Types.Values.Any(t => t.Fields.Any(f => f.From.Count > 0 || f.Sides is not null)) || Source.Files.Count > 0 || Source.Baseline?.Files.Count > 0
        || Source.Path is not null || Source.Baseline?.Path is not null;

    /// <summary>
    /// Whether it names the build that wrote it, or its baseline's (0.30, <see cref="IrSource.WrittenBy"/>). A document that names
    /// its baseline's writer names its own too, which the writer holds it to.
    /// </summary>
    internal bool NamesItsWriter => Source.WrittenBy is not null || Source.Baseline?.WrittenBy is not null;

    /// <summary>
    /// Whether it records a package the config file declared a build (0.31, <see cref="IrSource.Builds"/>), its own source's or its
    /// baseline's.
    /// </summary>
    internal bool DeclaresBuilds => Source.Builds.Count > 0 || Source.Baseline?.Builds.Count > 0;

    /// <summary>Whether it says what only 0.29 says: a <see cref="IrNullReason.Detail"/>, or a minimum length in code points, anywhere.</summary>
    internal bool Explains => IrDocumentExplained.First(this) is not null;

    /// <summary>Whether any of its values is a binary one (0.27): a scalar of kind <see cref="IrScalarKind.Bytes"/> anywhere.</summary>
    internal bool HoldsBytes => IrDocumentScalars.Of(this).Any(s => s.Scalar.Kind == IrScalarKind.Bytes);

    /// <summary>
    /// The table types the document's table-valued parameters take, by the key a parameter names (<see cref="IrParameter.TableType"/>),
    /// the type's name as the source writes it (<c>sales.line_list</c>) (0.26). Empty when no parameter is of one.
    /// </summary>
    public EquatableMap<string, IrTableType> TableTypes { get; init; } = [];

    /// <summary>
    /// The columns the config file declares to hold JSON of a schema, each with the files it read (0.28), sorted by column. Empty
    /// when none is declared.
    /// </summary>
    public EquatableArray<IrColumnDeclaration> Columns { get; init; } = [];

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
        && TableTypes == other.TableTypes
        && IrDocumentOrder.Columns(this).SequenceEqual(IrDocumentOrder.Columns(other));

    /// <summary>A hash that ignores order, as equality does: from what the document holds, not where.</summary>
    public override int GetHashCode() =>
        HashCode.Combine(Source, Objects.Count, Types.Count, Unknowns.Count, Overrides.Count);
}

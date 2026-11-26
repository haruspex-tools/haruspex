using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// Where the shapes were inferred from: the dialect, the package they were read from when there is one, and what the callers'
/// sessions are declared to set and which packages are declared builds; and, for a document combining two databases, where the other was read from.
/// </summary>
/// <param name="Dialect">The SQL dialect of the source, such as <c>tsql</c>.</param>
public sealed record IrSource(string Dialect)
{
    /// <summary>
    /// The options the callers' sessions are declared to set, sorted by name (0.17). Empty when none is declared: then every
    /// claim holds whatever the callers set, as it did in every version before 0.17, and the file leaves <c>session</c> out.
    /// </summary>
    public EquatableArray<IrSessionOption> Session { get; init; } = [];

    /// <summary>
    /// The package the source was read from, by its file name (<c>sales.dacpac</c>), with its SHA-256 (0.19); null for a source
    /// that is not a package, such as a folder of scripts, and then the file leaves <c>package</c> and <c>sha256</c> out.
    /// </summary>
    public IrPackage? Package { get; init; }

    /// <summary>
    /// The build of the tool that wrote the claims read from this source (0.30): for a document's own source, the build that wrote
    /// the document; for its baseline, the build that wrote the baseline's claims, which is another when the baseline was read from
    /// an IR file. Null when it isn't known: in a document of a version before 0.30, which names none, and for a baseline read from
    /// one. Every document of 0.30 names its own. Where the claims came from, not a claim, so comparing two documents' claims
    /// (<see cref="IrComparison"/>) leaves it out.
    /// Why: docs/decisions/0117-an-ir-document-names-the-build-that-made-its-claims.md.
    /// </summary>
    public IrBuild? WrittenBy { get; init; }

    /// <summary>
    /// The packages <see cref="Package"/> refers to, or a project file and the projects read with it refer to, whether found or
    /// not (0.19), held in the file's order (by name, then database, the same database first) whatever order they are given in, so
    /// that two sources that say the same are equal.
    /// Empty when there are none, and then the file leaves <c>references</c> out.
    /// </summary>
    public EquatableArray<IrPackageReference> References
    {
        get;
        init => field = [.. (value ?? throw new ArgumentNullException(nameof(value)))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Database is not null)
            .ThenBy(r => r.Database, StringComparer.Ordinal)];
    } = [];

    /// <summary>
    /// The packages read for this source that the config file declared builds (<c>builds</c>), by their file names as
    /// <see cref="Package"/> and <see cref="References"/> name them, sorted (0.31): packages whose primary keys and unique constraints
    /// are claimed on the user's word, as a declared session's options are, though nothing in them says what made them. Empty when
    /// none is declared, or when no package declared one was read, and then the file leaves <c>builds</c> out. A package that is a
    /// build by its origin is listed too when declared one: the declaration is recorded, whether or not it was needed. An assumption
    /// the claims rest on, so comparing two documents (<see cref="IrComparison"/>) reports it changing.
    /// Why: docs/decisions/0122-the-ir-records-the-packages-declared-builds.md.
    /// </summary>
    public EquatableArray<string> Builds
    {
        get;
        init => field = [.. (value ?? throw new ArgumentNullException(nameof(value))).Order(StringComparer.Ordinal)];
    } = [];

    /// <summary>
    /// Where the source was read from, as the config file names it (0.25): relative to the config file's folder, with <c>/</c>
    /// between its parts, so that it is the same on every machine (<c>database</c>, <c>../db/Shop.sqlproj</c>,
    /// <c>release/Shop.dacpac</c>). What <c>haruspex explain</c> labels each side of a combined field with. Null when it isn't
    /// known, as for a document built by hand, and then the file leaves <c>path</c> out. Where the claims came from, not a claim.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// The SHA-256 of each script file the document's locations point into, by its path as a location writes it (0.25): so that a
    /// reader of a location (<c>haruspex explain</c>) can tell whether the file in front of it is the one the location was written
    /// from, as a package's <see cref="Package"/> hash lets it tell a package. Each is of the file's text with its line endings made
    /// LF, in UTF-8, so that a checkout that writes CRLF hashes the same. Empty for a document that points into no script, such as
    /// one read from a package, and then the file leaves <c>files</c> out. Where a claim came from, not a claim.
    /// </summary>
    public EquatableMap<string, string> Files { get; init; } = [];

    /// <summary>
    /// For a document that combines two databases (0.24), the source of the other: the baseline, the database the application
    /// meets before a change is deployed, whose claims the document's hold for as well as its own source's. Null for a document
    /// of one database, which is every document but one <c>haruspex generate</c> writes with a baseline, and then the file leaves
    /// <c>baseline</c> out. A baseline is itself the source of one database, so it has none of its own.
    /// Why: docs/decisions/0072-a-document-written-with-a-baseline-is-the-combined-one-and-says-so.md.
    /// </summary>
    public IrSource? Baseline
    {
        get;
        init => field = value?.Baseline is null ? value : throw new ArgumentException("A baseline is the source of one database, so it has no baseline of its own.", nameof(value));
    }
}

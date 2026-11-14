using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// Where the shapes were inferred from: the dialect, the package they were read from when there is one, and what the callers'
/// sessions are declared to set.
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
    /// The packages <see cref="Package"/> refers to, whether found or not (0.19), held in the file's order (by name, then
    /// database, the same database first) whatever order they are given in, so that two sources that say the same are equal.
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
}

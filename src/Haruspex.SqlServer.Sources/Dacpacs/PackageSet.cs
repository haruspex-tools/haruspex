using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// The DACPACs one read opens, and every reference to one it meets: what reading a DACPAC and reading a project share. A package is
/// opened once, whatever refers to it, and read into each database that holds it; each reference is recorded once, found or not,
/// for the IR's source record.
/// </summary>
/// <remarks>
/// A package's own references are followed as decision 0040 says: one of the same database is read into the same model, breadth
/// first; one of another database is handed back by the name the reference gives the database, to be read into a model of its own
/// (decision 0042); one of a database on another server is noted and not read (<see cref="SourceCodes.OtherServer"/>), since a body
/// reaches it only by a four-part name, which nothing binds.
/// </remarks>
/// <param name="folders">The config file's <c>references</c> folders, full paths, where a package not beside its referrer is looked for.</param>
/// <param name="diagnostics">Where a package not found or not readable, or a reference not read, is reported.</param>
/// <param name="builds">The config file's <c>builds</c>: the file names of the packages declared built from a project.</param>
internal sealed class PackageSet(IReadOnlyList<string> folders, List<Diagnostic> diagnostics, IReadOnlyCollection<string> builds)
{
    private readonly Dictionary<(string Name, string? Database), DacPackage?> opened = [];
    private readonly Dictionary<(string Name, string? Database), ReferenceRead> seen = [];
    private readonly Dictionary<string, DacPackage?> byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadedPackage> readAsBuilds = [];

    /// <summary>Every reference met, each once, in the order met.</summary>
    public IReadOnlyList<ReferenceRead> References => [.. seen.Values];

    /// <summary>
    /// Every package read whose key constraints were taken as enforced because the config file's <c>builds</c> names it, each once,
    /// in the order read: what the IR's source record says was declared a build. A package <c>builds</c> names that wasn't read,
    /// found or not, isn't here, since nothing was assumed of it.
    /// </summary>
    public IReadOnlyList<LoadedPackage> ReadAsBuilds => readAsBuilds;

    /// <summary>The config file's <c>references</c> folders.</summary>
    public IReadOnlyList<string> Folders { get; } = folders;

    /// <summary>The package at <paramref name="path"/>, opened once however often it is asked for; null, reported, when it can't be read.</summary>
    public DacPackage? Open(string path)
    {
        var full = Path.GetFullPath(path);
        if (!byPath.TryGetValue(full, out var package))
            byPath[full] = package = DacPackage.Open(full, diagnostics);
        return package;
    }

    /// <summary>
    /// Records a reference a project makes to a package (<paramref name="found"/>, or null when it wasn't), once by its file name and
    /// database, as a package's own references are; and makes the package what a package's reference of that name and database
    /// opens, so that it is read once.
    /// </summary>
    /// <param name="name">Its file name.</param>
    /// <param name="database">Null for the same database; otherwise the database as the reference names it.</param>
    /// <param name="found">The package, opened.</param>
    public void Record(string name, string? database, DacPackage? found)
    {
        var key = (name.ToUpperInvariant(), database);
        opened.TryAdd(key, found);
        seen.TryAdd(key, new ReferenceRead(name, database, found is null ? null : new LoadedPackage(found.Name, found.Path, found.Sha256)));
    }

    /// <summary>
    /// Reads <paramref name="roots"/>, and every package of their database reachable through their references, breadth first, each
    /// once by its file name, into <paramref name="into"/>. A found package of another database is added to <paramref name="others"/>
    /// by the name its reference gives the database, the first for a name winning; with no <paramref name="others"/> (a database read
    /// one level down) it is only recorded.
    /// </summary>
    /// <param name="into">What the database's sources declare.</param>
    /// <param name="roots">The packages of the database to start from.</param>
    /// <param name="others">Where packages of other databases go, or null.</param>
    /// <param name="quiet">Whether to leave out each package's note of what isn't modelled (for <c>master</c>).</param>
    /// <param name="built">
    /// The file names of the packages the database's projects, read from their scripts, would build: a reference to one of them
    /// is to that project, already read, as a build takes it (a referenced project's package is named for its project file,
    /// observed). Empty for a database read from packages alone.
    /// </param>
    /// <returns>What the database lacks: a package of it referred to and not found.</returns>
    public List<UnreadPart> ReadInto(SchemaDeclarations into, IReadOnlyList<DacPackage> roots, Dictionary<string, DacPackage>? others, bool quiet = false, IReadOnlyCollection<string>? built = null)
    {
        var database = new List<DacPackage>(roots);
        var unread = new List<UnreadPart>();
        for (var i = 0; i < database.Count; i++)
        {
            var referrer = database[i];
            foreach (var reference in referrer.References)
            {
                if (reference.Database is null && built is not null && built.Contains(reference.LogicalName, StringComparer.OrdinalIgnoreCase))
                    continue;
                Follow(reference, referrer, database, unread, others);
            }
        }

        foreach (var package in database)
        {
            var declared = builds.Contains(package.Name, StringComparer.OrdinalIgnoreCase);
            if (declared && !readAsBuilds.Exists(p => p.Path == package.Path))
                readAsBuilds.Add(new LoadedPackage(package.Name, package.Path, package.Sha256));
            PackageReader.Read(package, into, quiet, declared);
        }

        return unread;
    }

    private void Follow(PackageReference reference, DacPackage referrer, List<DacPackage> database, List<UnreadPart> unread, Dictionary<string, DacPackage>? others)
    {
        if (reference.Server is { } server)
        {
            var note = SourceCodes.OtherServer.At(
                new FileLocation(referrer.Path),
                $"{referrer.Name} refers to {reference.LogicalName}, the database {reference.Database} on the server {server}, which isn't read: a name in it, which a body writes in four parts, stays unknown.");
            if (!diagnostics.Contains(note))
                diagnostics.Add(note);
            return;
        }

        if (reference.Database is null && database.Any(p => p.Name.Equals(reference.LogicalName, StringComparison.OrdinalIgnoreCase)))
            return;
        var key = (reference.LogicalName.ToUpperInvariant(), reference.Database);
        if (!opened.TryGetValue(key, out var found))
        {
            found = Find(reference, referrer);
            opened[key] = found;
        }

        seen.TryAdd(key, new ReferenceRead(reference.LogicalName, reference.Database, found is null ? null : new LoadedPackage(found.Name, found.Path, found.Sha256)));
        if (reference.Database is null)
        {
            if (found is not null && !database.Contains(found))
                database.Add(found);
            else if (found is null)
                unread.Add(new UnreadPart(reference.LogicalName, referrer.Name));
        }
        else if (found is not null && others is not null)
        {
            others.TryAdd(reference.Database, found);
        }
    }

    /// <summary>The referenced package, opened; or null, with a warning naming every place looked, when there is none.</summary>
    private DacPackage? Find(PackageReference reference, DacPackage referrer)
    {
        if (ReferenceFinder.Find(reference, referrer.Path, Folders) is { } path)
            return Open(path);
        var which = reference.Database is null ? "a package of the same database" : $"the package of the database {reference.Database}";
        var places = string.Join(", ", ReferenceFinder.Shown(reference, referrer.Path, Folders));
        diagnostics.Add(SourceCodes.ReferenceNotFound.At(
            new FileLocation(referrer.Path),
            $"{referrer.Name} refers to {reference.LogicalName}, {which}, which isn't found: looked for at {places}. Put it beside {referrer.Name}, or name its folder in \"references\" in the config file.",
            reference.LogicalName));
        return null;
    }
}

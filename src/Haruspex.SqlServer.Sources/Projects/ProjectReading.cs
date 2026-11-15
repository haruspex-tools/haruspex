using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// One read of a project, with what it refers to: the state that runs through it (the projects read, the packages opened, what is
/// missing, the targets met), kept in one place so that a project or a package referred to many times is read and reported once.
/// </summary>
/// <remarks>
/// A database is read from a root project (<see cref="Database"/>): it and the projects of its database it refers to, transitively,
/// each once, their scripts as a folder's are; then the DACPACs of its database they refer to, with the packages of the same
/// database those refer to, as a DACPAC's are (<see cref="PackageSet"/>). What a project, or such a package, refers to of another
/// database is read into a model of its own the same way, one level down, as decision 0042 reads one from a DACPAC.
/// </remarks>
internal sealed class ProjectReading
{
    private readonly string folder;
    private readonly IReadOnlyList<string> searchFolders;
    private readonly PackageCache cache;

    private readonly List<Diagnostic> diagnostics = [];
    private readonly List<string> projects = [];
    private readonly Dictionary<string, (DatabaseReference Reference, string? Problem, List<string> Referrers)> missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (SourceLocation At, int Projects)> targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Where, string? Database), DacPackage?> found = [];

    /// <summary>The DACPACs the read opens, and every reference to one it meets.</summary>
    private readonly PackageSet packages;

    /// <summary>A read of the project in <paramref name="folder"/>.</summary>
    /// <param name="folder">The given project's folder, which every location is relative to.</param>
    /// <param name="searchFolders">The config file's <c>references</c> folders, full paths.</param>
    /// <param name="cache">Where packages are looked for.</param>
    public ProjectReading(string folder, IReadOnlyList<string> searchFolders, PackageCache cache)
    {
        this.folder = folder;
        this.searchFolders = searchFolders;
        this.cache = cache;
        packages = new PackageSet(searchFolders, diagnostics);
    }

    /// <summary>Reads the project at <paramref name="root"/>, the database it is part of and the databases it refers to.</summary>
    public SqlProjectReadResult Read(string root)
    {
        var others = new Dictionary<string, Other>(StringComparer.OrdinalIgnoreCase);
        var own = Database(root, null, null, others);
        // A database whose project wasn't read is left out, as a package not found is: a name in it is unknown as without the reference.
        var databases = new Dictionary<string, DatabaseModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, other) in others)
        {
            if (OtherDatabase(name, other) is { } model)
                databases[name] = model;
        }

        Report();
        return new SqlProjectReadResult(own.ToModel(databases), [.. projects], [.. packages.References], [.. diagnostics.Distinct()]);
    }

    /// <summary>
    /// One database: the project <paramref name="root"/> names, the projects of its database it refers to, and the packages of its
    /// database they refer to. Another database they refer to is added to <paramref name="others"/> by the name a body writes it
    /// by, the first for a name winning; with no <paramref name="others"/>, as for a database read one level down, it is passed
    /// over, and a package of it only found and recorded.
    /// </summary>
    private DatabaseRead Database(string root, string? rootReferrer, DatabaseReference? rootReference, Dictionary<string, Other>? others)
    {
        var queue = new List<(string Path, string? Referrer, DatabaseReference? Reference)> { (root, rootReferrer, rootReference) };
        var scripts = new List<ScriptFile>();
        var roots = new List<DacPackage>();
        var unread = new List<UnreadPart>();
        var absent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var read = new List<string>();
        DatabaseSettings? settings = null;
        string? collation = null;
        var rooted = false;
        for (var i = 0; i < queue.Count; i++)
        {
            var (next, referrer, reference) = queue[i];
            var located = DiskPath.Locate(next, reference?.FullPath ?? next, out var problem);
            if (located is not null && read.Contains(located, StringComparer.OrdinalIgnoreCase))
                continue;
            if (located is null && referrer is not null && reference is not null)
            {
                Missing(next, reference, problem, referrer);
                if (!absent.TryGetValue(next, out var referrers))
                    absent[next] = referrers = [];
                if (!referrers.Contains(referrer, StringComparer.OrdinalIgnoreCase))
                    referrers.Add(referrer);
                continue;
            }

            located ??= next;
            read.Add(located);
            projects.Add(located);
            if (Project(located) is not { } project)
                continue;
            if (i == 0)
            {
                rooted = true;
                settings = project.Settings;
                collation = project.Collation;
            }

            scripts.AddRange(project.Scripts.Select(s => new ScriptFile(s.Path, Path.GetRelativePath(folder, s.Path).Replace('\\', '/'), s.Settings)));
            var name = Path.GetFileName(located);
            foreach (var each in project.References)
            {
                if (each.Kind == DatabaseReferenceKind.Project && each.Database is null)
                    queue.Add((each.FullPath ?? each.Described, name, each));
                else if (each is { Kind: DatabaseReferenceKind.Project, Database: { } database })
                    others?.TryAdd(database, new Other(each.FullPath ?? each.Described, name, each, null));
                else
                    Package(each, name, roots, unread, others);
            }
        }

        // A project of this database not found, once however many refer to it; one of another database leaves that database out.
        if (rootReference?.Database is null)
            unread.AddRange(absent.Select(a => new UnreadPart(Path.GetFileName(a.Key), string.Join(", ", a.Value))));

        // In order of their relative paths, as a folder's are, so the outcome doesn't depend on the order of the items; a script two
        // projects build is read twice, and what it defines is defined twice, as in their packages. The packages of the database
        // come after, read as a DACPAC's are.
        var declarations = new SchemaDeclarations();
        SqlScriptFolder.ReadInto(declarations, scripts.OrderBy(s => s.Relative, StringComparer.Ordinal));
        var ofOthers = others is null ? null : new Dictionary<string, DacPackage>(StringComparer.OrdinalIgnoreCase);
        unread.AddRange(packages.ReadInto(declarations, [.. roots.Distinct()], ofOthers));
        foreach (var (database, package) in ofOthers ?? [])
            others?.TryAdd(database, new Other(null, null, null, package));
        diagnostics.AddRange(declarations.Diagnostics);
        return new DatabaseRead(declarations, settings, collation, unread, rooted);
    }

    /// <summary>
    /// A package a project of the database refers to, found: one of the database among <paramref name="roots"/>, one of another
    /// among <paramref name="others"/> (when they are read); one of the database not found among what it lacks.
    /// </summary>
    private void Package(DatabaseReference reference, string referrer, List<DacPackage> roots, List<UnreadPart> unread, Dictionary<string, Other>? others)
    {
        var package = Find(reference, referrer);
        if (reference.Database is not { } database)
        {
            if (package is null)
                unread.Add(new UnreadPart(reference.FileName, referrer));
            else
                roots.Add(package);
        }
        else if (package is not null)
        {
            others?.TryAdd(database, new Other(null, null, null, package));
        }
    }

    /// <summary>
    /// Another database's model: from its package, as a DACPAC's is, or from its project, one level down; null when its project
    /// isn't there or can't be read.
    /// </summary>
    private DatabaseModel? OtherDatabase(string name, Other other) =>
        other.Package is { } package ? DacpacReader.OtherDatabase(name, package, packages, diagnostics)
        : other.Project is { } project && Database(project, other.Referrer, other.Reference, null) is { Rooted: true } read ? read.ToModel()
        : null;

    /// <summary>
    /// The package a reference names, opened once however many projects make it: where its build takes it from, or else by its
    /// file name in a <c>references</c> folder of the config file. Null, with one warning naming every place looked, when there is none.
    /// </summary>
    private DacPackage? Find(DatabaseReference reference, string referrer)
    {
        var key = (reference.FullPath ?? reference.FileName, reference.Database);
        if (found.TryGetValue(key, out var known))
            return known;
        var places = new List<string>();
        if (reference.FullPath is { } path)
            places.Add(path);
        places.AddRange(searchFolders.Select(f => Path.Join(f, reference.FileName)));
        var at = places.Select(p => DiskPath.Locate(p, p, out _)).FirstOrDefault(p => p is not null && File.Exists(p));
        var package = at is null ? null : packages.Open(at);
        found[key] = package;
        packages.Record(package?.Name ?? reference.FileName, reference.Database, package);
        if (at is null)
        {
            var which = reference.Database is null ? "of its own database" : $"the database {reference.Database}";
            var looked = places.Count == 0 ? "" : $": looked for at {string.Join(", ", places)}";
            diagnostics.Add(SourceCodes.ReferenceNotFound.At(reference.At, $"{referrer} refers to {reference.Described}, {which}, which isn't found{looked}. {Remedy(reference)}"));
        }

        return package;
    }

    /// <summary>What to do about a reference not found, by what it names.</summary>
    private static string Remedy(DatabaseReference reference) => reference.Kind switch
    {
        DatabaseReferenceKind.Package => $"Restore the project, or name a folder holding {reference.FileName} in \"references\" in the config file.",
        DatabaseReferenceKind.VisualStudio => $"Only a Visual Studio install has it: name a folder holding {reference.FileName} in \"references\" in the config file.",
        DatabaseReferenceKind.Artifact or DatabaseReferenceKind.Project => $"Put it there, or name a folder holding {reference.FileName} in \"references\" in the config file.",
        _ => throw new ArgumentOutOfRangeException(nameof(reference)),
    };

    /// <summary>A project referred to and not found: once for the project, however many refer to it, naming each.</summary>
    private void Missing(string path, DatabaseReference reference, string? problem, string referrer)
    {
        if (!missing.TryGetValue(path, out var known))
            missing[path] = known = (reference, problem, []);
        if (!known.Referrers.Contains(referrer, StringComparer.OrdinalIgnoreCase))
            known.Referrers.Add(referrer);
    }

    /// <summary>What is said once for the whole read: each target, and each project not found.</summary>
    private void Report()
    {
        foreach (var (name, (at, count)) in targets)
        {
            diagnostics.Add(SourceCodes.PartNotRead.At(
                at,
                $"The target {name}{(count > 1 ? $", which {count} projects import or define," : "")} runs during a build, and what it does isn't read: a script it adds or takes out, or a setting it changes, is not seen."));
        }

        foreach (var (reference, problem, referrers) in missing.Values)
        {
            var which = reference.Database is null ? "of the same database" : $"of the database {reference.Database}";
            diagnostics.Add(SourceCodes.ProjectNotFound.At(
                reference.At,
                $"{string.Join(", ", referrers)} {(referrers.Count == 1 ? "refers" : "refer")} to {reference.Described}, {which}, which {problem ?? "isn't found"}: what it defines is missing."));
        }
    }

    /// <summary>
    /// One project, read as <c>Debug</c> and compared with a read as <c>Release</c>; null, with why, when it can't be read as a
    /// project at all.
    /// </summary>
    private ProjectRead? Project(string path)
    {
        var shown = Path.GetRelativePath(folder, path).Replace('\\', '/');
        if (ProjectFile.Load(path, shown, out var problem) is not { } file)
        {
            diagnostics.Add(SourceCodes.UnreadableProject.At(new FileLocation(path), $"{Path.GetFileName(path)} can't be read as a project: {problem}."));
            return null;
        }

        if (Read(file, new Dictionary<string, string>(), diagnostics) is not { } debug)
            return null;
        foreach (var target in debug.Targets)
            targets[target.Name] = targets.TryGetValue(target.Name, out var seen) ? (seen.At, seen.Projects + 1) : (target.At, 1);
        var release = Read(file, new Dictionary<string, string> { [ProjectXml.Configuration] = ProjectXml.Release }, []);
        if (release is not null && Difference(debug, release) is { } differs)
        {
            diagnostics.Add(SourceCodes.PartNotRead.At(
                file.At(file.Root),
                $"{Path.GetFileName(path)} builds differently as Release than as Debug: {differs}. It is read as Debug, as dotnet build builds it unless told otherwise."));
        }

        return debug;
    }

    /// <summary>One project, evaluated with <paramref name="globals"/>, and what it gives the read.</summary>
    private ProjectRead? Read(ProjectFile file, IReadOnlyDictionary<string, string> globals, List<Diagnostic> into)
    {
        if (ProjectEvaluator.Evaluate(file, folder, globals, into) is not { } project)
            return null;
        into.AddRange(project.Problems);
        var settings = ProjectSettings.Read(project, into, out var collation);
        var scripts = ProjectFiles.Read(project, into);
        var references = ProjectReferences.Read(project, cache, into);
        return new ProjectRead([.. scripts], settings, collation, references, project.Targets);
    }

    /// <summary>What differs between two reads of one project, in words; null when nothing does.</summary>
    private static string? Difference(ProjectRead debug, ProjectRead release)
    {
        var differs = new List<string>();
        static string Shown(ProjectScript s) => $"{Path.GetFileName(s.Path)}{(s.Settings == ScriptSettings.Default ? "" : $" ({Describe(s.Settings)})")}";
        var only = debug.Scripts.Select(Shown).Except(release.Scripts.Select(Shown)).Concat(release.Scripts.Select(Shown).Except(debug.Scripts.Select(Shown))).ToList();
        if (only.Count > 0)
            differs.Add($"the scripts {string.Join(", ", only)}");
        if (debug.Settings != release.Settings || !string.Equals(debug.Collation, release.Collation, StringComparison.Ordinal))
            differs.Add("the database's settings");
        static string Referred(DatabaseReference r) => $"{r.Kind} {r.FullPath ?? r.FileName} {r.Database}";
        if (!debug.References.Select(Referred).SequenceEqual(release.References.Select(Referred), StringComparer.OrdinalIgnoreCase))
            differs.Add("what it refers to");
        return differs.Count == 0 ? null : string.Join(", and ", differs);
    }

    private static string Describe(ScriptSettings settings) =>
        string.Join(", ", new[] { settings.AnsiNulls ? null : "ANSI_NULLS off", settings.QuotedIdentifier ? null : "QUOTED_IDENTIFIER off" }.OfType<string>());

    /// <summary>
    /// Where a database is read from: a project (<paramref name="project"/>, with what referred to it), or a package
    /// (<paramref name="package"/>).
    /// </summary>
    private sealed class Other(string? project, string? referrer, DatabaseReference? reference, DacPackage? package)
    {
        public string? Project { get; } = project;

        public string? Referrer { get; } = referrer;

        public DatabaseReference? Reference { get; } = reference;

        public DacPackage? Package { get; } = package;
    }

    /// <summary>One database's sources read, to become its model once the databases it refers to are read too.</summary>
    private sealed class DatabaseRead(SchemaDeclarations declarations, DatabaseSettings? settings, string? collation, List<UnreadPart> unread, bool rooted)
    {
        /// <summary>Whether its root project was read.</summary>
        public bool Rooted { get; } = rooted;

        /// <summary>The model, holding <paramref name="databases"/> as the other databases it refers to.</summary>
        public DatabaseModel ToModel(IReadOnlyDictionary<string, DatabaseModel>? databases = null)
        {
            var model = declarations.ToModel(settings, unread, databases);
            return settings is null ? model.WithDeclaredCollation(collation) : model;
        }
    }

    /// <summary>What one project gives the read: its scripts, its settings (the database's, if it is the root), and what it refers to.</summary>
    private sealed class ProjectRead(EquatableArray<ProjectScript> scripts, DatabaseSettings? settings, string? collation, IReadOnlyList<DatabaseReference> references, IReadOnlyList<ProjectTarget> targets)
    {
        public EquatableArray<ProjectScript> Scripts { get; } = scripts;

        public DatabaseSettings? Settings { get; } = settings;

        public string? Collation { get; } = collation;

        public IReadOnlyList<DatabaseReference> References { get; } = references;

        public IReadOnlyList<ProjectTarget> Targets { get; } = targets;
    }
}

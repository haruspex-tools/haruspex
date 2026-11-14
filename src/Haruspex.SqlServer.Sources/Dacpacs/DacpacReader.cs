using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// Reads a DACPAC, and the DACPACs of the same database it refers to, into one <see cref="DatabaseModel"/>: the model a folder
/// of the same project's scripts gives, read from <c>model.xml</c> directly, without DacFx.
/// </summary>
/// <remarks>
/// <para>
/// A DACPAC is an OPC zip whose <c>model.xml</c> holds the schema as elements: tables and their columns, constraints and
/// indexes as structured data, and each module's text as written. Tables are read from the elements (<see cref="TableReader"/>),
/// and modules parsed by ScriptDom and read by the same reader as a folder's scripts (<see cref="ModuleTextReader"/>). The
/// guide's chapter <i>The inputs and the model</i> explains it (<c>docs/internals/02-the-inputs-and-the-model.md</c>).
/// </para>
/// <para>
/// <b>References.</b> A database split across projects builds a package per project, each referring to the others it uses. Every
/// package of the same database reachable through references is read, each once, into the one model: what the database holds
/// is the sum of them. A package of another database (by name or SQLCMD variable) or <c>master</c> is read the same way into a
/// model of its own, which the model holds under the name a body writes the database by
/// (<see cref="DatabaseModel.Databases"/>); that database's own references to a third are recorded, not read. A reference not found is
/// one warning, with every place looked (<see cref="ReferenceFinder"/>), and the model says what it lacks
/// (<see cref="DatabaseModel.Unread"/>); an object defined in two packages is reported as a folder's would be.
/// </para>
/// </remarks>
public static class DacpacReader
{
    /// <summary>Reads the package at <paramref name="path"/> and every package of its database it refers to.</summary>
    /// <param name="path">The DACPAC.</param>
    /// <param name="searchFolders">More folders to look for referenced packages in, after the referring package's own (<c>references</c> in the config file).</param>
    public static DacpacReadResult Read(string path, IReadOnlyList<string>? searchFolders = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var folders = (searchFolders ?? []).Select(Path.GetFullPath).ToList();
        var diagnostics = new List<Diagnostic>();
        if (DacPackage.Open(path, diagnostics) is not { } root)
            return new DacpacReadResult(new SchemaDeclarations().ToModel(), null, [], [.. diagnostics]);

        var seen = new Dictionary<(string Name, string? Database), ReferenceRead>();
        var others = new Dictionary<string, DacPackage>(StringComparer.OrdinalIgnoreCase);
        var own = Database(root, folders, seen, others, diagnostics);

        // Each other database the package refers to, read with the packages of its own database; its references to a third
        // database are recorded, not followed (decision 0040).
        var databases = others.ToDictionary(o => o.Key, o => Database(o.Value, folders, seen, null, diagnostics).ToModel(), StringComparer.OrdinalIgnoreCase);
        var model = own.ToModel(databases);
        return new DacpacReadResult(model, new LoadedPackage(root.Name, root.Path, root.Sha256), [.. seen.Values], [.. diagnostics]);
    }

    /// <summary>
    /// The model of one database: <paramref name="root"/> and every package of its database reachable through references,
    /// breadth first, each once by its file name whatever refers to it. Each reference met is recorded in <paramref name="seen"/>;
    /// a found package of another database is added to <paramref name="others"/> by the name the reference gives the database,
    /// when that is wanted, and otherwise only recorded.
    /// </summary>
    private static DatabaseRead Database(
        DacPackage root,
        IReadOnlyList<string> folders,
        Dictionary<(string Name, string? Database), ReferenceRead> seen,
        Dictionary<string, DacPackage>? others,
        List<Diagnostic> diagnostics)
    {
        var declarations = new SchemaDeclarations();
        var database = new List<DacPackage> { root };
        var unread = new List<UnreadPart>();
        for (var i = 0; i < database.Count; i++)
        {
            var referrer = database[i];
            foreach (var reference in referrer.References)
            {
                var key = (reference.LogicalName.ToUpperInvariant(), reference.Database);
                if (reference.Database is null && database.Any(p => p.Name.Equals(reference.LogicalName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (seen.TryGetValue(key, out var met))
                {
                    if (reference.Database is null && met.Found is null)
                        unread.Add(new UnreadPart(reference.LogicalName, referrer.Name));
                    continue;
                }

                var found = Find(reference, referrer, folders, diagnostics);
                seen[key] = new ReferenceRead(reference.LogicalName, reference.Database, found is null ? null : new LoadedPackage(found.Name, found.Path, found.Sha256));
                if (reference.Database is null)
                {
                    if (found is not null)
                        database.Add(found);
                    else
                        unread.Add(new UnreadPart(reference.LogicalName, referrer.Name));
                }
                else if (found is not null && others is not null)
                {
                    others.TryAdd(reference.Database, found);
                }
            }
        }

        foreach (var package in database)
            PackageReader.Read(package, declarations);
        diagnostics.AddRange(declarations.Diagnostics);
        return new DatabaseRead(declarations, PackageReader.Settings(root), unread);
    }

    /// <summary>One database's packages read, to become its model once the databases it refers to are read too.</summary>
    private sealed class DatabaseRead(SchemaDeclarations declarations, DatabaseSettings? settings, List<UnreadPart> unread)
    {
        /// <summary>The model, holding <paramref name="databases"/> as the other databases it refers to.</summary>
        public DatabaseModel ToModel(IReadOnlyDictionary<string, DatabaseModel>? databases = null) => declarations.ToModel(settings, unread, databases);
    }

    /// <summary>The referenced package, opened; or null, with a warning naming every place looked, when there is none.</summary>
    private static DacPackage? Find(PackageReference reference, DacPackage referrer, IReadOnlyList<string> folders, List<Diagnostic> diagnostics)
    {
        if (ReferenceFinder.Find(reference, referrer.Path, folders) is { } path)
            return DacPackage.Open(path, diagnostics);
        var which = reference.Database is null ? "a package of the same database" : $"the package of the database {reference.Database}";
        var places = string.Join(", ", ReferenceFinder.Candidates(reference, referrer.Path, folders));
        diagnostics.Add(SourceCodes.ReferenceNotFound.At(
            new FileLocation(referrer.Path),
            $"{referrer.Name} refers to {reference.LogicalName}, {which}, which isn't found: looked for at {places}. Put it beside {referrer.Name}, or name its folder in \"references\" in the config file."));
        return null;
    }
}

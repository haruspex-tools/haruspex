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
/// is the sum of them. One referring to another database (by name or SQLCMD variable) or to <c>master</c> is found and hashed,
/// so the output says what it rests on, but not read: names in another database are not bound yet. A reference not found is
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
        var declarations = new SchemaDeclarations();
        if (DacPackage.Open(path, declarations.Diagnostics) is not { } root)
            return new DacpacReadResult(declarations.ToModel(), null, [], [.. declarations.Diagnostics]);

        // Breadth first from the package given: each package of the database once, by its file name, whatever refers to it.
        var database = new List<DacPackage> { root };
        var seen = new Dictionary<(string Name, string? Database), ReferenceRead>();
        var unread = new List<string>();
        for (var i = 0; i < database.Count; i++)
        {
            var referrer = database[i];
            foreach (var reference in referrer.References)
            {
                var key = (reference.LogicalName.ToUpperInvariant(), reference.Database);
                if (seen.ContainsKey(key) || (reference.Database is null && reference.LogicalName.Equals(root.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var found = Find(reference, referrer, folders, declarations.Diagnostics);
                seen[key] = new ReferenceRead(reference.LogicalName, reference.Database, found is null ? null : new LoadedPackage(found.Name, found.Path, found.Sha256));
                if (found is not null && reference.Database is null)
                    database.Add(found);
                else if (found is null && reference.Database is null)
                    unread.Add($"{reference.LogicalName}, a package {referrer.Name} refers to that was not found");
            }
        }

        foreach (var package in database)
            PackageReader.Read(package, declarations);

        var model = declarations.ToModel(PackageReader.Settings(root), unread);
        return new DacpacReadResult(model, new LoadedPackage(root.Name, root.Path, root.Sha256), [.. seen.Values], [.. declarations.Diagnostics]);
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

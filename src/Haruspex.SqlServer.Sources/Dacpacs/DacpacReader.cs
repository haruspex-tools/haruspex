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
/// (<see cref="DatabaseModel.Unread"/>); an object defined in two packages is reported as a folder's would be. Following the
/// references is <see cref="PackageSet"/>'s, which a project's read shares.
/// </para>
/// </remarks>
public static class DacpacReader
{
    /// <summary>Reads the package at <paramref name="path"/> and every package of its database it refers to.</summary>
    /// <param name="path">The DACPAC.</param>
    /// <param name="searchFolders">More folders to look for referenced packages in, after the referring package's own (<c>references</c> in the config file).</param>
    /// <param name="builds">The file names of packages declared built from a project (<c>builds</c> in the config file), whose key constraints hold.</param>
    public static DacpacReadResult Read(string path, IReadOnlyList<string>? searchFolders = null, IReadOnlyCollection<string>? builds = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var diagnostics = new List<Diagnostic>();
        if (DacPackage.Open(path, diagnostics) is not { } root)
            return new DacpacReadResult(new SchemaDeclarations().ToModel(), null, [], [.. diagnostics]);

        var packages = new PackageSet([.. (searchFolders ?? []).Select(Path.GetFullPath)], diagnostics, builds ?? []);
        var own = new SchemaDeclarations();
        var others = new Dictionary<string, DacPackage>(StringComparer.OrdinalIgnoreCase);
        var unread = packages.ReadInto(own, [root], others);
        own.SettleSynonyms();
        diagnostics.AddRange(own.Diagnostics);

        // Each other database the package refers to, read with the packages of its own database; its references to a third
        // database are recorded, not followed (decision 0040).
        var databases = others.ToDictionary(o => o.Key, o => OtherDatabase(o.Key, o.Value, packages, diagnostics), StringComparer.OrdinalIgnoreCase);
        var model = own.ToModel(PackageReader.Settings(root), unread, databases);
        return new DacpacReadResult(model, new LoadedPackage(root.Name, root.Path, root.Sha256), [.. packages.References], [.. diagnostics]);
    }

    /// <summary>
    /// The name a reference gives the master database. Its package lists the system's objects, most of them without text and none
    /// a project can act on, so its note of what isn't modelled would say the same useless thing on every run, and isn't given.
    /// </summary>
    internal const string Master = "master";

    /// <summary>
    /// The model of another database, read from <paramref name="root"/> and the packages of its own database, one level down: its
    /// references to a third database are recorded, not read.
    /// </summary>
    /// <param name="name">The name the reference gives the database.</param>
    /// <param name="root">Its package.</param>
    /// <param name="packages">The packages of the read.</param>
    /// <param name="diagnostics">Where what is found reading it is reported.</param>
    internal static DatabaseModel OtherDatabase(string name, DacPackage root, PackageSet packages, List<Diagnostic> diagnostics)
    {
        var declarations = new SchemaDeclarations();
        var unread = packages.ReadInto(declarations, [root], null, quiet: name.Equals(Master, StringComparison.OrdinalIgnoreCase));
        declarations.SettleSynonyms();
        diagnostics.AddRange(declarations.Diagnostics);
        return declarations.ToModel(PackageReader.Settings(root), unread);
    }
}

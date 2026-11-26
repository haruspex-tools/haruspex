using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// Reads a database project's <c>.sqlproj</c>, and what it refers to, into one <see cref="DatabaseModel"/>: the model the DACPAC its
/// build makes gives, read with the same referenced packages, with neither MSBuild, DacFx nor a build. Both formats
/// are read: <c>Microsoft.Build.Sql</c>'s, whose build reads every <c>.sql</c> file under the project's folder by default, and
/// legacy SSDT's, which lists each.
/// </summary>
/// <remarks>
/// <para>
/// A read has three steps: the project file, evaluated as far as decision 0044 says (<see cref="ProjectEvaluator"/>); the scripts
/// its build reads, each with the settings it starts with (<see cref="ProjectFiles"/>), and the database's settings
/// (<see cref="ProjectSettings"/>); then those scripts, read as a folder's are, into one model. Locations are relative to the
/// given project's folder (<c>../crane/berth.sql:3:5</c> for a referenced project's script), so each names a file a person can
/// open.
/// </para>
/// <para>
/// <b>References.</b> Each project of the same database a project refers to is read from its own <c>.sqlproj</c>, its scripts with
/// its own settings, transitively, each once: the database is the sum of them, as it is of a DACPAC's packages. The database's
/// settings are the given project's. A DACPAC of the same database a project refers to, by its path or in a NuGet package, is read
/// into the same model as a DACPAC's packages are, with the packages of its database it refers to. Another database, a project or
/// a DACPAC, is read into a model of its own, one level down, which the model holds under the name a body writes it by
/// (<see cref="DatabaseModel.Databases"/>), as from a DACPAC (decision 0042). Where each is looked for, and why, is
/// <see cref="ProjectReferences"/>'s. A referenced project or package not found is one warning, and the model says what it lacks
/// (<see cref="DatabaseModel.Unread"/>), naming every project that refers to it; an object two sources define is reported as a
/// folder's would be. A target, which isn't read, is reported once by its name, however many projects define it.
/// </para>
/// <para>
/// <b>Configuration.</b> A project is read as <c>dotnet build</c> builds it, as <c>Debug</c> unless it says otherwise, and read
/// again as <c>Release</c>: where the two differ in what is read (a script, a setting, a reference), that is a warning naming it.
/// </para>
/// </remarks>
public static class SqlProjectReader
{
    /// <summary>Reads the project at <paramref name="path"/> and what it refers to.</summary>
    /// <param name="path">The <c>.sqlproj</c>.</param>
    /// <param name="searchFolders">
    /// More folders to look for a referenced DACPAC in, by its file name, after where the build takes it from (<c>references</c>
    /// in the config file).
    /// </param>
    /// <param name="builds">The file names of referenced packages declared built from a project (<c>builds</c> in the config file), whose key constraints hold.</param>
    public static SqlProjectReadResult Read(string path, IReadOnlyList<string>? searchFolders = null, IReadOnlyCollection<string>? builds = null) =>
        Read(path, searchFolders ?? [], PackageCache.Default, builds ?? []);

    /// <summary>
    /// Reads the project at <paramref name="path"/>, finding the packages it refers to in <paramref name="cache"/>, and each path on
    /// disk through <paramref name="listings"/> when given.
    /// </summary>
    internal static SqlProjectReadResult Read(string path, IReadOnlyList<string> searchFolders, PackageCache cache, IReadOnlyCollection<string>? builds = null, FolderListings? listings = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(searchFolders);
        var root = Path.GetFullPath(path);
        return new ProjectReading(Path.GetDirectoryName(root) ?? ".", [.. searchFolders.Select(Path.GetFullPath)], cache, builds ?? [], listings).Read(root);
    }
}

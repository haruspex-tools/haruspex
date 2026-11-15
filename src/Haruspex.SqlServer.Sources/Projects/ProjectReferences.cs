using System.Text.RegularExpressions;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// What a project refers to: the projects of the same database, which are read with it into one model, as a DACPAC's packages of
/// the same database are; and the references read in a later slice (another database's project, a DACPAC, a package), each one
/// note, with whatever is named in it unknown exactly as from a folder of the same scripts.
/// </summary>
/// <remarks>
/// A <c>ProjectReference</c> is to the same database unless it names another, by <c>DatabaseVariableLiteralValue</c>,
/// <c>DatabaseSqlCmdVariable</c> or <c>ServerSqlCmdVariable</c>, as SSDT and Microsoft.Build.Sql write one. A reference to a
/// project that isn't a database project (a C# one, for a CLR assembly) adds no schema the scripts don't declare, and is passed
/// over.
/// </remarks>
internal static partial class ProjectReferences
{
    private static readonly string[] OtherDatabase = [ProjectXml.DatabaseVariableLiteralValue, ProjectXml.DatabaseSqlCmdVariable, ProjectXml.ServerSqlCmdVariable];

    /// <summary>
    /// The system databases' packages Visual Studio's SSDT installs and its projects refer to by a path under its own folder, which
    /// only its build defines: <c>$(DacPacRootPath)\Extensions\Microsoft\SQLDB\Extensions\SqlServer\150\SqlSchemas\master.dacpac</c>.
    /// Recognised by that form, as SSDT's imports are by their names, so the reference is the note any reference to <c>master</c>
    /// is, not a warning that a property is defined nowhere.
    /// </summary>
    [GeneratedRegex(@"^\$\(DacPacRootPath\)[\\/].*[\\/]SqlSchemas[\\/](?<name>[^\\/]+\.dacpac)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SsdtSystemPackage();

    /// <summary>
    /// The projects of the same database <paramref name="project"/> refers to; every other reference to a database noted, and a
    /// package reference only when its package holds a DACPAC (or <paramref name="cache"/> can't say).
    /// </summary>
    public static IReadOnlyList<ProjectItem> SameDatabase(EvaluatedProject project, PackageCache cache, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var name = Path.GetFileName(project.File.Path);
        foreach (var item in project.Doubted)
            Doubted(item, name, cache, diagnostics);

        var same = new List<ProjectItem>();
        foreach (var item in project.OfType(ProjectXml.ProjectReference))
        {
            if (OtherDatabase.FirstOrDefault(m => item.Metadata.TryGetValue(m, out var v) && v.Text.Length > 0) is { } other)
                NotRead(diagnostics, item.At, $"{name} refers to {item.Spec} as another database ({other} {item.Metadata[other].Text}), which isn't read yet: a name in it stays unknown.");
            else if (item.Spec.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase))
                same.Add(item);
        }

        foreach (var item in project.OfType(ProjectXml.ArtifactReference))
            NotRead(diagnostics, item.At, $"{name} refers to the DACPAC {item.Spec}, which isn't read from a project yet: a name in it stays unknown.");
        foreach (var item in project.OfType(ProjectXml.PackageReference))
            Package(item.Spec, item.Metadata.GetValueOrDefault(ProjectXml.Version)?.Text, item.At, name, cache, diagnostics);
        return same;
    }

    /// <summary>
    /// A reference whose element isn't evaluated: said as any reference of its kind when what it names is known (none is read yet
    /// but a project of the same database), and as a part not read when what it names isn't.
    /// </summary>
    private static void Doubted(DoubtedItem item, string name, PackageCache cache, ICollection<Diagnostic> diagnostics)
    {
        switch (item.Type)
        {
            case ProjectXml.ArtifactReference when SsdtSystemPackage().Match(item.Written) is { Success: true } system:
                NotRead(diagnostics, item.At, $"{name} refers to SSDT's own {system.Groups["name"].Value}, which isn't read from a project yet: a name in it stays unknown.");
                break;
            case ProjectXml.ArtifactReference when item.Spec is { } spec:
                NotRead(diagnostics, item.At, $"{name} refers to the DACPAC {spec}, which isn't read from a project yet: a name in it stays unknown.");
                break;
            case ProjectXml.PackageReference when item.Spec is { } spec:
                Package(spec, null, item.At, name, cache, diagnostics);
                break;
            case ProjectXml.ProjectReference when item.Spec is { } spec && !spec.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase):
                break;
            case ProjectXml.ProjectReference or ProjectXml.ArtifactReference or ProjectXml.PackageReference:
                Doubt.Report(diagnostics, $"The {item.Type} {item.Written} of {name}", item.Doubts);
                break;
        }
    }

    /// <summary>
    /// A package reference, noted when its package holds a DACPAC: a system database's (<c>Microsoft.SqlServer.Dacpacs.*</c>), or one
    /// the cache shows with <c>tools/*.dacpac</c>, or one the cache doesn't have, which may. The reference assemblies SSDT's newer
    /// project template refers to when built with the .NET SDK hold none, and say nothing whether restored or not.
    /// </summary>
    private static void Package(string id, string? version, SourceLocation at, string name, PackageCache cache, ICollection<Diagnostic> diagnostics)
    {
        if (id.StartsWith("Microsoft.NETFramework.ReferenceAssemblies", StringComparison.OrdinalIgnoreCase))
            return;
        var holds = id.StartsWith("Microsoft.SqlServer.Dacpacs.", StringComparison.OrdinalIgnoreCase) ? true : cache.HoldsDacpac(id, version);
        switch (holds)
        {
            case true:
                NotRead(diagnostics, at, $"{name} refers to the package {id}, whose DACPAC isn't read from a project yet: a name in it stays unknown.");
                break;
            case null:
                NotRead(diagnostics, at, $"{name} refers to the package {id}, which isn't in the NuGet cache, so whether it holds a DACPAC isn't known; one it holds isn't read from a project yet, and a name in it stays unknown.");
                break;
        }
    }

    private static void NotRead(ICollection<Diagnostic> diagnostics, SourceLocation at, string message) =>
        diagnostics.Add(SourceCodes.ReferenceNotRead.At(at, message));
}

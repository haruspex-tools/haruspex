using System.Text.RegularExpressions;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// What a project refers to that adds to a database's schema, each resolved to where its build takes it from, as observed
/// (<c>docs/decisions/0045-a-project-s-references-are-read-from-where-its-build-takes-them.md</c>): another database project
/// (<c>ProjectReference</c>), a DACPAC (<c>ArtifactReference</c>), or the DACPAC a NuGet package holds (<c>PackageReference</c>),
/// each of the project's own database or of another.
/// </summary>
/// <remarks>
/// <para>
/// A reference is to another database when it names one, by <c>DatabaseSqlCmdVariable</c> (a body writes it <c>[$(VaultDb)]</c>)
/// or <c>DatabaseVariableLiteralValue</c> (<c>almanac</c>), and to the project's own otherwise. A variable is taken by its name,
/// never its value: a build records every variable's value empty, and binds a variable the project doesn't declare as it binds one it does
/// (observed). One that names a server too (<c>ServerSqlCmdVariable</c>) is noted and not read
/// (<see cref="SourceCodes.OtherServer"/>). A reference to a project that isn't a database project (a C# one, for a CLR assembly)
/// adds no schema the scripts don't declare, and is passed over.
/// </para>
/// <para>
/// <b>Where each is.</b> A project or an artifact is its <c>Include</c>; an artifact's <c>HintPath</c> is not used (observed: an
/// <c>Include</c> that isn't there fails the build, SQL72027, whatever the <c>HintPath</c>). A package, in a
/// <c>Microsoft.Build.Sql</c> project only (a legacy project's build makes no reference of one, observed), is its
/// <c>HintPath</c>, or else <c>tools/&lt;name&gt;.dacpac</c> in the folder the restore gave its version
/// (<see cref="PackageCache"/>), its name being its <c>DacpacName</c>, else <c>master</c> or <c>msdb</c> for a system database's
/// package (<c>Microsoft.SqlServer.Dacpacs.*</c>), else its id; a system database's package is that database unless it names
/// another. A package restored without that file is no reference at all, as the build makes none (observed, with a
/// <c>DacpacName</c> naming no file: no reference and no warning).
/// </para>
/// </remarks>
internal static partial class ProjectReferences
{
    private const string SystemPackages = "Microsoft.SqlServer.Dacpacs";

    /// <summary>
    /// The system databases' packages Visual Studio's SSDT installs and its projects refer to by a path under its own folder, which
    /// only its build defines: <c>$(DacPacRootPath)\Extensions\Microsoft\SQLDB\Extensions\SqlServer\150\SqlSchemas\master.dacpac</c>.
    /// Recognised by that form, as SSDT's imports are by their names, so the reference is read, from a <c>references</c> folder of
    /// the config file, rather than reported as a property defined nowhere.
    /// </summary>
    [GeneratedRegex(@"^\$\(DacPacRootPath\)[\\/].*[\\/]SqlSchemas[\\/](?<name>[^\\/]+\.dacpac)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SsdtSystemPackage();

    /// <summary>The references of <paramref name="project"/> that add to a database's schema, in the order its elements give them.</summary>
    /// <param name="project">The project.</param>
    /// <param name="cache">Where packages are looked for.</param>
    /// <param name="diagnostics">Where a reference that isn't evaluated, or isn't read, is reported.</param>
    public static IReadOnlyList<DatabaseReference> Read(EvaluatedProject project, PackageCache cache, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var name = Path.GetFileName(project.File.Path);
        var references = new List<DatabaseReference>();
        foreach (var item in project.Doubted)
            Doubted(item, name, project.Format.IsSdkStyle, diagnostics, references);

        foreach (var item in project.OfType(ProjectXml.ProjectReference).Where(i => i.Spec.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase)))
        {
            if (Database(item.Metadata, $"The ProjectReference {item.Spec} of {name}", $"{name} refers to the project {item.Spec}", item.At, diagnostics) is { } database)
                references.Add(new DatabaseReference(DatabaseReferenceKind.Project, $"the project {item.Spec}", item.FullPath, Path.GetFileName(item.Spec), database.Name, item.At));
        }

        foreach (var item in project.OfType(ProjectXml.ArtifactReference))
        {
            if (Database(item.Metadata, $"The ArtifactReference {item.Spec} of {name}", $"{name} refers to the DACPAC {item.Spec}", item.At, diagnostics) is { } database)
                references.Add(new DatabaseReference(DatabaseReferenceKind.Artifact, $"the DACPAC {item.Spec}", item.FullPath, Path.GetFileName(item.Spec.Replace('\\', '/')), database.Name, item.At));
        }

        if (project.Format.IsSdkStyle)
        {
            foreach (var item in project.OfType(ProjectXml.PackageReference))
                Package(item, project, cache, name, diagnostics, references);
        }

        return references;
    }

    /// <summary>
    /// A reference whose element isn't evaluated: Visual Studio's own system database, read by its file name; one to a project that
    /// isn't a database project, or to a package from a legacy project, passed over; any other, reported as not read.
    /// </summary>
    private static void Doubted(DoubtedItem item, string name, bool sdk, ICollection<Diagnostic> diagnostics, List<DatabaseReference> into)
    {
        switch (item.Type)
        {
            case ProjectXml.ArtifactReference when SsdtSystemPackage().Match(item.Written) is { Success: true } system:
                var file = system.Groups["name"].Value;
                if (Database(item.Metadata, $"The ArtifactReference {item.Written} of {name}", $"{name} refers to SSDT's own {file}", item.At, diagnostics) is { } database)
                    into.Add(new DatabaseReference(DatabaseReferenceKind.VisualStudio, $"SSDT's own {file}", null, file, database.Name, item.At));
                break;
            case ProjectXml.ProjectReference when item.Spec is { } spec && !spec.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase):
                break;
            case ProjectXml.ProjectReference or ProjectXml.ArtifactReference:
            case ProjectXml.PackageReference when sdk && (item.Spec is null || !item.Spec.StartsWith("Microsoft.NETFramework.ReferenceAssemblies", StringComparison.OrdinalIgnoreCase)):
                Doubt.Report(diagnostics, $"The {item.Type} {item.Written} of {name}", item.Doubts);
                break;
        }
    }

    /// <summary>
    /// The database a reference's metadata names: null for the project's own (<see cref="Named.Name"/> null), or the name a body writes
    /// it by. Null altogether, with why, when it isn't read: its metadata isn't evaluated, it names its database both ways, or it
    /// names a server.
    /// </summary>
    private static Named? Database(IReadOnlyDictionary<string, Evaluated> metadata, string what, string who, SourceLocation at, ICollection<Diagnostic> diagnostics, string? otherwise = null)
    {
        Evaluated Of(string name) => metadata.GetValueOrDefault(name) ?? Evaluated.Sure("");
        var variable = Of(ProjectXml.DatabaseSqlCmdVariable);
        var literal = Of(ProjectXml.DatabaseVariableLiteralValue);
        var server = Of(ProjectXml.ServerSqlCmdVariable);
        var doubts = variable.Doubts.Concat(literal.Doubts).Concat(server.Doubts).ToList();
        if (doubts.Count > 0)
        {
            Doubt.Report(diagnostics, what, doubts);
            return null;
        }

        if (variable.Text.Length > 0 && literal.Text.Length > 0)
        {
            Doubt.Report(diagnostics, what, [new Doubt(at, $"it names its database both by {ProjectXml.DatabaseSqlCmdVariable} and by {ProjectXml.DatabaseVariableLiteralValue}, and which a build takes isn't observed")]);
            return null;
        }

        var database = variable.Text.Length > 0 ? $"$({variable.Text})" : literal.Text.Length > 0 ? literal.Text : otherwise;
        if (server.Text.Length > 0)
        {
            var note = SourceCodes.OtherServer.At(at, $"{who}, the database {database} on the server $({server.Text}), which isn't read: a name in it, which a body writes in four parts, stays unknown.");
            if (!diagnostics.Contains(note))
                diagnostics.Add(note);
            return null;
        }

        return new Named(database);
    }

    /// <summary>A package's DACPAC, where its build takes it from, added to <paramref name="into"/>; nothing when the build makes no reference of it.</summary>
    private static void Package(ProjectItem item, EvaluatedProject project, PackageCache cache, string name, ICollection<Diagnostic> diagnostics, List<DatabaseReference> into)
    {
        var id = item.Spec;
        if (id.StartsWith("Microsoft.NETFramework.ReferenceAssemblies", StringComparison.OrdinalIgnoreCase))
            return;
        Evaluated Of(string metadata) => item.Metadata.GetValueOrDefault(metadata) ?? Evaluated.Sure("");
        var what = $"The PackageReference {id} of {name}";
        var used = new[] { Of(ProjectXml.DacpacName), Of(ProjectXml.HintPath), Of(ProjectXml.Version) };
        if (used.SelectMany(u => u.Doubts).ToList() is { Count: > 0 } doubts)
        {
            Doubt.Report(diagnostics, what, doubts);
            return;
        }

        // As Sdk.targets names the package's DACPAC and its database: master or msdb by the package's name, master for any other
        // system package (the first versions of Microsoft.SqlServer.Dacpacs held only master), and the package's id otherwise.
        var system = id.StartsWith(SystemPackages, StringComparison.OrdinalIgnoreCase);
        var msdb = system && id.EndsWith("msdb", StringComparison.OrdinalIgnoreCase);
        var master = system && id.EndsWith(DacpacReader.Master, StringComparison.OrdinalIgnoreCase);
        var dacpac = (Of(ProjectXml.DacpacName).Text is { Length: > 0 } named ? named : system ? (msdb ? "msdb" : DacpacReader.Master) : id) + ".dacpac";
        if (Database(item.Metadata, what, $"{name} refers to the package {id}", item.At, diagnostics, master ? DacpacReader.Master : msdb ? "msdb" : null) is not { } database)
            return;

        var version = Of(ProjectXml.Version).Text;
        var described = $"{dacpac} of the package {id}{(version.Length > 0 ? " " + version : "")}";
        var folder = Path.GetDirectoryName(project.File.Path) ?? ".";
        if (Of(ProjectXml.HintPath).Text is { Length: > 0 } hint)
        {
            // The build makes a reference only of a file that is there.
            if (DiskPath.Find(folder, hint, out _) is { } given && File.Exists(given))
                into.Add(new DatabaseReference(DatabaseReferenceKind.Package, described, given, dacpac, database.Name, item.At));
            return;
        }

        if (version.Length == 0)
        {
            Doubt.Report(diagnostics, what, [new Doubt(item.At, "it gives no version, and a version given elsewhere (Directory.Packages.props) isn't read")]);
            return;
        }

        // The project's own restore folder, when it names one; a doubted one could be any folder, so nothing in a cache is read.
        var own = project.Properties[ProjectXml.RestorePackagesPath];
        if (own is { IsSure: false })
        {
            Doubt.Report(diagnostics, what, own.Doubts);
            return;
        }

        var restored = own is { Text.Length: > 0 } path ? DiskPath.Full(folder, path.Text) : cache.Folder;
        if (PackageCache.VersionFolder(restored, id, version) is not { } versionFolder)
        {
            Doubt.Report(diagnostics, what, [new Doubt(item.At, $"its version {version} isn't one version, and a range or a floating version is resolved by the restore against a feed")]);
            return;
        }

        var tools = Path.Join(versionFolder, "tools", dacpac);
        if (DiskPath.Locate(versionFolder, version, out _) is not { } found)
            into.Add(new DatabaseReference(DatabaseReferenceKind.Package, described, tools, dacpac, database.Name, item.At));
        else if (DiskPath.Locate(Path.Join(found, "tools", dacpac), dacpac, out _) is { } file && File.Exists(file))
            into.Add(new DatabaseReference(DatabaseReferenceKind.Package, described, file, dacpac, database.Name, item.At));
    }

    /// <summary>A database as a reference names it: null for the project's own.</summary>
    private sealed record Named(string? Name);
}

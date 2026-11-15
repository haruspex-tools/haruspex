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
internal static class ProjectReferences
{
    private static readonly string[] OtherDatabase = [ProjectXml.DatabaseVariableLiteralValue, ProjectXml.DatabaseSqlCmdVariable, ProjectXml.ServerSqlCmdVariable];

    /// <summary>The projects of the same database <paramref name="project"/> refers to, full paths as written; every other reference noted.</summary>
    public static IReadOnlyList<ProjectItem> SameDatabase(EvaluatedProject project, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var name = Path.GetFileName(project.File.Path);
        foreach (var item in project.Doubted.Where(d => d.Type is ProjectXml.ProjectReference or ProjectXml.ArtifactReference or ProjectXml.PackageReference))
            Doubt.Report(diagnostics, $"The {item.Type} of {name}", item.Doubts);

        var same = new List<ProjectItem>();
        foreach (var item in project.OfType(ProjectXml.ProjectReference))
        {
            if (OtherDatabase.FirstOrDefault(m => item.Metadata.TryGetValue(m, out var v) && v.Text.Length > 0) is { } other)
                NotRead(diagnostics, item, $"{name} refers to {item.Spec} as another database ({other} {item.Metadata[other].Text}), which isn't read yet: a name in it stays unknown.");
            else if (item.Spec.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase))
                same.Add(item);
        }

        foreach (var item in project.OfType(ProjectXml.ArtifactReference))
            NotRead(diagnostics, item, $"{name} refers to the DACPAC {item.Spec}, which isn't read from a project yet: a name in it stays unknown.");
        foreach (var item in project.OfType(ProjectXml.PackageReference))
            NotRead(diagnostics, item, $"{name} refers to the package {item.Spec}, whose DACPAC isn't read from a project yet: a name in it stays unknown.");
        return same;
    }

    private static void NotRead(ICollection<Diagnostic> diagnostics, ProjectItem item, string message) =>
        diagnostics.Add(SourceCodes.ReferenceNotRead.At(item.At, message));
}

using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// Reads a database project's <c>.sqlproj</c>, and the projects of the same database it refers to, into one
/// <see cref="DatabaseModel"/>: the model the DACPAC its build makes gives, with neither MSBuild, DacFx nor a build. Both formats
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
/// settings are the given project's. A referenced project not found is one warning, and the model says what it lacks
/// (<see cref="DatabaseModel.Unread"/>); an object two projects define is reported as a folder's would be.
/// </para>
/// <para>
/// <b>Configuration.</b> A project is read as <c>dotnet build</c> builds it, as <c>Debug</c> unless it says otherwise, and read
/// again as <c>Release</c>: where the two differ in what is read (a script, a setting, a reference), that is a warning naming it.
/// </para>
/// </remarks>
public static class SqlProjectReader
{
    /// <summary>Reads the project at <paramref name="path"/> and every project of its database it refers to.</summary>
    /// <param name="path">The <c>.sqlproj</c>.</param>
    public static SqlProjectReadResult Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var root = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(root) ?? ".";
        var diagnostics = new List<Diagnostic>();
        var queue = new List<(string Path, string? Referrer, ProjectItem? Reference)> { (root, null, null) };
        var projects = new List<string>();
        var scripts = new List<ScriptFile>();
        var unread = new List<UnreadPart>();
        DatabaseSettings? settings = null;
        string? collation = null;
        for (var i = 0; i < queue.Count; i++)
        {
            var (next, referrer, reference) = queue[i];
            var located = DiskPath.Locate(next, reference?.Spec ?? next, out var problem);
            if (located is not null && projects.Contains(located, StringComparer.OrdinalIgnoreCase))
                continue;
            if (located is null && referrer is not null && reference is not null)
            {
                diagnostics.Add(SourceCodes.ProjectNotFound.At(
                    reference.At,
                    $"{referrer} refers to {reference.Spec}, a project of the same database, which {problem ?? "isn't found"}: what it defines is missing."));
                unread.Add(new UnreadPart(Path.GetFileName(next), referrer));
                continue;
            }

            located ??= next;
            projects.Add(located);
            if (Project(located, folder, diagnostics) is not { } read)
                continue;
            if (i == 0)
            {
                settings = read.Settings;
                collation = read.Collation;
            }

            scripts.AddRange(read.Scripts.Select(s => new ScriptFile(s.Path, Path.GetRelativePath(folder, s.Path).Replace('\\', '/'), s.Settings)));
            queue.AddRange(read.References.Select(r => (r.FullPath ?? r.Spec, (string?)Path.GetFileName(located), (ProjectItem?)r)));
        }

        // In order of their relative paths, as a folder's are, so the outcome doesn't depend on the order of the items; a script two
        // projects build is read twice, and what it defines is defined twice, as in their packages.
        var declarations = new SchemaDeclarations();
        SqlScriptFolder.ReadInto(declarations, scripts.OrderBy(s => s.Relative, StringComparer.Ordinal));
        diagnostics.AddRange(declarations.Diagnostics);
        var model = declarations.ToModel(settings, unread);
        return new SqlProjectReadResult(settings is null ? model.WithDeclaredCollation(collation) : model, [.. projects], [.. diagnostics.Distinct()]);
    }

    /// <summary>
    /// One project, read as <c>Debug</c> and compared with a read as <c>Release</c>; null, with why, when it can't be read as a
    /// project at all.
    /// </summary>
    private static ProjectRead? Project(string path, string folder, List<Diagnostic> diagnostics)
    {
        var shown = Path.GetRelativePath(folder, path).Replace('\\', '/');
        if (ProjectFile.Load(path, shown, out var problem) is not { } file)
        {
            diagnostics.Add(SourceCodes.UnreadableProject.At(new FileLocation(path), $"{Path.GetFileName(path)} can't be read as a project: {problem}."));
            return null;
        }

        if (Read(file, folder, new Dictionary<string, string>(), diagnostics) is not { } debug)
            return null;
        var release = Read(file, folder, new Dictionary<string, string> { [ProjectXml.Configuration] = ProjectXml.Release }, []);
        if (release is not null && Difference(debug, release) is { } differs)
        {
            diagnostics.Add(SourceCodes.PartNotRead.At(
                file.At(file.Root),
                $"{Path.GetFileName(path)} builds differently as Release than as Debug: {differs}. It is read as Debug, as dotnet build builds it unless told otherwise."));
        }

        return debug;
    }

    /// <summary>One project, evaluated with <paramref name="globals"/>, and what it gives the read.</summary>
    private static ProjectRead? Read(ProjectFile file, string folder, IReadOnlyDictionary<string, string> globals, List<Diagnostic> diagnostics)
    {
        if (ProjectEvaluator.Evaluate(file, folder, globals, diagnostics) is not { } project)
            return null;
        foreach (var problem in project.Problems)
            diagnostics.Add(problem);
        var settings = ProjectSettings.Read(project, diagnostics, out var collation);
        var scripts = ProjectFiles.Read(project, diagnostics);
        var references = ProjectReferences.SameDatabase(project, diagnostics);
        return new ProjectRead([.. scripts], settings, collation, references);
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
        if (!debug.References.Select(r => r.FullPath).SequenceEqual(release.References.Select(r => r.FullPath), StringComparer.OrdinalIgnoreCase))
            differs.Add("the projects it refers to");
        return differs.Count == 0 ? null : string.Join(", and ", differs);
    }

    private static string Describe(ScriptSettings settings) =>
        string.Join(", ", new[] { settings.AnsiNulls ? null : "ANSI_NULLS off", settings.QuotedIdentifier ? null : "QUOTED_IDENTIFIER off" }.OfType<string>());

    /// <summary>What one project gives the read: its scripts, its settings (the database's, if it is the one given), and the projects of its database it refers to.</summary>
    private sealed class ProjectRead(EquatableArray<ProjectScript> scripts, DatabaseSettings? settings, string? collation, IReadOnlyList<ProjectItem> references)
    {
        public EquatableArray<ProjectScript> Scripts { get; } = scripts;

        public DatabaseSettings? Settings { get; } = settings;

        public string? Collation { get; } = collation;

        public IReadOnlyList<ProjectItem> References { get; } = references;
    }
}

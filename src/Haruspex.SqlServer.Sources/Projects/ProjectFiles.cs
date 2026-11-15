using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The scripts a project's build reads, each with the settings it starts with: its <c>Build</c> items, as
/// <see cref="ProjectEvaluator"/> leaves them, less what the SDK takes out when the build runs.
/// </summary>
/// <remarks>
/// <para>
/// A <c>Microsoft.Build.Sql</c> project's <c>Build</c> items are every <c>.sql</c> file under its folder, less <c>bin</c> and
/// <c>obj</c>, less its <c>Build Remove</c>, <c>None</c> and deployment scripts, plus any it includes explicitly. Its build also
/// takes out, as it runs, each file a pre- or post-deployment script includes with <c>:r</c>, and (2.3.0, not 2.0.0) each
/// <c>PrePlan</c> script. A <c>Content</c> item neither adds to <c>Build</c> nor takes out of it (observed under 2.3.0: one the
/// pattern already holds is built, one outside the folder isn't, one after a <c>Build Remove</c> isn't). A legacy project's are
/// the scripts it lists.
/// </para>
/// <para>
/// A script starts with its item's <c>AnsiNulls</c> and <c>QuotedIdentifier</c> metadata (<c>On</c>, <c>Off</c>, <c>True</c> or
/// <c>False</c>, ignoring case), and without it with the project's properties of the same names, on when absent: the project's
/// are also each script's default (observed: a project with <c>QuotedIdentifier</c> <c>False</c> builds every script without the
/// metadata with it off).
/// </para>
/// </remarks>
internal static class ProjectFiles
{
    /// <summary>The item types a <c>Microsoft.Build.Sql</c> build takes out of <c>Build</c> (<c>PrePlan</c> by the versions that do).</summary>
    private static readonly string[] TakeOut =
    [
        ProjectXml.None, ProjectXml.PreDeploy, ProjectXml.PostDeploy, ProjectXml.BuildExtensionConfiguration, ProjectXml.DeploymentExtensionConfiguration,
    ];

    /// <summary>The deployment scripts, whose <c>:r</c> includes the build takes out too.</summary>
    private static readonly string[] Deployment = [ProjectXml.PreDeploy, ProjectXml.PostDeploy];

    /// <summary>The scripts of <paramref name="project"/>'s build, in its items' order, each once; what isn't read is reported.</summary>
    public static IReadOnlyList<ProjectScript> Read(EvaluatedProject project, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var name = Path.GetFileName(project.File.Path);
        var defaults = Defaults(project, diagnostics);
        var taken = TakenOut(project, diagnostics);
        var unsure = Unsure(project, taken, diagnostics);
        var scripts = new List<ProjectScript>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in project.OfType(ProjectXml.Build))
        {
            if (item.FullPath is not { } full)
            {
                diagnostics.Add(SourceCodes.ScriptNotFound.At(item.At, $"{name} builds {item.Spec}, which is rooted the way only Windows roots a path, so it names no place on this machine."));
                continue;
            }

            if (!seen.Add(full))
            {
                Duplicate(project, item, diagnostics);
                continue;
            }

            if (taken.Contains(full) || unsure.Contains(full))
                continue;
            if (DiskPath.Locate(full, item.Spec, out var problem) is not { } found)
            {
                diagnostics.Add(SourceCodes.ScriptNotFound.At(item.At, $"{name} builds {item.Spec}, which {problem ?? "isn't there"}."));
                continue;
            }

            if (Settings(item, defaults, diagnostics) is { } settings)
                scripts.Add(new ProjectScript(found, settings));
        }

        if (project.OfType(ProjectXml.Compile).Any())
        {
            diagnostics.Add(SourceCodes.PartNotRead.At(
                project.OfType(ProjectXml.Compile).First().At,
                $"{name} compiles C# files (Compile items), whose CLR objects its build adds to the database; they aren't read, so whatever uses one is unknown."));
        }

        return scripts;
    }

    /// <summary>
    /// The scripts of <c>Build</c> that a doubted item may take out, or whose settings it may change, each reported: a doubted
    /// <c>Build Remove</c> or <c>Build Update</c>, and for a <c>Microsoft.Build.Sql</c> project a doubted <c>None</c>, deployment
    /// script or <c>PrePlan</c> (where the version takes one out). When what the item names is known (only whether it applies is
    /// doubted), those are the scripts it names, and a deployment script's <c>:r</c> includes; when it isn't, every script of the
    /// project, since it could name any. None of them is read: a script the build may not read is never typed. A doubted
    /// <c>Build</c> include adds nothing, and says so.
    /// </summary>
    private static HashSet<string> Unsure(EvaluatedProject project, HashSet<string> taken, ICollection<Diagnostic> diagnostics)
    {
        var name = Path.GetFileName(project.File.Path);
        var folder = project.File.Folder;
        var built = project.OfType(ProjectXml.Build).Select(i => i.FullPath).OfType<string>().Where(p => !taken.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unsure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in project.Doubted)
        {
            var isBuild = item.Type.Equals(ProjectXml.Build, StringComparison.OrdinalIgnoreCase);
            if (isBuild && item.Verb == ProjectXml.Include)
            {
                Doubt.Report(diagnostics, $"The Build item {item.Written} of {name}, so the scripts it may add,", item.Doubts);
                continue;
            }

            var takesOut = isBuild || (project.Format.IsSdkStyle && item.Verb == ProjectXml.Include
                && (TakeOut.Contains(item.Type, StringComparer.OrdinalIgnoreCase) || (item.Type.Equals(ProjectXml.PrePlan, StringComparison.OrdinalIgnoreCase) && TakesOutPrePlan(project) != false)));
            if (!takesOut)
                continue;
            var named = item.Spec is not { } spec
                ? built
                : [.. built.Where(p => ItemSpecs.AnyNames(folder, spec, p)).Concat(Deployment.Contains(item.Type, StringComparer.OrdinalIgnoreCase) ? Includes(ItemSpecs.Files(folder, spec)) : [])
                    .Where(p => built.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (named.Count == 0)
                continue;
            unsure.UnionWith(named);
            var which = item.Spec is null ? $"No script of {name} is read" : $"{string.Join(", ", named.Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')))} {(named.Count == 1 ? "isn't" : "aren't")} read";
            var does = item.Verb == ProjectXml.Update ? "change the settings they start with" : "take them out of the build";
            var reported = SourceCodes.PartNotRead.At(
                item.At,
                $"{which}: the {item.Type} {item.Verb} \"{item.Written}\" of {name} may {does}, and whether it does isn't known: {string.Join("; ", item.Doubts.Select(d => d.Why).Distinct())}.");
            if (!diagnostics.Contains(reported))
                diagnostics.Add(reported);
        }

        return unsure;
    }

    /// <summary>
    /// A script the project's <c>Build</c> holds twice: a <c>Microsoft.Build.Sql</c> build with its default items stops at it
    /// (NETSDK1022, observed), so it is said; the script is read once.
    /// </summary>
    private static void Duplicate(EvaluatedProject project, ProjectItem item, ICollection<Diagnostic> diagnostics)
    {
        if (!project.Format.IsSdkStyle || project.Properties[ProjectXml.EnableDefaultSqlItems] is not { Text: var enabled } || !enabled.Equals("true", StringComparison.OrdinalIgnoreCase))
            return;
        var reported = SourceCodes.PartNotRead.At(
            item.At,
            $"{Path.GetFileName(project.File.Path)} includes {item.Spec} in Build, which its default items already hold: a build stops there (NETSDK1022, duplicate items). It is read once.");
        if (!diagnostics.Contains(reported))
            diagnostics.Add(reported);
    }

    /// <summary>The settings a script of the project starts with when its item doesn't say: the project's own; null for one doubted.</summary>
    private static (bool? AnsiNulls, bool? QuotedIdentifier) Defaults(EvaluatedProject project, ICollection<Diagnostic> diagnostics) =>
        (ProjectSettings.Switch(project, ProjectXml.AnsiNulls, absent: true, diagnostics),
         ProjectSettings.Switch(project, ProjectXml.QuotedIdentifier, absent: true, diagnostics));

    /// <summary>The script's settings, from its metadata or the project's; null, reported, when one isn't known.</summary>
    private static ScriptSettings? Settings(ProjectItem item, (bool? AnsiNulls, bool? QuotedIdentifier) defaults, ICollection<Diagnostic> diagnostics)
    {
        bool? Setting(string metadata, bool? project)
        {
            if (!item.Metadata.TryGetValue(metadata, out var value) || value is { IsSure: true, Text.Length: 0 })
            {
                if (project is null)
                    diagnostics.Add(SourceCodes.PartNotRead.At(item.At, $"{item.Spec} isn't read: it starts with the project's {metadata}, which isn't known."));
                return project;
            }

            if (!value.IsSure)
            {
                Doubt.Report(diagnostics, $"{metadata} of {item.Spec}, so the script,", value.Doubts);
                return null;
            }

            switch (value.Text.Trim().ToUpperInvariant())
            {
                case "ON" or "TRUE":
                    return true;
                case "OFF" or "FALSE":
                    return false;
                default:
                    diagnostics.Add(SourceCodes.PartNotRead.At(item.At, $"{item.Spec} has {metadata} \"{value.Text}\", which isn't On or Off, so the script isn't read."));
                    return null;
            }
        }

        var ansiNulls = Setting(ProjectXml.AnsiNulls, defaults.AnsiNulls);
        var quoted = Setting(ProjectXml.QuotedIdentifier, defaults.QuotedIdentifier);
        return ansiNulls is { } nulls && quoted is { } identifier ? new ScriptSettings(nulls, identifier) : null;
    }

    /// <summary>
    /// The files a <c>Microsoft.Build.Sql</c> build takes out of <c>Build</c> as it runs: each a deployment script includes with
    /// <c>:r</c>, and each <c>PrePlan</c> script where the version does. None for a legacy project.
    /// </summary>
    private static HashSet<string> TakenOut(EvaluatedProject project, ICollection<Diagnostic> diagnostics)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (project.Format.SdkVersion is not { } version)
            return taken;

        foreach (var script in project.OfType(ProjectXml.PreDeploy).Concat(project.OfType(ProjectXml.PostDeploy)))
        {
            if (script.FullPath is { } path)
                taken.UnionWith(Includes([path], script, diagnostics));
        }

        var preplan = project.OfType(ProjectXml.PrePlan).Select(i => i.FullPath).OfType<string>().ToList();
        if (preplan.Count > 0)
        {
            switch (TakesOutPrePlan(project))
            {
                case true:
                    taken.UnionWith(preplan);
                    break;
                case false:
                    break;
                default:
                    diagnostics.Add(SourceCodes.PartNotRead.At(
                        project.OfType(ProjectXml.PrePlan).First().At,
                        $"Whether Microsoft.Build.Sql {(version.Length > 0 ? version : "of the version global.json names")} builds a PrePlan script isn't known (2.0.0 does, 2.3.0 doesn't), so the PrePlan scripts aren't read."));
                    taken.UnionWith(preplan);
                    break;
            }
        }

        return taken;
    }

    /// <summary>
    /// Whether the project's SDK takes a <c>PrePlan</c> script out of <c>Build</c>: from 2.3.0 it does, to 2.0.0 it doesn't (both
    /// observed); null for a version between, or none named, which wasn't observed.
    /// </summary>
    private static bool? TakesOutPrePlan(EvaluatedProject project) =>
        Version.TryParse((project.Format.SdkVersion ?? "").Split('-')[0], out var version)
            ? version >= new Version(2, 3, 0) ? true : version <= new Version(2, 0, 0) ? false : null
            : null;

    /// <summary>
    /// Every file <paramref name="scripts"/> include with <c>:r</c>, and every file those include, as full paths; an include through a
    /// SQLCMD variable, which isn't evaluated, is reported when <paramref name="from"/> is given.
    /// </summary>
    private static HashSet<string> Includes(IEnumerable<string> scripts, ProjectItem? from = null, ICollection<Diagnostic>? diagnostics = null)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(scripts);
        while (pending.TryDequeue(out var script))
        {
            if (DiskPath.Locate(script, script, out _) is not { } file)
                continue;
            foreach (var written in SqlcmdIncludes.Of(File.ReadAllText(file)))
            {
                if (written.Contains("$(", StringComparison.Ordinal))
                {
                    if (from is not null)
                        diagnostics?.Add(SourceCodes.PartNotRead.At(from.At, $"{from.Spec} includes {written} with :r through a SQLCMD variable, which isn't evaluated, so whether a build leaves that script out isn't known."));
                    continue;
                }

                var included = DiskPath.Full(Path.GetDirectoryName(file) ?? ".", written);
                if (found.Add(included))
                    pending.Enqueue(included);
            }
        }

        return found;
    }
}

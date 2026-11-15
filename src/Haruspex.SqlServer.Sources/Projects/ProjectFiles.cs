using System.Text.RegularExpressions;
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
internal static partial class ProjectFiles
{
    /// <summary>The item types the SDK takes out of <c>Build</c>, whose doubted elements make the scripts unknown.</summary>
    private static readonly string[] ReadTypes =
    [
        ProjectXml.Build, ProjectXml.None, ProjectXml.PreDeploy, ProjectXml.PostDeploy, ProjectXml.PrePlan,
        ProjectXml.BuildExtensionConfiguration, ProjectXml.DeploymentExtensionConfiguration,
    ];

    /// <summary>The scripts of <paramref name="project"/>'s build, in its items' order, each once; what isn't read is reported.</summary>
    public static IReadOnlyList<ProjectScript> Read(EvaluatedProject project, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var name = Path.GetFileName(project.File.Path);
        foreach (var item in project.Doubted.Where(d => ReadTypes.Contains(d.Type, StringComparer.OrdinalIgnoreCase)))
            Doubt.Report(diagnostics, $"The {item.Type} item of {name}, so which scripts are built,", item.Doubts);

        var defaults = Defaults(project, diagnostics);
        var taken = TakenOut(project, diagnostics);
        var scripts = new List<ProjectScript>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in project.OfType(ProjectXml.Build))
        {
            if (item.FullPath is not { } full)
            {
                diagnostics.Add(SourceCodes.ScriptNotFound.At(item.At, $"{name} builds {item.Spec}, which is rooted the way only Windows roots a path, so it names no place on this machine."));
                continue;
            }

            if (taken.Contains(full) || !seen.Add(full))
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
                Included(path, script, taken, diagnostics);
        }

        var preplan = project.OfType(ProjectXml.PrePlan).Select(i => i.FullPath).OfType<string>().ToList();
        if (preplan.Count > 0)
        {
            switch (Version.TryParse(version.Split('-')[0], out var given) ? given : null)
            {
                case { } v when v >= new Version(2, 3, 0):
                    taken.UnionWith(preplan);
                    break;
                case { } v when v <= new Version(2, 0, 0):
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

    /// <summary>Each file <paramref name="path"/> includes with <c>:r</c>, and each they include, added to <paramref name="taken"/>.</summary>
    private static void Included(string path, ProjectItem from, HashSet<string> taken, ICollection<Diagnostic> diagnostics)
    {
        if (DiskPath.Locate(path, from.Spec, out _) is not { } found)
            return;
        foreach (Match include in Include().Matches(File.ReadAllText(found)))
        {
            var written = include.Groups["path"].Value.Trim().Trim('"');
            if (written.Contains("$(", StringComparison.Ordinal))
            {
                diagnostics.Add(SourceCodes.PartNotRead.At(from.At, $"{from.Spec} includes {written} with :r through a SQLCMD variable, which isn't evaluated, so whether a build leaves that script out isn't known."));
                continue;
            }

            var included = DiskPath.Full(Path.GetDirectoryName(found) ?? ".", written);
            if (taken.Add(included))
                Included(included, from, taken, diagnostics);
        }
    }

    /// <summary>A sqlcmd <c>:r</c> line: the command at the start of a line, then the path, quoted or not.</summary>
    [GeneratedRegex(@"^[ \t]*:r[ \t]+(?<path>[^\r\n]+?)[ \t]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Include();
}

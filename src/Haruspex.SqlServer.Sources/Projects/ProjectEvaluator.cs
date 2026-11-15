using System.Xml.Linq;
using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// Evaluates a project file as far as decision 0044 says, in MSBuild's order: a first pass over the project and every file it
/// imports sets the properties, in document order; a second sets the item definitions and the items, with the properties' final
/// values. What isn't evaluated is a <see cref="Doubt"/> on what it touches, or a <see cref="DoubtedItem"/>, or, for what can't be
/// confined to anything (an import not evaluated, a target), a diagnostic of its own.
/// </summary>
/// <remarks>
/// <para>
/// MSBuild imports files of its own around a project, and the reader doesn't read those: it builds in what they do that the
/// database's build reads. For a <c>Microsoft.Build.Sql</c> project, its <c>Sdk.props</c> is read before the project
/// (<see cref="SdkProps"/>) and its <c>Sdk.targets</c> after it (<see cref="SdkTargets"/>), as the SDK's own files were observed to
/// say in versions 1.0.0, 2.0.0 and 2.3.0. A legacy project imports <c>Microsoft.Common.props</c> and SSDT's
/// <c>Microsoft.Data.Tools.Schema.SqlTasks.targets</c> itself, by paths on the build machine and under conditions that hold
/// there; those imports are recognised by their file names, and stand for what they do.
/// </para>
/// <para>
/// Both import the nearest <c>Directory.Build.props</c> above the project's folder, before the project, and the nearest
/// <c>Directory.Build.targets</c>, after it, as MSBuild's common files do; any further one only if that file imports it.
/// </para>
/// </remarks>
internal sealed class ProjectEvaluator
{
    /// <summary>Elements of an item that MSBuild reads as how to apply it, not as its metadata.</summary>
    private static readonly HashSet<string> ItemAttributes = new(StringComparer.Ordinal)
    {
        ProjectXml.Include, ProjectXml.Exclude, ProjectXml.Remove, ProjectXml.Update, ProjectXml.Condition,
    };

    /// <summary>Attributes of an item that change how it applies in ways the reader doesn't evaluate.</summary>
    private static readonly HashSet<string> UnevaluatedItemAttributes = new(StringComparer.Ordinal)
    {
        ProjectXml.KeepMetadata, ProjectXml.RemoveMetadata, ProjectXml.KeepDuplicates, ProjectXml.MatchOnMetadata, ProjectXml.MatchOnMetadataOptions,
    };

    /// <summary>The item types whose items are files, named by paths relative to the project's folder.</summary>
    private static readonly HashSet<string> FileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ProjectXml.Build, ProjectXml.None, ProjectXml.Content, ProjectXml.PreDeploy, ProjectXml.PostDeploy, ProjectXml.PrePlan,
        ProjectXml.BuildExtensionConfiguration, ProjectXml.DeploymentExtensionConfiguration, ProjectXml.ProjectReference,
        ProjectXml.ArtifactReference, ProjectXml.Compile,
    };

    private readonly ProjectFile project;
    private readonly string shownRoot;
    private readonly ProjectProperties properties;
    private readonly List<Diagnostic> problems = [];
    private readonly List<Step> steps = [];
    private readonly HashSet<string> imported = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProjectItem> items = [];
    private readonly List<DoubtedItem> doubted = [];
    private readonly Dictionary<string, Dictionary<string, Evaluated>> definitions = new(StringComparer.OrdinalIgnoreCase);
    private bool targetsImported;

    private ProjectEvaluator(ProjectFile project, string shownRoot, IReadOnlyDictionary<string, string> globals)
    {
        this.project = project;
        this.shownRoot = shownRoot;
        properties = new ProjectProperties(project.Path, globals);
        imported.Add(project.Path);
    }

    /// <summary>The project's folder, which its items' paths are relative to.</summary>
    private string Folder => project.Folder;

    /// <summary>
    /// The project in <paramref name="project"/>, evaluated with the command line's <paramref name="globals"/>; null with a
    /// problem added to <paramref name="problems"/> when it isn't a project the reader reads.
    /// </summary>
    /// <param name="project">The project file, loaded.</param>
    /// <param name="shownRoot">The folder locations are shown relative to: the folder of the project read first.</param>
    /// <param name="globals">The properties a command line sets, such as <c>Configuration</c>.</param>
    /// <param name="problems">Where a project that can't be read says why.</param>
    public static EvaluatedProject? Evaluate(ProjectFile project, string shownRoot, IReadOnlyDictionary<string, string> globals, ICollection<Diagnostic> problems)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(problems);
        if (Format(project) is not { } format)
        {
            problems.Add(SourceCodes.UnreadableProject.At(
                project.At(project.Root),
                $"{Path.GetFileName(project.Path)} uses the SDK {Sdks(project).FirstOrDefault(s => !IsSqlSdk(s))}, which haruspex doesn't read: it reads Microsoft.Build.Sql projects and legacy SSDT ones."));
            return null;
        }

        var evaluator = new ProjectEvaluator(project, shownRoot, globals);
        if (format.IsSdkStyle)
            evaluator.SdkProps();
        evaluator.Walk(project, []);
        if (format.IsSdkStyle)
            evaluator.SdkTargets();
        evaluator.Items();
        return new EvaluatedProject(project, format, evaluator.properties, evaluator.items, evaluator.doubted, evaluator.problems);
    }

    /// <summary>
    /// The project's format: <c>Microsoft.Build.Sql</c> when it names that SDK (in <c>Project Sdk</c>, an <c>Sdk</c> element or an
    /// SDK import), legacy when it names no SDK; null when it names another.
    /// </summary>
    private static ProjectFormat? Format(ProjectFile project)
    {
        var sdks = Sdks(project).ToList();
        if (sdks.Count == 0)
            return ProjectFormat.Legacy;
        if (!sdks.All(IsSqlSdk))
            return null;
        var named = sdks[0].Split('/', 2);
        return new ProjectFormat(named.Length > 1 ? named[1].Trim() : "");
    }

    /// <summary>Every SDK the project names, as <c>Name/Version</c> or <c>Name</c>.</summary>
    private static IEnumerable<string> Sdks(ProjectFile project)
    {
        foreach (var sdk in ((string?)project.Root.Attribute(ProjectXml.Sdk) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return sdk;
        foreach (var element in project.Root.Elements().Where(e => e.Name.LocalName == ProjectXml.Sdk))
            yield return (string?)element.Attribute("Version") is { Length: > 0 } version ? $"{(string?)element.Attribute("Name")}/{version}" : (string?)element.Attribute("Name") ?? "";
    }

    private static bool IsSqlSdk(string sdk) => sdk.Split('/')[0].Trim().Equals(ProjectXml.MicrosoftBuildSql, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the SDK's <c>Sdk.props</c> does before the project: imports <c>Directory.Build.props</c>, gives <c>Configuration</c>,
    /// <c>Platform</c> and the output folders their defaults, and includes every <c>.sql</c> file (<see cref="StepKind.DefaultItems"/>).
    /// </summary>
    private void SdkProps()
    {
        DirectoryBuildFile(ProjectXml.DirectoryBuildProps, ProjectXml.ImportDirectoryBuildProps);
        properties.Default(ProjectXml.Configuration, ProjectXml.Debug);
        properties.Default(ProjectXml.Platform, "AnyCPU");
        properties.Default(ProjectXml.BaseOutputPath, "bin\\");
        properties.Default(ProjectXml.BaseIntermediateOutputPath, "obj\\");
        steps.Add(new Step(StepKind.DefaultItems, project, project.Root, []));
    }

    /// <summary>
    /// What the SDK's <c>Sdk.targets</c> does after the project: imports <c>Directory.Build.targets</c>, takes the deployment and
    /// <c>None</c> items out of <c>Build</c> (<see cref="StepKind.SdkRemoves"/>), and gives <c>EnableDefaultSqlItems</c> and
    /// <c>DefaultSqlItemExcludes</c> their values, last, so that the project's own count.
    /// </summary>
    private void SdkTargets()
    {
        DirectoryBuildFile(ProjectXml.DirectoryBuildTargets, ProjectXml.ImportDirectoryBuildTargets);
        targetsImported = true;
        steps.Add(new Step(StepKind.SdkRemoves, project, project.Root, []));
        properties.Default(ProjectXml.EnableDefaultSqlItems, "true");
        // The SDK adds to the project's own excludes; a project that sets none has none, which is no doubt.
        properties.Default(ProjectXml.DefaultSqlItemExcludes, "");
        var at = project.At(project.Root);
        properties.Set(ProjectXml.DefaultSqlItemExcludes, properties.Expand(
            $"$({ProjectXml.DefaultSqlItemExcludes});$({ProjectXml.BaseOutputPath})/**;$({ProjectXml.BaseIntermediateOutputPath})/**", project, at));
    }

    /// <summary>The first pass over <paramref name="file"/>'s elements, in order, each doubted for <paramref name="forced"/> too.</summary>
    private void Walk(ProjectFile file, EquatableArray<Doubt> forced) => Walk(file, file.Root.Elements(), forced);

    private void Walk(ProjectFile file, IEnumerable<XElement> elements, EquatableArray<Doubt> forced)
    {
        foreach (var element in elements)
        {
            switch (element.Name.LocalName)
            {
                case ProjectXml.PropertyGroup:
                    var group = Condition(element, file).And(new Truth(Holds: true, forced));
                    foreach (var property in element.Elements())
                        SetProperty(property, file, group);
                    break;
                case ProjectXml.ItemGroup or ProjectXml.ItemDefinitionGroup:
                    steps.Add(new Step(element.Name.LocalName == ProjectXml.ItemGroup ? StepKind.Items : StepKind.Definitions, file, element, forced));
                    break;
                case ProjectXml.Import:
                    Import(element, file, Truth.Always);
                    break;
                case ProjectXml.ImportGroup:
                    var imports = Condition(element, file);
                    foreach (var import in element.Elements().Where(e => e.Name.LocalName == ProjectXml.Import))
                        Import(import, file, imports);
                    break;
                case ProjectXml.Choose:
                    var choose = new Doubt(file.At(element), "Choose isn't evaluated, so whether what its When and Otherwise set is set isn't known");
                    foreach (var branch in element.Elements())
                        Walk(file, branch.Elements(), [.. forced, choose]);
                    break;
                case ProjectXml.Target when element.HasElements:
                    problems.Add(SourceCodes.PartNotRead.At(
                        file.At(element),
                        $"The target {(string?)element.Attribute("Name")} runs during a build, and what it does isn't read: a script it adds or takes out, or a setting it changes, is not seen."));
                    break;
            }
        }
    }

    /// <summary>One property of a group: set when the group's condition and its own hold, doubted when either is.</summary>
    private void SetProperty(XElement property, ProjectFile file, Truth group)
    {
        var at = file.At(property);
        var when = group.And(Condition(property, file));
        var value = properties.Expand(property.Value, file, at);
        if (property.HasElements)
            value = value.Doubted([new Doubt(at, $"{property.Name.LocalName} holds XML, which isn't evaluated")]);
        properties.Set(property.Name.LocalName, value, when);
    }

    /// <summary>An element's <c>Condition</c>, evaluated; one without a condition holds.</summary>
    private Truth Condition(XElement element, ProjectFile file)
    {
        if (element.Attribute(ProjectXml.Condition) is not { } condition)
            return Truth.Always;
        var at = file.At(condition);
        return ConditionEvaluator.Evaluate(condition.Value, raw => properties.Expand(raw, file, at), Folder, at);
    }

    /// <summary>
    /// An <c>Import</c>: the SDK's and SSDT's own recognised by their names and stood for; any other file evaluated as the project
    /// is, when its path and condition are; otherwise reported, since what an import defines can be anything.
    /// </summary>
    private void Import(XElement element, ProjectFile file, Truth outer)
    {
        var at = file.At(element);
        var written = (string?)element.Attribute(ProjectXml.Project) ?? "";
        if ((string?)element.Attribute(ProjectXml.Sdk) is { } sdk)
        {
            if (!IsSqlSdk(sdk))
                problems.Add(SourceCodes.PartNotRead.At(at, $"The import of {written} from the SDK {sdk} isn't read: what it defines is not seen."));
            return;
        }

        var name = written[(written.LastIndexOfAny(['/', '\\']) + 1)..];
        if (name.Equals("Microsoft.Common.props", StringComparison.OrdinalIgnoreCase))
        {
            // MSBuild's own, which a legacy project imports by a path on the build machine: it imports Directory.Build.props.
            DirectoryBuildFile(ProjectXml.DirectoryBuildProps, ProjectXml.ImportDirectoryBuildProps);
            return;
        }

        if (name.Equals("Microsoft.Data.Tools.Schema.SqlTasks.targets", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Microsoft.Common.targets", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Microsoft.CSharp.targets", StringComparison.OrdinalIgnoreCase))
        {
            // SSDT's build, which a legacy project imports twice under opposite conditions; MSBuild's common targets, which it
            // imports, import Directory.Build.targets.
            if (!targetsImported)
                DirectoryBuildFile(ProjectXml.DirectoryBuildTargets, ProjectXml.ImportDirectoryBuildTargets);
            targetsImported = true;
            return;
        }

        var when = outer.And(Condition(element, file));
        var path = properties.Expand(written, file, at);
        if (!when.Doubts.Concat(path.Doubts).Any() && !when.Holds)
            return;
        string? problem;
        if (when.Doubts.Count > 0 || path.Doubts.Count > 0)
        {
            problem = string.Join("; ", when.Doubts.Concat(path.Doubts).Select(d => d.Why).Distinct());
        }
        else if (Glob.IsPattern(path.Text))
        {
            problem = "an import of a pattern isn't evaluated";
        }
        else if (DiskPath.Find(file.Folder, path.Text, out var notFound) is { } found)
        {
            ImportFile(found, at);
            return;
        }
        else
        {
            problem = notFound ?? $"{path.Text} isn't found, where a build would stop";
        }

        problems.Add(SourceCodes.PartNotRead.At(at, $"The import of {written} isn't read: {problem}. What it defines is not seen."));
    }

    /// <summary>A file the project imports, evaluated in place, each once, as MSBuild imports a file once.</summary>
    private void ImportFile(string path, SourceLocation at)
    {
        if (!imported.Add(path))
            return;
        if (ProjectFile.Load(path, Shown(path), out var problem) is not { } file)
        {
            problems.Add(SourceCodes.PartNotRead.At(at, $"The import of {Shown(path)} isn't read: {problem}. What it defines is not seen."));
            return;
        }

        Walk(file, []);
    }

    /// <summary>
    /// The nearest <paramref name="name"/> (<c>Directory.Build.props</c> or <c>.targets</c>) in the project's folder or a folder
    /// above it, imported, unless the property <paramref name="unless"/> is false, as MSBuild's common files do.
    /// </summary>
    private void DirectoryBuildFile(string name, string unless)
    {
        if (properties[unless] is { Text: var off } && off.Equals("false", StringComparison.OrdinalIgnoreCase))
            return;
        for (var folder = Folder; folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (Directory.Exists(folder) && Directory.EnumerateFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                ImportFile(found, project.At(project.Root));
                return;
            }
        }
    }

    /// <summary>A path as locations show it: relative to the folder of the project read first, with <c>/</c>.</summary>
    private string Shown(string path) => Path.GetRelativePath(shownRoot, path).Replace('\\', '/');

    /// <summary>The second pass: every item definition, then every item, in order.</summary>
    private void Items()
    {
        foreach (var step in steps.Where(s => s.Kind == StepKind.Definitions))
            Definitions(step);
        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case StepKind.DefaultItems:
                    DefaultItems(step);
                    break;
                case StepKind.SdkRemoves:
                    SdkRemoves();
                    break;
                case StepKind.Items:
                    var group = Condition(step.Element, step.File).And(new Truth(Holds: true, step.Forced));
                    foreach (var item in step.Element.Elements())
                        Item(item, step.File, group);
                    break;
            }
        }
    }

    /// <summary>An <c>ItemDefinitionGroup</c>: metadata every later item of a type starts with.</summary>
    private void Definitions(Step step)
    {
        var group = Condition(step.Element, step.File).And(new Truth(Holds: true, step.Forced));
        foreach (var type in step.Element.Elements())
        {
            if (!definitions.TryGetValue(type.Name.LocalName, out var metadata))
                definitions[type.Name.LocalName] = metadata = new(StringComparer.OrdinalIgnoreCase);
            SetMetadata(type, step.File, group.And(Condition(type, step.File)), metadata);
        }
    }

    /// <summary>The SDK's own <c>Build</c> item: every <c>.sql</c> file under the project's folder, less <c>DefaultSqlItemExcludes</c>.</summary>
    private void DefaultItems(Step step)
    {
        var at = step.File.At(step.Element);
        var enabled = properties[ProjectXml.EnableDefaultSqlItems] ?? Evaluated.Sure("");
        if (!enabled.IsSure)
        {
            doubted.Add(new DoubtedItem(ProjectXml.Build, at, enabled.Doubts));
            return;
        }

        if (!enabled.Text.Equals("true", StringComparison.OrdinalIgnoreCase))
            return;
        var excludes = properties[ProjectXml.DefaultSqlItemExcludes] ?? Evaluated.Sure("");
        if (!excludes.IsSure)
        {
            doubted.Add(new DoubtedItem(ProjectXml.Build, at, excludes.Doubts));
            return;
        }

        Include(ProjectXml.Build, "**/*.sql", excludes.Text, at, null, step.File);
    }

    /// <summary>
    /// The SDK's <c>Build Remove</c> of every deployment script and <c>None</c> item, after the project. <c>PrePlan</c> is taken out
    /// too by the versions that do so (observed: 2.3.0 does, 2.0.0 and 1.0.0 don't); <c>ProjectFiles</c> says which.
    /// </summary>
    private void SdkRemoves()
    {
        foreach (var type in (string[])[ProjectXml.PreDeploy, ProjectXml.PostDeploy, ProjectXml.None, ProjectXml.BuildExtensionConfiguration, ProjectXml.DeploymentExtensionConfiguration])
        {
            var paths = items.Where(i => i.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && i.FullPath is not null).Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            items.RemoveAll(i => i.Type.Equals(ProjectXml.Build, StringComparison.OrdinalIgnoreCase) && i.FullPath is { } path && paths.Contains(path));
        }
    }

    /// <summary>One item element: an <c>Include</c>, a <c>Remove</c> or an <c>Update</c>, applied when it and its group hold.</summary>
    private void Item(XElement element, ProjectFile file, Truth group)
    {
        var type = element.Name.LocalName;
        var at = file.At(element);
        var when = group.And(Condition(element, file));
        var unevaluated = element.Attributes().Where(a => UnevaluatedItemAttributes.Contains(a.Name.LocalName)).Select(a => new Doubt(at, $"{a.Name.LocalName} isn't evaluated"));
        var doubts = Doubt.Union(when.Doubts, unevaluated);
        if (doubts.Count == 0 && !when.Holds)
            return;

        var include = (string?)element.Attribute(ProjectXml.Include);
        var remove = (string?)element.Attribute(ProjectXml.Remove);
        var update = (string?)element.Attribute(ProjectXml.Update);
        var spec = properties.Expand(include ?? remove ?? update ?? "", file, at);
        var exclude = properties.Expand((string?)element.Attribute(ProjectXml.Exclude) ?? "", file, at);
        doubts = Doubt.Union(doubts, spec.Doubts.Concat(exclude.Doubts));
        if (doubts.Count > 0)
        {
            doubted.Add(new DoubtedItem(type, at, doubts));
            return;
        }

        if (include is not null)
        {
            Include(type, spec.Text, exclude.Text, at, element, file);
        }
        else if (remove is not null)
        {
            items.RemoveAll(i => i.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && Matches(i, spec.Text));
        }
        else if (update is not null)
        {
            foreach (var item in items.Where(i => i.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && Matches(i, spec.Text)))
                SetMetadata(element, file, Truth.Always, item.Metadata);
        }
    }

    /// <summary>Adds an item for each path or name <paramref name="specs"/> lists, a pattern for each file it names less <paramref name="excludes"/>.</summary>
    private void Include(string type, string specs, string excludes, SourceLocation at, XElement? element, ProjectFile file)
    {
        var left = Specs(excludes).ToList();
        foreach (var spec in Specs(specs))
        {
            var added = new List<ProjectItem>();
            if (!FileTypes.Contains(type))
                added.Add(new ProjectItem(type, spec, null, at));
            else if (Glob.IsPattern(spec))
                added.AddRange(Glob.Of(Folder, spec).Files().Where(f => !left.Any(x => Names(x, f))).Select(f => new ProjectItem(type, Path.GetRelativePath(Folder, f), f, at)));
            else if (!left.Any(x => Names(x, DiskPath.Full(Folder, spec))))
                added.Add(new ProjectItem(type, spec, DiskPath.IsRootedElsewhere(spec) ? null : DiskPath.Full(Folder, spec), at));

            foreach (var item in added)
            {
                if (definitions.TryGetValue(type, out var defaults))
                {
                    foreach (var (name, value) in defaults)
                        item.Metadata[name] = value;
                }

                if (element is not null)
                    SetMetadata(element, file, Truth.Always, item.Metadata);
                items.Add(item);
            }
        }
    }

    /// <summary>Whether <paramref name="item"/> is one <paramref name="specs"/> names: by pattern or path for a file, by name otherwise, ignoring case.</summary>
    private bool Matches(ProjectItem item, string specs) =>
        Specs(specs).Any(spec => item.FullPath is { } path ? Names(spec, path) : item.Spec.Equals(spec, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the path or pattern <paramref name="spec"/> names the file at <paramref name="path"/>, ignoring case.</summary>
    private bool Names(string spec, string path) =>
        Glob.IsPattern(spec) ? Glob.Of(Folder, spec).Matches(path) : DiskPath.Full(Folder, spec).Equals(path, StringComparison.OrdinalIgnoreCase);

    private static string[] Specs(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// An item element's metadata into <paramref name="into"/>: its attributes other than how it applies, and its child elements,
    /// each under its own condition and <paramref name="when"/>.
    /// </summary>
    private void SetMetadata(XElement element, ProjectFile file, Truth when, Dictionary<string, Evaluated> into)
    {
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration && !ItemAttributes.Contains(a.Name.LocalName) && !UnevaluatedItemAttributes.Contains(a.Name.LocalName)))
            Set(attribute.Name.LocalName, properties.Expand(attribute.Value, file, file.At(attribute)), when);
        foreach (var child in element.Elements())
            Set(child.Name.LocalName, properties.Expand(child.Value, file, file.At(child)), when.And(Condition(child, file)));

        void Set(string name, Evaluated value, Truth holds)
        {
            var before = into.GetValueOrDefault(name) ?? Evaluated.Sure("");
            if (holds.Doubts.Count > 0)
                into[name] = (holds.Holds ? value : before).Doubted(Doubt.Union(before.Doubts.Concat(value.Doubts), holds.Doubts));
            else if (holds.Holds)
                into[name] = value;
        }
    }

    /// <summary>What the second pass does at a place in the first: an item group, item definitions, or one of the SDK's own steps.</summary>
    private enum StepKind
    {
        Items,
        Definitions,
        DefaultItems,
        SdkRemoves,
    }

    /// <summary>A place in the first pass the second comes back to, with the doubts a <c>Choose</c> around it adds.</summary>
    private sealed class Step(StepKind kind, ProjectFile file, XElement element, EquatableArray<Doubt> forced)
    {
        public StepKind Kind { get; } = kind;

        public ProjectFile File { get; } = file;

        public XElement Element { get; } = element;

        public EquatableArray<Doubt> Forced { get; } = forced;
    }
}

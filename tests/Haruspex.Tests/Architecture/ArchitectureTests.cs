using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// Enforces the layering in <c>docs/architecture.md</c>: each project under <c>src/</c> is a layer in that document's
/// table, which this test reads, and refers only to what its layer may know. Project references are the first
/// enforcement; this test is the second, because a dependency direction erodes one convenient reference at a time.
/// </summary>
/// <remarks>
/// The table is read from the document rather than copied here, so it exists once; the document says why it is the
/// source. Two things are read for each layer. The compiled assembly's references, which are what its code really
/// uses (the compiler drops a reference nothing uses). And the project file's references, which are what a
/// published package would depend on even when unused. The second also covers what the first cannot see: a
/// constant from another assembly is inlined, leaving no reference behind. Framework assemblies are allowed
/// everywhere except those named in <see cref="Layer.ForbiddenFramework"/>; every other assembly must be allowed
/// by name. Some framework types share an assembly with everything else (the file system's are in
/// System.Runtime), so for those the assembly's type references are read too (<see cref="Layer.ForbiddenTypes"/>).
/// </remarks>
public class ArchitectureTests
{
    /// <summary>Files, zips and XML: what a layer whose row says it never handles them may not refer to.</summary>
    private static readonly string[] SourceFormats = ["System.Xml", "System.IO.Compression", "System.IO.Packaging"];

    /// <summary>
    /// The file system's types. They live in System.Runtime with everything else, so no assembly reference shows
    /// them; the type references do.
    /// </summary>
    private static readonly string[] FileSystem =
        ["System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileStream", "System.IO.FileSystemInfo"];

    private static readonly string Document = Path.Combine(RepositoryPaths.Root, "docs", "architecture.md");

    /// <summary>The table of layers in <c>docs/architecture.md</c>, read from it.</summary>
    private static readonly Layer[] Layers = ReadLayers();

    [Fact]
    public void Every_project_under_src_is_a_layer_in_the_table()
    {
        var notLayers = Projects().Keys.Where(name => Layers.All(l => l.Name != name)).ToList();

        Assert.True(notLayers.Count == 0, $"Not a layer in the table: {string.Join(", ", notLayers)}. Split it into layers, or add the layer's row to the table in docs/architecture.md.");
    }

    [Fact]
    public void Every_layer_with_code_today_has_its_project()
    {
        var projects = Projects();
        var missing = Layers.Where(l => !l.Planned && !projects.ContainsKey(l.Name)).Select(l => l.Name).ToList();

        Assert.True(missing.Count == 0, $"No project under src/ for: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// The IR knows no target language: which words a language reserves, how it writes an import, and anything else an
    /// emitter needs of its own target belong to that emitter (decision 0003, and <c>src/Haruspex.Ir/README.md</c>). A
    /// reference can't show it, since such knowledge is plain strings, so this reads the names the IR's assembly declares:
    /// a type or member named for a language or for its keywords is the knowledge coming back.
    /// </summary>
    [Fact]
    public void The_ir_declares_nothing_named_for_a_target_language()
    {
        string[] targets = ["TypeScript", "JavaScript", "ECMAScript", "Zod", "Reserved", "Keyword"];
        var names = typeof(Haruspex.Ir.IrDocument).Assembly.GetTypes()
            .SelectMany(t => t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(m => $"{t.Name}.{m.Name}").Prepend(t.Name));

        Assert.Empty(names.Where(n => targets.Any(target => n.Contains(target, StringComparison.OrdinalIgnoreCase))).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_assembly_refers_only_to_what_its_layer_may_know()
    {
        var violations = new List<string>();
        var assemblyToLayer = Projects().ToDictionary(p => p.Value.AssemblyName, p => p.Key, StringComparer.Ordinal);

        foreach (var (name, project) in Projects())
        {
            var path = Path.Combine(AppContext.BaseDirectory, project.AssemblyName + ".dll");
            if (!File.Exists(path))
            {
                violations.Add($"{name}: {project.AssemblyName}.dll is not in the test output; reference the project from Haruspex.Tests.");
                continue;
            }

            var layer = Layers.FirstOrDefault(l => l.Name == name);
            foreach (var reference in AssemblyReferences(path))
            {
                if (assemblyToLayer.TryGetValue(reference, out var referencedLayer))
                {
                    if (layer is null || !layer.Knows.Contains(referencedLayer))
                        violations.Add($"{name} refers to the layer {referencedLayer}, which {Describe(layer, name)} may not know.");
                }
                else if (reference.StartsWith("Haruspex", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{name} refers to {reference}, which is not a layer in the table.");
                }
                else if (IsFramework(reference))
                {
                    if (layer?.ForbiddenFramework.FirstOrDefault(f => reference.StartsWith(f, StringComparison.Ordinal)) is { } forbidden)
                        violations.Add($"{name} refers to {reference} ({forbidden}), which {Describe(layer, name)} may not know.");
                }
                else if (layer is null || !layer.Packages.Contains(reference))
                {
                    violations.Add($"{name} refers to the package assembly {reference}, which {Describe(layer, name)} may not use.");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Each_assembly_uses_no_type_its_layer_may_not_know()
    {
        var violations = new List<string>();
        foreach (var (name, project) in Projects())
        {
            var path = Path.Combine(AppContext.BaseDirectory, project.AssemblyName + ".dll");
            if (Layers.FirstOrDefault(l => l.Name == name) is not { ForbiddenTypes.Length: > 0 } layer || !File.Exists(path))
                continue;

            foreach (var type in TypeReferences(path).Where(layer.ForbiddenTypes.Contains))
                violations.Add($"{name} uses {type}, which its layer may not know.");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Each_project_file_refers_only_to_what_its_layer_may_know()
    {
        var violations = new List<string>();
        foreach (var (name, project) in Projects())
        {
            var layer = Layers.FirstOrDefault(l => l.Name == name);
            foreach (var reference in project.ProjectReferences.Where(r => layer is null || !layer.Knows.Contains(r)))
                violations.Add($"{name}.csproj has a ProjectReference to {reference}, which {Describe(layer, name)} may not know.");
            foreach (var reference in project.PackageReferences.Where(r => layer is null || !layer.Packages.Contains(r)))
                violations.Add($"{name}.csproj has a PackageReference to {reference}, which {Describe(layer, name)} may not use.");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_table_only_allows_layers_listed_in_it()
    {
        // A typo in the table would otherwise allow nothing, silently.
        var unknown = Layers.SelectMany(l => l.Knows.Select(r => (l.Name, r))).Where(x => Layers.All(l => l.Name != x.r)).ToList();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// Reads the rows of the table under "The table" in <c>docs/architecture.md</c>. A layer's name is in backticks,
    /// followed by <c>(planned)</c> when it has no code yet. "May refer to" is <c>nothing</c>, <c>every layer</c> or
    /// short layer names, then after a semicolon the packages in backticks. "Files, zips, XML" is <c>may</c> or
    /// <c>never</c>. Anything else fails, so a typo can't allow nothing silently.
    /// </summary>
    private static Layer[] ReadLayers()
    {
        var lines = File.ReadAllLines(Document);
        var start = Array.IndexOf(lines, "## The table");
        var rows = lines.Skip(start + 1).SkipWhile(l => !l.StartsWith('|')).TakeWhile(l => l.StartsWith('|')).Skip(2)
            .Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .ToList();
        if (start < 0 || rows.Count == 0)
            throw new InvalidOperationException($"{Document} has no table under \"## The table\".");

        var names = rows.Select(r => Name(r[0])).ToList();
        string Short(string name) => name.Replace("Haruspex.", "", StringComparison.Ordinal).Replace("SqlServer.", "", StringComparison.Ordinal);

        return [.. rows.Select(row =>
        {
            if (row.Length != 5)
                throw new InvalidOperationException($"{Document}: a row of the layer table has {row.Length} cells, not 5: {string.Join(" | ", row)}");
            var name = Name(row[0]);
            var mayReferTo = row[3].Split(';', 2);
            var layers = mayReferTo[0].Trim() switch
            {
                "nothing" => [],
                "every layer" => [.. names.Where(n => n != name)],
                var list => list.Split(',').Select(l => names.SingleOrDefault(n => Short(n) == l.Trim())
                    ?? throw new InvalidOperationException($"{Document}: {name} may refer to \"{l.Trim()}\", which is not a layer in the table.")).ToArray(),
            };
            string[] packages = mayReferTo.Length == 2 ? [.. mayReferTo[1].Split(',').Select(p => p.Trim().Trim('`'))] : [];
            var neverFiles = row[4] switch
            {
                "never" => true,
                "may" => false,
                var other => throw new InvalidOperationException($"{Document}: {name}'s \"Files, zips, XML\" is \"{other}\", not \"may\" or \"never\"."),
            };
            return new Layer(name, layers, packages,
                ForbiddenFramework: neverFiles ? SourceFormats : null,
                Planned: row[0].EndsWith("(planned)", StringComparison.Ordinal),
                ForbiddenTypes: neverFiles ? FileSystem : null);
        })];

        string Name(string cell) => cell.Split('`') is [_, var inside, ..] ? inside : throw new InvalidOperationException($"{Document}: no layer name in backticks in \"{cell}\".");
    }

    private static string Describe(Layer? layer, string name) => layer is null ? $"{name}, not being a layer," : "its layer";

    /// <param name="Name">The project's name, which is also its package id.</param>
    /// <param name="Knows">The layers it may refer to.</param>
    /// <param name="Packages">The assemblies of third-party packages it may refer to.</param>
    /// <param name="ForbiddenFramework">Prefixes of framework assemblies it must not refer to.</param>
    /// <param name="Planned">The layer has no code yet, so its project may not exist.</param>
    /// <param name="ForbiddenTypes">Full names of framework types it must not use.</param>
    private sealed record Layer(string Name, string[] Knows, string[] Packages, string[]? ForbiddenFramework = null, bool Planned = false, string[]? ForbiddenTypes = null)
    {
        public string[] ForbiddenFramework { get; } = ForbiddenFramework ?? [];

        public string[] ForbiddenTypes { get; } = ForbiddenTypes ?? [];
    }

    private sealed record Project(string AssemblyName, IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> PackageReferences);

    private static SortedDictionary<string, Project> Projects()
    {
        var projects = new SortedDictionary<string, Project>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var xml = XDocument.Load(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var assemblyName = xml.Descendants("AssemblyName").Select(e => e.Value.Trim()).FirstOrDefault() ?? name;
            var projectReferences = xml.Descendants("ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
                .ToList();
            var packageReferences = xml.Descendants("PackageReference").Select(e => e.Attribute("Include")!.Value).ToList();
            projects[name] = new Project(assemblyName, projectReferences, packageReferences);
        }

        return projects;
    }

    private static List<string> AssemblyReferences(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return [.. metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name))];
    }

    /// <summary>The full names of the types the assembly refers to in other assemblies, nested types included.</summary>
    private static List<string> TypeReferences(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return [.. metadata.TypeReferences.Select(h => FullName(metadata, h))];
    }

    private static string FullName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var type = metadata.GetTypeReference(handle);
        var name = metadata.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? FullName(metadata, (TypeReferenceHandle)type.ResolutionScope) + "+" + name
            : metadata.GetString(type.Namespace) is { Length: > 0 } ns ? ns + "." + name : name;
    }

    /// <summary>An assembly of the shared framework the tests run on, which every layer may use.</summary>
    private static bool IsFramework(string assemblyName) =>
        assemblyName is "netstandard" or "mscorlib"
        || File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), assemblyName + ".dll"));
}

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Haruspex.Tests;

/// <summary>
/// Enforces the layering in <c>docs/architecture.md</c>: each project under <c>src/</c> is a layer in the table
/// below, and refers only to what its layer may know. Project references are the first enforcement; this test
/// is the second, because a dependency direction erodes one convenient reference at a time.
/// </summary>
/// <remarks>
/// Two things are read for each layer. The compiled assembly's references, which are what its code really
/// uses (the compiler drops a reference nothing uses). And the project file's references, which are what a
/// published package would depend on even when unused. The second also covers what the first cannot see: a
/// constant from another assembly is inlined, leaving no reference behind. Framework assemblies are allowed
/// everywhere except those named in <see cref="Layer.ForbiddenFramework"/>; every other assembly must be allowed
/// by name. Some framework types share an assembly with everything else (the file system's are in
/// System.Runtime), so for those the assembly's type references are read too (<see cref="Layer.ForbiddenTypes"/>).
/// </remarks>
public class ArchitectureTests
{
    private const string ScriptDom = "Microsoft.SqlServer.TransactSql.ScriptDom";

    private const string Diagnostics = "Haruspex.Diagnostics";
    private const string Model = "Haruspex.SqlServer.Model";
    private const string Sources = "Haruspex.SqlServer.Sources";
    private const string Semantics = "Haruspex.SqlServer.Semantics";
    private const string Inference = "Haruspex.SqlServer.Inference";
    private const string Ir = "Haruspex.Ir";
    private const string Config = "Haruspex.Config";
    private const string EmitZod = "Haruspex.Emit.Zod";
    private const string EmitJsonSchema = "Haruspex.Emit.JsonSchema";
    private const string Cli = "Haruspex.Cli";

    /// <summary>Files, zips and XML: what only the layer that reads sources may know.</summary>
    private static readonly string[] SourceFormats = ["System.Xml", "System.IO.Compression", "System.IO.Packaging"];

    /// <summary>
    /// The file system's types. They live in System.Runtime with everything else, so no assembly reference shows
    /// them; the type references do.
    /// </summary>
    private static readonly string[] FileSystem =
        ["System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileStream", "System.IO.FileSystemInfo"];

    /// <summary>
    /// The table of layers (docs/architecture.md, from haruspex-design.md "Layers and what each may know"). A
    /// layer may refer to the layers and packages listed for it, and to nothing else of this repository's.
    /// </summary>
    private static readonly Layer[] Layers =
    [
        new(Diagnostics, Knows: [], Packages: []),

        // Bodies are kept as ScriptDom fragments, so the model needs ScriptDom; it must not know where the
        // schema came from (Sources) or what a body's references point to (Semantics).
        new(Model, Knows: [Diagnostics], Packages: [ScriptDom], ForbiddenFramework: SourceFormats, ForbiddenTypes: FileSystem),

        // The only layer that reads files, and later a DACPAC's zip and XML.
        new(Sources, Knows: [Diagnostics, Model], Packages: [ScriptDom]),

        // Binding needs the model, never how it was read, and never the IR.
        new(Semantics, Knows: [Diagnostics, Model], Packages: [ScriptDom], ForbiddenFramework: SourceFormats, ForbiddenTypes: FileSystem),

        // From bound bodies into the IR: not files, zips or XML, the emitters or the config file.
        new(Inference, Knows: [Diagnostics, Model, Semantics, Ir], Packages: [ScriptDom], ForbiddenFramework: SourceFormats, ForbiddenTypes: FileSystem),

        // The IR knows no SQL of any dialect.
        new(Ir, Knows: [], Packages: []),

        // Not written yet. Selection patterns name model objects, so it may see the model, but not SQL syntax.
        new(Config, Knows: [Diagnostics, Model, Ir], Packages: [], Planned: true),

        // Emitters see only the IR.
        new(EmitZod, Knows: [Ir], Packages: []),
        new(EmitJsonSchema, Knows: [Ir], Packages: [], Planned: true),

        // Composes everything. It needs no SQL package of its own: parsing belongs to the layers.
        new(Cli, Knows: [Diagnostics, Model, Sources, Semantics, Inference, Ir, Config, EmitZod, EmitJsonSchema], Packages: ["System.CommandLine"]),
    ];

    [Fact]
    public void Every_project_under_src_is_a_layer_in_the_table()
    {
        var notLayers = Projects().Keys.Where(name => Layers.All(l => l.Name != name)).ToList();

        Assert.True(notLayers.Count == 0, $"Not a layer in the table: {string.Join(", ", notLayers)}. Split it into layers, or add the layer to the table and to docs/architecture.md.");
    }

    [Fact]
    public void Every_layer_with_code_today_has_its_project()
    {
        var projects = Projects();
        var missing = Layers.Where(l => !l.Planned && !projects.ContainsKey(l.Name)).Select(l => l.Name).ToList();

        Assert.True(missing.Count == 0, $"No project under src/ for: {string.Join(", ", missing)}.");
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
        foreach (var file in Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
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

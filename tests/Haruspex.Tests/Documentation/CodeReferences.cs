using System.Reflection;
using System.Text.RegularExpressions;
using Haruspex.Cli;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Emit.JsonSchema;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics.Collations;
using Haruspex.SqlServer.Sources;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// Decides whether a piece of inline code in the documentation is a reference to haruspex's code, and whether that
/// reference resolves. The convention it holds is written in <c>docs/internals/README.md</c>, "How the guide is kept
/// true".
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of text are references. A <b>path</b> starts with one of the repository's top-level folders (a project's
/// folder under <c>src/</c>, or <c>tests/</c>, <c>docs/</c>, <c>schemas/</c>) and must name a file or folder that exists. A
/// <b>C# file name</b> (<c>FlowState.cs</c>) must name a file under <c>src/</c> or <c>tests/</c>. A <b>name</b> in PascalCase,
/// optionally followed by members (<c>ResultTrack</c>, <c>ResultTrack.Then</c>, <c>FixedPoint.MostPasses</c>), or a
/// test's name (<c>Calls_nest_at_most_32_deep</c>), must name something declared.
/// </para>
/// <para>
/// A name resolves when its first part is a type of haruspex (read by reflection from the shipped assemblies and these
/// tests; for the differential tests, which this project does not reference, from their source), and each later part is
/// a member or nested type of the part before it. A name alone may also be a test method, a project, layer or
/// namespace of haruspex, or a name in a committed Zod snapshot or a worked example's Zod, since the documentation shows generated
/// names, but never a member written alone, which would hide a renamed type that left a property of its old name. A schema, or a
/// table, view, function or procedure as <c>Schema.Name</c>, that a fixture defines is a name too. Anything else must be in
/// <see cref="Outside"/>, which says why each is there.
/// Text in capitals (<c>FOR JSON</c>, <c>WITHOUT_ARRAY_WRAPPER</c>) is SQL, not a name, and is not checked.
/// </para>
/// </remarks>
internal sealed partial class CodeReferences
{
    /// <summary>
    /// Names written in code in the documentation that are not haruspex's, so that nothing here can check them. A name
    /// that is renamed in haruspex stops resolving and fails the test; it must not be added here to make it pass.
    /// </summary>
    public static readonly IReadOnlySet<string> Outside = new HashSet<string>(StringComparer.Ordinal)
    {
        // .NET's own types, named where the architecture test says what a layer may not use, or why JSON is read by hand.
        "File", "Directory", "FileInfo", "DirectoryInfo", "FileStream", "FileSystemInfo", "JsonDocument", "JsonSerializer",

        // SqlClient and Testcontainers, which only the differential tests use, and a connection string's keyword.
        "DbProviderFactories", "SqlClientFactory", "SqlConnection", "SshDockerEndpointNotSupportedException", "DisposeAsync",
        "TrustServerCertificate",

        // MSBuild's items and properties.
        "AnalysisLevel", "IsPackable", "SQLDBExtensionsRefPath", "NetCoreBuild",

        // JavaScript, in the emitter's documentation, and Node's buffer of bytes, which the driver hands a binary value over as.
        "Date", "Infinity", "Buffer", "Uint8Array",

        // The mssql driver's types, which the functions take and pass through.
        "ConnectionPool", "Transaction", "RequestError", "IProcedureResult",

        // Names from before the split into layers, in the table of what each type became (docs/architecture.md).
        "Haruspex.SqlServer", "Catalog", "TableModel", "ColumnModel", "SqlProject.Changed",

        // Elements and annotations of a DACPAC's model.xml that the reader doesn't read, named where the documentation says
        // what it passes over or how an extract differs (the ones it reads are its own words, readerWords).
        "SqlTableType", "SqlSequence",

        // Products named in the layers' diagrams.
        "ScriptDom", "TypeScript",

        // A layer planned but not yet built, in the layer table.
        "Haruspex.Emit.JsonSchema",

        // Names the configuration reference and the decision log show haruspex generating, or not, for objects no fixture
        // or worked example has, and a prefix.
        "AuditGetOrder", "StockLevelHistory", "GetOrderResult2Row", "PartTreeNode", "GetOrderRow2", "Admin", "DboGetOrder",
    };

    /// <summary>
    /// The first part of a dotted name that is .NET's or a package's namespace (<c>System.CommandLine</c>,
    /// <c>Microsoft.Data.SqlClient</c>, <c>Testcontainers.MsSql</c>, <c>MSBuild.Sdk.SqlProj</c>, an SDK named where it isn't read),
    /// named where a dependency is listed. Not checked.
    /// </summary>
    public static readonly IReadOnlySet<string> OutsideNamespaces = new HashSet<string>(StringComparer.Ordinal) { "System", "Microsoft", "Testcontainers", "MSBuild" };

    private static readonly Assembly[] Assemblies =
    [
        typeof(EquatableArray).Assembly,
        typeof(SourceLocation).Assembly,
        typeof(DatabaseModel).Assembly,
        typeof(SqlServer.Syntax.SyntaxReader).Assembly,
        typeof(SqlScriptFolder).Assembly,
        typeof(SqlServer.Semantics.Binder).Assembly,
        typeof(IrInference).Assembly,
        typeof(IrDocument).Assembly,
        typeof(HaruspexConfig).Assembly,
        typeof(ZodEmitter).Assembly,
        typeof(JsonSchemaEmitter).Assembly,
        typeof(Haruspex.Emit.Mssql.MssqlEmitter).Assembly,
        typeof(SqlProject).Assembly,
        typeof(CodeReferences).Assembly,
        Architecture.AnalyserBuild.Assembly,
    ];

    private static readonly string[] CodeFolders = ["src", "tests"];

    private const BindingFlags Every = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    private readonly Dictionary<string, List<Type>> types = new(StringComparer.Ordinal);
    private readonly HashSet<string> namespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> sourceTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> methods = new(StringComparer.Ordinal);
    private readonly HashSet<string> generated = new(StringComparer.Ordinal);

    /// <summary>
    /// The SQL names the fixtures define: each table, view, function and procedure as <c>Schema.Name</c>, and each schema. Most
    /// fixtures name their objects in lower case, which this check passes over, but the showcase's fixtures keep Microsoft's
    /// names (<c>Sales.Orders</c>), which look like a type and a member, so their READMEs' names are checked against them.
    /// </summary>
    private readonly HashSet<string> sqlNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> csharpFiles = new(StringComparer.Ordinal);

    /// <summary>
    /// The words of <c>model.xml</c> the DACPAC reader reads (<c>SqlTable</c>, <c>BodyScript</c>), and of a project file the project
    /// reader reads (<c>Build</c>, <c>DefaultCollation</c>): the values of <c>ModelXml</c>'s and <c>ProjectXml</c>'s constants, so a
    /// word the documentation names is one a reader really reads, and renaming one there fails here.
    /// </summary>
    private readonly HashSet<string> readerWords = new(StringComparer.Ordinal);

    /// <summary>The references of this commit: read once, since reflection and the source scan are the slow part.</summary>
    public static CodeReferences Current { get; } = new();

    private CodeReferences()
    {
        foreach (var type in Assemblies.SelectMany(a => a.GetTypes()).Where(t => !t.Name.Contains('<', StringComparison.Ordinal)))
        {
            Add(types, SimpleName(type), type);
            if (type.Namespace is { } ns)
                namespaces.Add(ns);
        }

        foreach (var words in (string[])["Haruspex.SqlServer.Sources.Dacpacs.ModelXml", "Haruspex.SqlServer.Sources.Projects.ProjectXml"])
        {
            foreach (var field in typeof(SqlScriptFolder).Assembly.GetType(words, throwOnError: true)!.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
                readerWords.Add((string)field.GetRawConstantValue()!);
        }

        // ScriptDom's types are named where haruspex says which syntax it reads.
        foreach (var type in typeof(TSqlFragment).Assembly.GetExportedTypes())
            Add(types, SimpleName(type), type);

        foreach (var file in SourceFiles())
        {
            var relative = MarkdownText.Relative(file);
            csharpFiles.Add(Path.GetFileName(file));
            if (!relative.StartsWith("tests/", StringComparison.Ordinal))
                continue;
            var text = File.ReadAllText(file);
            foreach (Match m in TypeDeclaration().Matches(text))
                Add(sourceTypes, m.Groups["name"].Value, relative);
            foreach (Match m in MethodDeclaration().Matches(text))
                methods.Add(m.Groups["name"].Value);
        }

        // A project, and a layer by its short name as the prose calls it (Ir, Sources, Semantics).
        foreach (var project in Directory.EnumerateFiles(RepositoryPaths.Root, "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(project);
            namespaces.Add(name);
            namespaces.Add(name.Split('.')[^1]);
        }

        // Generated names are real when haruspex generates them: in a committed snapshot (a test's own, or a fixture's
        // expected output), or in a module the documentation shows, which WorkedExampleTests (a worked example's output)
        // or QuickStartTests (the five-minute start's) holds to what haruspex gives. A name is a schema or type the module
        // exports; in a module the documentation shows, it may also be a key of an object the module declares (a column's
        // or a JSON key's name as the SQL writes it), which the page beside it explains. Keys are taken only from those
        // modules, so a stale name can't resolve by matching a column of some fixture no page shows.
        var snapshots = Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.Tests", "Snapshots"), "*.verified.ts")
            .Concat(Fixture.All.SelectMany(f => Directory.EnumerateFiles(f.Expected, "*.verified.ts")))
            .Select(File.ReadAllText);
        var shown = MarkdownText.Documentation
            .SelectMany(f => MarkdownText.FencedBlocks(File.ReadAllText(Path.Combine(RepositoryPaths.Root, f))))
            .Where(b => b.Words.Contains(WorkedExample.ZodMark) || QuickStartTests.IsModule(b))
            .Select(b => b.Text)
            .ToList();
        foreach (var module in snapshots.Concat(shown))
        {
            foreach (Match m in GeneratedName().Matches(module))
                generated.Add(m.Groups["name"].Value);
        }

        foreach (var module in shown)
        {
            foreach (Match m in GeneratedKey().Matches(module))
                generated.Add(m.Groups["name"].Value);
        }

        // An example application's TypeScript, its own and the module generated for it, which its test compiles
        // (ExampleAppTests): a name declared there, exported or not, is real, as a page quoting the file shows it
        // (QuotedExampleTests).
        foreach (var source in Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "examples"), "*.ts", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match m in ExampleDeclaration().Matches(File.ReadAllText(source)))
                generated.Add(m.Groups["name"].Value);
        }

        foreach (var fixture in Fixture.All)
        {
            var model = SqlScriptFolder.Read(fixture.Database).Model;
            foreach (var name in model.Tables.Select(t => t.Name).Concat(model.Views.Select(v => v.Name)).Concat(model.Functions.Select(f => f.Name)).Concat(model.Procedures.Select(p => p.Name)))
            {
                sqlNames.Add(name.Schema);
                sqlNames.Add(name.Schema + "." + name.Name);
            }
        }
    }

    /// <summary>Whether <paramref name="code"/> is a reference this convention checks.</summary>
    public static bool IsReference(string code) => Kind(code) is not null;

    /// <summary>Why <paramref name="code"/>, a reference, leads nowhere; null when it resolves or is not a reference.</summary>
    public string? Problem(string code) => Kind(code) switch
    {
        ReferenceKind.Path => PathProblem(code),
        ReferenceKind.CSharpFile => csharpFiles.Contains(code) ? null : "names no C# file under src/ or tests/",
        ReferenceKind.Name => NameProblem(code),
        _ => null,
    };

    /// <summary>
    /// The names a Mermaid diagram's labels give, with the line each is on: every word in PascalCase with at least two
    /// capitals anywhere and a lower-case letter (<c>IrBuilder</c>, <c>ILocalRelations</c>, <c>ResultTrack.Then</c>), which in a diagram of haruspex's types is a type's name. An acronym's plural (<c>CTEs</c>) is not. Plain
    /// words (<c>Sources</c>, <c>Model</c>) are the layers' names in prose, and are not checked.
    /// </summary>
    public static IEnumerable<(int Line, string Name)> DiagramNames(string markdown)
    {
        foreach (var block in MarkdownText.FencedBlocks(markdown).Where(b => b.Words is ["mermaid", ..]))
        {
            var lines = block.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in DiagramName().Matches(lines[i]))
                    yield return (block.Line + 1 + i, m.Value);
            }
        }
    }

    /// <summary>Why a path written in C# source leads nowhere; null when it resolves.</summary>
    public static string? PathProblem(string path)
    {
        var full = Path.GetFullPath(Path.Combine(RepositoryPaths.Root, path.TrimEnd('/')));
        return File.Exists(full) || Directory.Exists(full) ? null : "names no file or folder of the repository";
    }

    /// <summary>The C# files of the repository: the shipped code and the tests, build output and packages left out.</summary>
    public static IEnumerable<string> SourceFiles() =>
        CodeFolders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !MarkdownText.Relative(path).Split('/').Any(part => part is "bin" or "obj" or "node_modules"))
            .Order(StringComparer.Ordinal);

    private static ReferenceKind? Kind(string code)
    {
        if (RepositoryPath().IsMatch(code))
            return ReferenceKind.Path;
        if (CSharpFile().IsMatch(code))
            return ReferenceKind.CSharpFile;
        if (Name().IsMatch(code) && code.Any(char.IsLower))
            return ReferenceKind.Name;
        return null;
    }

    private string? NameProblem(string code)
    {
        var parts = code.Split('.');
        if (Outside.Contains(code) || namespaces.Contains(code) || readerWords.Contains(code) || sqlNames.Contains(code) || (parts.Length > 1 && OutsideNamespaces.Contains(parts[0])))
            return null;
        if (types.TryGetValue(parts[0], out var candidates))
            return MemberProblem(candidates, parts);
        if (sourceTypes.TryGetValue(parts[0], out var files))
            return SourceMemberProblem(files, parts);
        if (parts.Length > 1)
            return $"{parts[0]} is not a type of haruspex";
        // A collation haruspex has proven a fold for is a name of SQL Server's that the code holds (CollationFold.Proven).
        if (code.Contains('_', StringComparison.Ordinal))
            return methods.Contains(code) || CollationFold.Proven.Any(p => p.Collation == code) ? null : "names no test";

        // A name alone is a type, never a member: a type is often renamed while a property keeps its old name
        // (Procedure.Redefinition), so a member written alone would hide the rename. Write a member as Type.Member.
        return generated.Contains(code)
            ? null
            : "names no type of haruspex and no generated name (write a member as Type.Member)";
    }

    /// <summary>Each part after the first is a member or nested type of what the part before it names.</summary>
    private static string? MemberProblem(IEnumerable<Type> candidates, string[] parts)
    {
        var current = candidates.ToList();
        for (var i = 1; i < parts.Length; i++)
        {
            var found = current.SelectMany(t => t.GetMember(parts[i], MemberTypes.All, Every)).ToList();
            if (found.Count == 0)
                return $"{string.Join('.', parts[..i])} has no member {parts[i]}";
            current = [.. found.Select(TypeOf).OfType<Type>()];
        }

        return null;
    }

    /// <summary>A type known only from the differential tests' source: each member must be declared in its files.</summary>
    private static string? SourceMemberProblem(IEnumerable<string> files, string[] parts)
    {
        var text = string.Concat(files.Select(f => File.ReadAllText(Path.Combine(RepositoryPaths.Root, f))));
        foreach (var member in parts.Skip(1))
        {
            if (!Regex.IsMatch(text, $@"\b{Regex.Escape(member)}\b\s*(?:\(|\{{|=>|=|;|<)"))
                return $"{parts[0]} has no member {member}";
        }

        return null;
    }

    private static Type? TypeOf(MemberInfo member) => member switch
    {
        Type nested => nested,
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        MethodInfo method => method.ReturnType,
        _ => null,
    };

    /// <summary>A type's name as code writes it: no generic arity, no enclosing type.</summary>
    private static string SimpleName(Type type) => type.Name.Split('`')[0];

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        list.Add(value);
    }

    private enum ReferenceKind
    {
        Path,
        CSharpFile,
        Name,
    }

    [GeneratedRegex(@"^(?:src/Haruspex\.|tests/|docs/|schemas/)[^\s*?<>{}|…]*$")]
    private static partial Regex RepositoryPath();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9.]*\.cs$")]
    private static partial Regex CSharpFile();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)*$")]
    private static partial Regex Name();

    [GeneratedRegex(@"\b(?:class|record|struct|interface|enum)\s+(?:class\s+|struct\s+)?(?<name>[A-Z]\w*)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"\b(?:void|Task|ValueTask)\s+(?<name>[A-Z]\w*)\s*\(")]
    private static partial Regex MethodDeclaration();

    [GeneratedRegex(@"(?<![\w.])(?![A-Z]+s\b)(?=[A-Z][a-z0-9]*[A-Z])(?=[A-Za-z0-9]*[a-z])[A-Z][A-Za-z0-9]*(?:\.[A-Z][A-Za-z0-9]*)*(?![\w])")]
    private static partial Regex DiagramName();

    [GeneratedRegex(@"\bexport\s+(?:const|type|interface|class|async\s+function)\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex GeneratedName();

    /// <summary>A declaration in an example application's TypeScript, exported or not.</summary>
    [GeneratedRegex(@"^(?:export\s+)?(?:const|type|interface|class|(?:async\s+)?function)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Multiline)]
    private static partial Regex ExampleDeclaration();

    /// <summary>A key of a generated object, one to a line: <c>  orderDate: z.iso.date(),</c>.</summary>
    [GeneratedRegex(@"^[ ]+(?<name>[A-Za-z_]\w*): ", RegexOptions.Multiline)]
    private static partial Regex GeneratedKey();
}

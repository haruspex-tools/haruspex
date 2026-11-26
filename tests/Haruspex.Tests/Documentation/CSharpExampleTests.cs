using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// Every C# example in the documentation compiles against the layers as they are built. A project's README is what a reader
/// of NuGet sees first, and its example is what they copy: one that no longer compiles is the first thing they meet. A block
/// that names a file after its language quotes that file instead, and <see cref="QuotedExampleTests"/> holds it.
/// </summary>
/// <remarks>
/// Each example is compiled as the body of a method, with every public namespace of the layers under <c>src/</c> in scope (the
/// command line's left out: it is no library), and the values it takes from the text around it as the method's parameters,
/// which <see cref="Given"/> lists by page. Only errors fail it: an example may leave a value unused.
/// </remarks>
public class CSharpExampleTests
{
    /// <summary>
    /// The values an example takes from the text around it, by page: what the page has already introduced, declared as the
    /// method's parameters. An example that needs nothing has no entry.
    /// </summary>
    private static readonly Dictionary<string, string> Given = new(StringComparer.Ordinal)
    {
        ["docs/internals/05-values-as-a-lattice.md"] = "IrType type",
        ["src/Haruspex.Config/README.md"] = "IrDocument document, IReadOnlyList<ObjectName> procedures",
        ["src/Haruspex.Ir/README.md"] = "DatabaseModel model",
        ["src/Haruspex.SqlServer.Syntax/README.md"] =
            "Microsoft.SqlServer.TransactSql.ScriptDom.CreateTableStatement create, Microsoft.SqlServer.TransactSql.ScriptDom.ProcedureStatementBody definition",
    };

    private static readonly List<(string Page, MarkdownText.FencedBlock Block)> Examples =
    [
        .. MarkdownText.Documentation.SelectMany(page => MarkdownText.FencedBlocks(File.ReadAllText(Path.Combine(RepositoryPaths.Root, page)))
            .Where(b => b.Words is ["csharp"])
            .Select(b => (page, b))),
    ];

    public static TheoryData<string, int> Blocks { get; } = [.. Examples.Select(e => (e.Page, e.Block.Line))];

    [Fact]
    public void Every_project_readme_s_example_is_compiled()
    {
        var readmes = Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "README.md", SearchOption.AllDirectories)
            .Select(MarkdownText.Relative)
            .Where(r => r != "src/Haruspex.Cli/README.md");
        Assert.All(readmes, readme => Assert.Contains(Examples, e => e.Page == readme));
    }

    [Fact]
    public void Every_page_given_values_has_an_example_to_take_them() =>
        Assert.All(Given.Keys, page => Assert.Contains(Examples, e => e.Page == page));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_csharp_example_compiles(string page, int line)
    {
        var block = Examples.Single(e => e.Page == page && e.Block.Line == line).Block;
        var errors = Errors(block.Text, Given.GetValueOrDefault(page, ""));
        Assert.True(
            errors.Count == 0,
            $"{page}:{line.ToString(CultureInfo.InvariantCulture)}: the example doesn't compile:\n{string.Join('\n', errors)}");
    }

    [Fact]
    public void An_example_that_calls_what_does_not_exist_fails() =>
        Assert.Contains(Errors("""IrJsonWriter.Write(document, indented: true);""", "IrDocument document"), e => e.Contains("CS1739", StringComparison.Ordinal));

    [Fact]
    public void An_example_that_uses_a_layer_as_it_is_compiles() =>
        Assert.Empty(Errors("""var json = IrJsonWriter.Write(document);""", "IrDocument document"));

    /// <summary>The namespaces an example may use unqualified: the framework's common ones, and every public one of the layers.</summary>
    private static readonly string[] Namespaces =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq",
        .. Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != "Haruspex.Cli")
            .SelectMany(name => Assembly.Load(new AssemblyName(name!)).GetExportedTypes())
            .Select(t => t.Namespace!)
            .Distinct()
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The framework and every assembly the tests run with, the layers and their packages among them.</summary>
    private static readonly MetadataReference[] References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)),
    ];

    /// <summary>The errors that compiling <paramref name="example"/> as a method's body, with <paramref name="given"/> its parameters, gives.</summary>
    private static List<string> Errors(string example, string given)
    {
        var code = $$"""
            {{string.Join('\n', Namespaces.Select(n => $"using {n};"))}}

            internal static class Example
            {
                internal static void Run({{given}})
                {
            {{example}}
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "Haruspex.Example",
            [CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        return
        [
            .. compilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()),
        ];
    }
}

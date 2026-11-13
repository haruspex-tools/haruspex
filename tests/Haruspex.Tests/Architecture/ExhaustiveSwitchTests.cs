using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// The project's own analysers (<c>analysers/README.md</c>, decision 0032): a switch over a closed hierarchy that forgets a case
/// fails the build, one that handles every case needs no discard arm and may have none, and nothing else is touched. Each
/// test compiles a small program with the analysers, as the build does, and reads the errors it would stop on.
/// </summary>
public class ExhaustiveSwitchTests
{
    /// <summary>A closed hierarchy of three cases, one under an abstract case of its own, as haruspex writes them.</summary>
    private const string Hierarchy = """
        public abstract record Shape { private protected Shape() { } }
        public sealed record Circle : Shape;
        public abstract record Polygon : Shape { private protected Polygon() { } }
        public sealed record Square(int Side) : Polygon;
        public sealed record Triangle : Polygon;
        """;

    /// <summary>The errors a build of <paramref name="code"/> stops on, by id, in order: warnings are errors, as in haruspex.</summary>
    private static async Task<List<string>> Errors(string code, string assembly = "Haruspex.Planted")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            assembly,
            [CSharpSyntaxTree.ParseText(Hierarchy + code, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        var withAnalysers = compilation.WithAnalyzers(Analysers(), new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: false, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false));
        var diagnostics = await withAnalysers.GetAllDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && !d.IsSuppressed).Select(d => d.Id).Order(StringComparer.Ordinal)];
    }

    /// <summary>The analysers the build runs (<see cref="AnalyserBuild"/>).</summary>
    private static ImmutableArray<DiagnosticAnalyzer> Analysers() =>
        new AnalyzerFileReference(AnalyserBuild.Path, new Loader()).GetAnalyzers(LanguageNames.CSharp);

    /// <summary>Loads the analysers' assembly as the test process already has it.</summary>
    private sealed class Loader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public System.Reflection.Assembly LoadFromPath(string fullPath) => AnalyserBuild.Assembly;
    }

    [Fact]
    public void The_analysers_are_found() => Assert.Equal(2, Analysers().Length);

    [Fact]
    public async Task A_switch_expression_that_handles_every_case_needs_no_discard_arm() =>
        Assert.Empty(await Errors("""
            public static class Use
            {
                public static int Of(Shape s) => s switch { Circle => 1, Square { Side: var n } => n, Triangle => 3 };
                public static int Grouped(Shape s) => s switch { Circle => 1, Polygon => 2 };
                public static int Either(Shape s) => s switch { Circle or Triangle => 1, Square q when q.Side > 2 => 2, Square => 3 };
                public static int Guarded(Shape s) => s switch { var x when x.GetHashCode() == 0 => 0, Circle => 1, Polygon => 2 };
            }
            """));

    [Fact]
    public async Task A_switch_expression_that_forgets_a_case_fails_the_build() =>
        Assert.Equal(["CS8509"], await Errors("""
            public static class Use
            {
                public static int Of(Shape s) => s switch { Circle => 1, Square => 2 };
            }
            """));

    [Fact]
    public async Task A_case_matched_only_under_a_condition_is_not_handled() =>
        Assert.Equal(["CS8509", "CS8509", "CS8846"], await Errors("""
            public static class Use
            {
                public static int Of(Shape s) => s switch { Circle => 1, Triangle => 2, Square { Side: 1 } => 3 };
                public static int Guarded(Shape s) => s switch { Circle => 1, Triangle => 2, Square q when q.Side > 1 => 3 };
                public static int GuardedFirst(Shape s) => s switch { var x when x.GetHashCode() == 0 => 0, Circle => 1, Triangle => 2 };
            }
            """));

    [Fact]
    public async Task A_switch_statement_without_default_that_forgets_a_case_fails_the_build() =>
        Assert.Equal(["HX0001"], await Errors("""
            public static class Use
            {
                public static int Of(Shape s)
                {
                    switch (s)
                    {
                        case Circle:
                            return 1;
                        case Square:
                            return 2;
                    }

                    return 0;
                }
            }
            """));

    [Fact]
    public async Task An_arm_for_no_case_beside_every_case_fails_the_build() =>
        Assert.Equal(["HX0002", "HX0002"], await Errors("""
            public static class Use
            {
                public static int Of(Shape s) => s switch { Circle => 1, Polygon => 2, _ => throw new System.InvalidOperationException() };

                public static int Statement(Shape s)
                {
                    switch (s)
                    {
                        case Circle:
                            return 1;
                        case Polygon:
                            return 2;
                        default:
                            throw new System.InvalidOperationException();
                    }
                }
            }
            """));

    [Fact]
    public async Task A_discard_over_a_value_that_may_be_null_is_its_null_arm() =>
        Assert.Empty(await Errors("""
            public static class Use
            {
                public static int Of(Shape? s) => s switch { Circle => 1, Polygon => 2, _ => 0 };
            }
            """));

    [Fact]
    public async Task A_hierarchy_that_is_not_haruspex_s_is_never_taken_to_be_closed() =>
        Assert.Equal(["CS8509"], await Errors(
            """
            public static class Use
            {
                public static int Of(Shape s) => s switch { Circle => 1, Polygon => 2 };
            }
            """,
            assembly: "Elsewhere"));

    [Fact]
    public async Task A_case_less_visible_than_its_hierarchy_fails_the_build() =>
        Assert.Equal(["HX0003", "HX0003"], await Errors("""
            internal sealed record Hidden : Shape;
            public static class Outer
            {
                private sealed record Nested : Polygon;
            }
            internal abstract record Inner { private protected Inner() { } }
            internal sealed record InnerCase : Inner;
            """));
}

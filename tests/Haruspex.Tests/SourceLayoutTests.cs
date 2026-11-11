using System.Text.RegularExpressions;

namespace Haruspex.Tests;

/// <summary>
/// Each C# file holds one type, and is named after it, so a reader who knows a type's name knows where to find it, and a
/// file's name says everything it holds. The rule is the layout CONTRIBUTING.md describes; this test keeps it.
/// </summary>
/// <remarks>
/// <para>
/// A type at the top level is found by its declaration starting a line. Every file declares a file-scoped namespace and
/// <c>dotnet format</c> enforces indentation, so a declaration at column 0 is a top-level type and an indented one is
/// nested. Reading the text this way needs no compiler package in the tests.
/// </para>
/// <para>
/// Allowed: a partial type split over files named <c>Type.cs</c> and <c>Type.Part.cs</c>; nested types, which belong to the
/// type they are in; and a type with the <c>file</c> modifier, which no other file can see. A file with no type at all
/// (global usings) is allowed too, and so are the files in <see cref="Exceptions"/>.
/// </para>
/// </remarks>
public partial class SourceLayoutTests
{
    /// <summary>Build output, other projects' files, and the prose checks' fixtures, which are wrong on purpose.</summary>
    private static readonly string[] Skipped = ["bin", "obj", "node_modules", "fixtures"];

    private static readonly List<string> Files =
    [
        .. new[] { "src", "tests" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !Path.GetRelativePath(TestPaths.RepoRoot, path).Split(Path.DirectorySeparatorChar).Any(Skipped.Contains))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// The files allowed to break the rule. Both are parts of <c>ProcedureAnalyzer</c> named for what they do, rather than
    /// <c>ProcedureAnalyzer.Part.cs</c>. The class is to be taken apart into types of its own, which will name these files
    /// properly, so they are not renamed in the meantime.
    /// </summary>
    private static readonly HashSet<string> Exceptions = new(StringComparer.Ordinal)
    {
        "src/Haruspex.SqlServer.Inference/Cardinality.cs",
        "src/Haruspex.SqlServer.Inference/Refinement.cs",
    };

    [Fact]
    public void The_scan_finds_the_source() => Assert.Contains(Files, f => f.EndsWith("SourceLayoutTests.cs", StringComparison.Ordinal));

    [Fact]
    public void The_scan_tells_top_level_types_from_nested_ones()
    {
        const string source = """
            namespace Example;

            /// <summary>A procedure.</summary>
            public sealed record Procedure(string Name)
            {
                private sealed class Cache;
            }

            internal enum Kind { One }
            file static class Helper;
            """;

        Assert.Equal(["Procedure", "Kind"], TopLevelTypes(source));
    }

    [Fact]
    public void Each_file_holds_one_type_named_after_it()
    {
        var problems = new List<string>();
        foreach (var path in Files.Where(p => !Exceptions.Contains(Relative(p))))
        {
            // Type.cs, or Type.Part.cs for a part of a partial type.
            var expected = Path.GetFileName(path).Split('.')[0];
            var others = TopLevelTypes(File.ReadAllText(path)).Where(name => name != expected).Distinct().ToList();
            if (others.Count > 0)
                problems.Add($"{Relative(path)}: {string.Join(", ", others)}");
        }

        Assert.True(
            problems.Count == 0,
            "These files hold a type not named after the file; move each to its own file, named after it:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Each_exception_is_a_file_that_still_breaks_the_rule()
    {
        // An exception that no longer applies would otherwise let the file break the rule again unnoticed.
        foreach (var file in Exceptions)
        {
            var path = Path.Combine(TestPaths.RepoRoot, file);
            Assert.True(File.Exists(path), $"{file} is listed as an exception but does not exist; remove it from the list.");
            Assert.Contains(TopLevelTypes(File.ReadAllText(path)), name => name != Path.GetFileName(file).Split('.')[0]);
        }
    }

    private static string Relative(string path) => Path.GetRelativePath(TestPaths.RepoRoot, path).Replace('\\', '/');

    /// <summary>The names of the types declared at column 0, other than <c>file</c> types.</summary>
    private static List<string> TopLevelTypes(string source) =>
        [.. TopLevelType().Matches(source).Where(m => !m.Groups["file"].Success).Select(m => m.Groups["name"].Value)];

    [GeneratedRegex(
        @"^(?:(?<file>file)\s+|(?:public|internal|sealed|static|abstract|readonly|partial|ref|unsafe)\s+)*(?:class|struct|interface|enum|record(?:\s+class|\s+struct)?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline)]
    private static partial Regex TopLevelType();
}

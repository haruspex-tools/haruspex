using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// The null-forgiving operator (<c>x!</c>) tells the compiler a value is not null without proving it, so a wrong one is a
/// <see cref="NullReferenceException"/> waiting where the nullable analysis would have said where. In haruspex each is
/// replaced by what proves it (a pattern, a type that can't be null, <c>[MemberNotNullWhen]</c>), or kept with a comment
/// that says why the value can't be null there, starting <c>Never null:</c>, on its line or the line above. Their number is
/// pinned, so a new one, even with its reason, is a change to this test that a reviewer sees.
/// </summary>
public class NullForgivingTests
{
    /// <summary>How many are kept in <c>src/</c>, each with its reason.</summary>
    private const int Kept = 28;

    /// <summary>The comment that says why one is kept.</summary>
    private const string Reason = "// Never null:";

    [Fact]
    public void Every_null_forgiving_operator_left_in_src_says_why_and_their_number_is_pinned()
    {
        var found = Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(f => Uses(Path.GetRelativePath(RepositoryPaths.Root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();

        Assert.True(found.All(u => u.Explained), "A null-forgiving operator without its reason (\"" + Reason + " …\" on its line or the line above):\n" + string.Join("\n", found.Where(u => !u.Explained).Select(u => u.At)));
        Assert.True(found.Count == Kept, $"{found.Count} null-forgiving operators in src, where {Kept} are expected:\n" + string.Join("\n", found.Select(u => u.At)));
    }

    [Fact]
    public void The_check_finds_one_without_its_reason_and_accepts_one_with()
    {
        const string Planted = """
            class C
            {
                string? s;
                int A() => s!.Length;
                // Never null: set by the constructor.
                int B() => s!.Length;
                int C() => s!.Length; // Never null: set by the constructor.
                bool D() => s != null && !(s.Length > 0);
            }
            """;

        var uses = Uses("planted.cs", Planted).ToList();

        Assert.Equal(["planted.cs:4", "planted.cs:6", "planted.cs:7"], uses.Select(u => u.At));
        Assert.Equal([false, true, true], uses.Select(u => u.Explained));
    }

    /// <summary>Each null-forgiving operator in a file, where it is, and whether its reason is written beside it.</summary>
    private static IEnumerable<(string At, bool Explained)> Uses(string file, string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        var lines = tree.GetText().Lines;
        foreach (var use in tree.GetRoot().DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().Where(e => e.IsKind(SyntaxKind.SuppressNullableWarningExpression)))
        {
            var line = use.GetLocation().GetLineSpan().StartLinePosition.Line;
            var explained = lines[line].ToString().Contains(Reason, StringComparison.Ordinal)
                || (line > 0 && lines[line - 1].ToString().TrimStart().StartsWith(Reason, StringComparison.Ordinal));
            yield return ($"{file}:{line + 1}", explained);
        }
    }
}

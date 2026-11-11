namespace Haruspex.Tests.Documentation;

/// <summary>
/// CONTRIBUTING's "Where a construct's tests are" names every construct family's test classes, so a reader goes from a
/// construct to its unit tests, its observation tests, its fixture and its reference section. This holds the section
/// complete: every unit test class in <c>tests/Haruspex.Tests/Inference</c> and every observation or grid class of the
/// differential tests is named there, in a family's row or among the classes named otherwise.
/// </summary>
/// <remarks>
/// <c>CodeReferenceTests</c> checks the other direction, that every name in the section exists. Classes are found from
/// their files (one type per file; a partial part, <c>Type.Part.cs</c>, is not a class of its own), so a new class can't be
/// left out of the table the way a fixture could once be left out of a list (decision 0021).
/// </remarks>
public class ConstructFamilyTests
{
    private const string Heading = "## Where a construct's tests are";

    [Fact]
    public void Every_construct_test_class_is_named_in_contributing()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "CONTRIBUTING.md"));
        var start = text.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"CONTRIBUTING.md has no section \"{Heading[3..]}\".");
        var end = text.IndexOf("\n## ", start + Heading.Length, StringComparison.Ordinal);
        var section = text[start..(end < 0 ? text.Length : end)];

        var tests = Path.Combine(RepositoryPaths.Root, "tests");
        var classes = Classes(Path.Combine(tests, "Haruspex.Tests", "Inference"), "*Tests.cs")
            .Concat(Classes(Path.Combine(tests, "Haruspex.DifferentialTests"), "*ObservationTests.cs"))
            .Concat(Classes(Path.Combine(tests, "Haruspex.DifferentialTests"), "*GridTests.cs"));

        Assert.Empty(classes.Where(c => !section.Contains($"`{c}`", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> Classes(string folder, string pattern) =>
        Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!)
            .Where(name => !name.Contains('.', StringComparison.Ordinal));
}

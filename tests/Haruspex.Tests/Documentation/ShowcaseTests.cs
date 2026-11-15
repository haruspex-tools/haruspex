using System.Globalization;
using System.Text.RegularExpressions;
using SampleCorpus = Haruspex.Tests.Corpus.Corpus;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// The project README's showcase: an example over each of Microsoft's vendored samples, written for the README over the
/// sample's own tables. <see cref="WorkedExampleTests"/> holds each to what haruspex infers over the whole sample. These tests
/// hold each to a real server too: its SQL is a fixture the differential tests deploy, seed and call, and that fixture's
/// snapshot is the README's Zod, so the fixture's copy of the sample's tables types the example as the sample does.
/// </summary>
public partial class ShowcaseTests
{
    private const string Readme = "README.md";

    /// <summary>The folders of a fixture's database that hold the objects an example defines.</summary>
    private static readonly string[] CodeFolders = ["functions", "procedures"];

    private static readonly List<WorkedExample> Showcase =
    [
        .. WorkedExample.Read(Readme, File.ReadAllText(Path.Combine(RepositoryPaths.Root, Readme))).Examples.Where(e => e.Over is not null),
    ];

    public static TheoryData<int> Examples { get; } = [.. Showcase.Select(e => e.Line)];

    [Fact]
    public void The_readme_shows_an_example_over_each_vendored_sample() =>
        Assert.Equal(SampleCorpus.All.Select(c => c.Name).Order(StringComparer.Ordinal), Showcase.Select(e => e.Over).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Examples))]
    public void Each_showcase_example_is_a_deployed_fixture_holding_exactly_its_sql(int line)
    {
        var example = Showcase.Single(e => e.Line == line);
        var fixture = FixtureOf(example);

        Assert.True(fixture.IsDeployed, $"The fixture {fixture.Name} isn't deployed, so the example at {Readme}:{At(example)} is never run.");
        Assert.Equal(Batches(example.Sql).Order(StringComparer.Ordinal), Code(fixture).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Each_showcase_example_s_fixture_generates_the_zod_the_readme_shows(int line)
    {
        var example = Showcase.Single(e => e.Line == line);
        var shown = Assert.Single(example.Outputs, o => o.Kind == WorkedExample.OutputKind.Zod);
        var (_, zod) = Assert.Single(FixtureOf(example).Snapshots);

        Assert.Equal(Text(shown.Text), Text(WorkedExample.ZodShown(File.ReadAllText(zod), shown.Text)));
    }

    /// <summary>The fixture whose functions and procedures include the example's first object.</summary>
    private static Fixture FixtureOf(WorkedExample example)
    {
        var first = Batches(example.Sql)[0];
        return Fixture.All.SingleOrDefault(f => Code(f).Contains(first))
            ?? throw new InvalidOperationException($"No fixture holds the SQL of the example at {Readme}:{At(example)}.");
    }

    /// <summary>The text of each function and procedure in the fixture, one object to a file.</summary>
    private static List<string> Code(Fixture fixture) =>
    [
        .. CodeFolders
            .Select(folder => Path.Combine(fixture.Database, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.sql"))
            .Select(file => Text(File.ReadAllText(file))),
    ];

    /// <summary>The script's batches, split on <c>GO</c> lines as sqlcmd splits them.</summary>
    private static List<string> Batches(string sql) => [.. GoLine().Split(sql).Select(Text).Where(b => b.Length > 0)];

    private static string Text(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string At(WorkedExample example) => example.Line.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[ \t]*GO[ \t]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();
}

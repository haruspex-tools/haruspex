using System.Text.RegularExpressions;
using Haruspex.SqlServer.Inference.Typing;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The table of conversions (<see cref="Conversions.Table"/>) as data: each row rests on an observation test that exists, and
/// no pair of types is in two rows, so the table, not the order of its rows, says what a conversion keeps.
/// </summary>
public partial class ConversionsTests
{
    /// <summary>Every test method of the differential tests' observation and grid classes, by name, read from their source.</summary>
    private static readonly HashSet<string> Observations =
    [
        .. Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.DifferentialTests"), "*.cs")
            .Where(f => f.EndsWith("ObservationTests.cs", StringComparison.Ordinal) || f.EndsWith("GridTests.cs", StringComparison.Ordinal))
            .SelectMany(f => TestMethod().Matches(File.ReadAllText(f)).Select(m => m.Groups["name"].Value)),
    ];

    [Fact]
    public void Every_row_cites_an_observation_test_that_exists()
    {
        var missing = Conversions.Table
            .SelectMany(r => r.ObservedIn.Where(name => !Observations.Contains(name)).Select(name => $"{string.Join("/", r.To)} from {string.Join("/", r.From)}: {name}"))
            .ToList();
        Assert.True(Conversions.Table.All(r => r.ObservedIn.Count > 0), "A row cites no observation test.");
        Assert.True(missing.Count == 0, "Cited, but no such observation test:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void No_pair_of_types_is_in_two_rows()
    {
        var pairs = Conversions.Table.SelectMany(r => r.To.SelectMany(to => r.From.Select(from => $"{from} into {to}"))).ToList();
        Assert.Empty(pairs.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [GeneratedRegex(@"\[(?:Fact|Theory)[^\]]*\]\s*(?:\[[^\]]*\]\s*)*public (?:async Task|void) (?<name>\w+)\(")]
    private static partial Regex TestMethod();
}

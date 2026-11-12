using System.Text.RegularExpressions;
using Haruspex.SqlServer.Inference.Typing;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The table of conversions (<see cref="Conversions.Table"/>) as data: each row rests on observation tests tied to it, which
/// read the table and check the rows that cite them, and no pair of types is in two rows, so the table, not the order of its
/// rows, says what a conversion keeps.
/// </summary>
public partial class ConversionsTests
{
    /// <summary>
    /// Every test method of the differential tests' observation and grid classes, by name, with its body, read from their source.
    /// </summary>
    private static readonly Dictionary<string, string> Observations = Directory
        .EnumerateFiles(Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.DifferentialTests"), "*.cs")
        .Where(f => f.EndsWith("ObservationTests.cs", StringComparison.Ordinal) || f.EndsWith("GridTests.cs", StringComparison.Ordinal))
        .SelectMany(f => Methods(File.ReadAllText(f)))
        .ToDictionary(m => m.Name, m => m.Body, StringComparer.Ordinal);

    [Fact]
    public void Every_row_cites_observation_tests_that_read_the_table()
    {
        // A test that reads the table checks the rows that cite it (or every row); one that doesn't can't be tied to a row.
        var problems = Conversions.Table
            .SelectMany(r => r.ObservedIn.Select(name => (Row: $"{string.Join("/", r.To)} from {string.Join("/", r.From)}", Name: name)))
            .Select(c => !Observations.TryGetValue(c.Name, out var body) ? $"{c.Row}: no observation test {c.Name}"
                : !body.Contains("Conversions.Table", StringComparison.Ordinal) ? $"{c.Row}: {c.Name} doesn't read the table"
                : null)
            .OfType<string>()
            .ToList();
        Assert.True(Conversions.Table.All(r => r.ObservedIn.Count > 0), "A row cites no observation test.");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_pair_of_types_is_in_two_rows()
    {
        var pairs = Conversions.Table.SelectMany(r => r.To.SelectMany(to => r.From.Select(from => $"{from} into {to}"))).ToList();
        Assert.Empty(pairs.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    /// <summary>Each test method in a source file: its name, and its body up to the brace that closes it.</summary>
    private static IEnumerable<(string Name, string Body)> Methods(string source) =>
        TestMethod().Matches(source).Select(m =>
        {
            var end = source.IndexOf("\n    }\n", m.Index, StringComparison.Ordinal);
            return (m.Groups["name"].Value, source[m.Index..(end < 0 ? source.Length : end)]);
        });

    [GeneratedRegex(@"\[(?:Fact|Theory)[^\]]*\]\s*(?:\[[^\]]*\]\s*)*public (?:async Task|void) (?<name>\w+)\(")]
    private static partial Regex TestMethod();
}

namespace Haruspex.DifferentialTests;

/// <summary>
/// Every procedure run of the differential tests, read from the deployed fixtures' <c>runs.json</c>. Each fixture procedure
/// is run or listed in its file's <c>notRun</c> with the reason; <c>Every_procedure_is_run_or_excluded_with_a_reason</c>
/// holds the two to that.
/// </summary>
public static class Runs
{
    private static readonly IReadOnlyList<(Fixture Fixture, RunFile File)> Files =
        [.. Fixture.All.Where(f => f.IsDeployed).Select(f => (f, RunFile.Read(f)))];

    /// <summary>The fixtures deployed into the test database: those with a seed. Their schemas must not overlap.</summary>
    public static IReadOnlyList<string> Fixtures { get; } = [.. Files.Select(f => f.Fixture.Name)];

    /// <summary>
    /// The fixtures whose seed must reach every edge the IR claims (<c>Coverage.MissingEdges</c>): each bound, each value
    /// of a set, each full length. Each fixture's file says why it is held to that or not.
    /// </summary>
    public static IReadOnlyList<string> EdgeFixtures { get; } = [.. Files.Where(f => f.File.SeedReachesEveryEdge).Select(f => f.Fixture.Name)];

    /// <summary>Every run, fixture by fixture, in the order of each file.</summary>
    public static IReadOnlyList<ProcedureRun> All { get; } = [.. Files.SelectMany(f => f.File.Runs)];

    /// <summary>Fixture procedures deliberately not run, each with the reason.</summary>
    public static IReadOnlyDictionary<string, string> NotRun { get; } =
        Files.SelectMany(f => f.File.NotRun).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    /// <summary>The run with this name, such as <c>trees: list_parts</c>.</summary>
    public static ProcedureRun Named(string name) => All.Single(r => r.Name == name);
}

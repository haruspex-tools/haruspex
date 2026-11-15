using System.Text.RegularExpressions;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Haruspex.Tests.Snapshotting;
using Kind = Haruspex.Tests.Coverage.SyntaxInventory.ClaimKind;

namespace Haruspex.Tests.Coverage;

/// <summary>
/// The coverage page, <c>docs/coverage.md</c>, is what haruspex does today: it lists every function the reference documents
/// and every construct ScriptDom parses, and each claim it makes of a construct is what the construct's example gives when
/// haruspex runs it. So a function the catalogue gains or loses, a node type a new ScriptDom adds, or a construct that comes to
/// be modelled changes the page, and the test fails until it is rewritten.
/// </summary>
public partial class CoveragePageTests
{
    private static readonly DocumentedFunctions Functions = DocumentedFunctions.Current;

    private static readonly SyntaxInventory Syntax = SyntaxInventory.Current;

    /// <summary>The entries whose examples are run through haruspex: every claim the probe can check.</summary>
    private static readonly Kind[] Run = [Kind.Modelled, Kind.NoEffect, Kind.Unknown, Kind.Partly];

    /// <summary>What haruspex says of each function probe and each example that parses, by its body, found once for every test.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<IrUnknownReason>>> Probed = new(() =>
    {
        var bodies = Functions.Names.Where(n => !CoveragePage.Rows(n).Any()).Select(n => CoveragePage.Probe(n, Functions))
            .Concat(Syntax.Entries.Where(e => Run.Contains(e.Claim)).SelectMany(e => e.Examples))
            .Distinct(StringComparer.Ordinal)
            .Where(b => ScriptDomNodes.In(b).Constructs is not null)
            .ToList();
        var reasons = CoverageProbe.Run(bodies);
        return bodies.Select((b, i) => (b, reasons[i])).ToDictionary(p => p.b, p => p.Item2, StringComparer.Ordinal);
    });

    [Fact]
    public void The_committed_page_is_what_haruspex_does_today()
    {
        var path = Path.Combine(RepositoryPaths.Root, CoveragePage.Path);
        var actual = CoveragePage.Render(Functions, Syntax, Probed.Value);
        var committed = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
        if (committed == actual)
            return;

        // As SnapshotStore does: rewritten only when asked, and never in CI.
        if (Environment.GetEnvironmentVariable(SnapshotStore.AcceptVariable) == "1" && !SnapshotStore.IsCiEnvironment(Environment.GetEnvironmentVariable))
        {
            File.WriteAllText(path, actual);
            return;
        }

        Assert.Fail(
            $"{CoveragePage.Path} is not what haruspex does today. If the change is meant, rewrite it with " +
            $"{SnapshotStore.AcceptVariable}=1 dotnet test --filter {nameof(CoveragePageTests)}, read the diff, and say why it " +
            "changed in the commit. The difference:\n" + SnapshotStore.UnifiedDiff(committed ?? "", actual, CoveragePage.Path, "what haruspex does"));
    }

    [Fact]
    public void Every_node_type_and_operator_the_parser_reads_has_one_entry_and_every_entry_names_one()
    {
        var nodes = Syntax.Entries.Select(e => e.Node).ToList();
        Assert.Empty(nodes.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(ScriptDomNodes.All.Except(nodes, StringComparer.Ordinal));
        Assert.Empty(nodes.Except(ScriptDomNodes.All, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_entry_has_the_examples_its_claim_needs_and_each_part_names_an_entry()
    {
        var problems = Syntax.Entries.Select(e => (e.Node, Problem: e.Claim switch
        {
            Kind.Modelled or Kind.NoEffect or Kind.Unknown or Kind.NotInABody when e.Examples.Count != 1 => "needs one example",
            Kind.Partly when e.Examples.Count != 2 => "needs two examples, the modelled form and the unknown one",
            Kind.NotParsed when e.Examples.Count > 1 => "takes one example at most",
            Kind.Within or Kind.Unlisted when e.Examples.Count > 0 => "takes no example",
            Kind.Within when !Syntax.Entries.Any(o => o.Node == e.Argument) => $"is part of {e.Argument}, which has no entry",
            Kind.Unknown or Kind.Partly when !Enum.GetValues<IrUnknownReason>().Any(r => IrWireNames.Of(r) == e.Argument) => $"names no reason {e.Argument}",
            _ => null,
        })).Where(p => p.Problem is not null).Select(p => $"{p.Node}: {p.Problem}").ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_example_parses_as_its_claim_says_and_holds_its_construct()
    {
        var problems = new List<string>();
        foreach (var entry in Syntax.Entries.Where(e => e.Examples.Count > 0))
        {
            foreach (var example in entry.Examples)
            {
                var (constructs, errors) = ScriptDomNodes.In(example);
                var problem = entry.Claim switch
                {
                    Kind.NotInABody => constructs is null ? null : "parses as a procedure's body",
                    Kind.NotParsed => constructs?.Contains(entry.Node) == true ? $"parses, holding {entry.Node}" : null,
                    _ when constructs is null => $"doesn't parse: {errors}",
                    _ => constructs.Contains(entry.Node) ? null : $"holds no {entry.Node}",
                };
                if (problem is not null)
                    problems.Add($"{entry.Node}: `{example}` {problem}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_example_gives_what_its_entry_claims()
    {
        var problems = new List<string>();
        foreach (var entry in Syntax.Entries.Where(e => Run.Contains(e.Claim)))
        {
            for (var i = 0; i < entry.Examples.Count; i++)
            {
                if (!Probed.Value.TryGetValue(entry.Examples[i], out var reasons))
                    continue; // It doesn't parse, which the test above reports.
                var wanted = entry.Claim is Kind.Unknown || (entry.Claim is Kind.Partly && i == 1) ? CoveragePage.Parse(entry.Argument!) : (IrUnknownReason?)null;
                var gives = reasons.Count == 0 ? "no unknown" : string.Join(", ", reasons.Select(IrWireNames.Of));
                if (wanted is { } reason ? !reasons.Contains(reason) : reasons.Count > 0)
                    problems.Add($"{entry.Node}: `{entry.Examples[i]}` gives {gives}, not {(wanted is { } r ? IrWireNames.Of(r) : "no unknown")}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void A_statement_without_an_example_is_one_the_walker_has_no_case_for()
    {
        // The walker's switch over statements: a statement it has no case for ends what it can list (Walker.Blocked).
        var walker = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "Haruspex.SqlServer.Inference", "Walker.cs"));
        var start = walker.IndexOf("private Outcome Statement(TSqlStatement statement", StringComparison.Ordinal);
        var end = walker.IndexOf("default:", start, StringComparison.Ordinal);
        // A case of an abstract statement (AlterTableStatement) covers its kinds, which their own entries describe.
        var cased = Case().Matches(walker[start..end]).Select(m => m.Groups["type"].Value).Where(ScriptDomNodes.All.Contains).ToHashSet(StringComparer.Ordinal);
        var entries = Syntax.Entries.ToDictionary(e => e.Node, StringComparer.Ordinal);

        Assert.NotEmpty(cased);
        var unclaimed = cased.Where(t => !entries.TryGetValue(t, out var e) || e.Claim is Kind.Unlisted or Kind.Unknown).ToList();
        Assert.True(unclaimed.Count == 0, "The walker has a case for these, which their entries say it lacks: " + string.Join(", ", unclaimed));
        Assert.Empty(Syntax.Entries.Where(e => e.Claim == Kind.Unlisted && cased.Contains(e.Node)).Select(e => e.Node));
    }

    [Fact]
    public void Every_function_of_the_catalogue_has_a_row_of_the_page()
    {
        var page = CoveragePage.Render(Functions, Syntax, Probed.Value);
        Assert.Empty(BuiltInFunctions.All.Where(f => !page.Contains($"| `{f.Code}` |", StringComparison.Ordinal)).Select(f => f.Code));
    }

    [Fact]
    public void Every_function_probe_parses()
    {
        var problems = Functions.Categories
            .SelectMany(c => c.Names.Where(n => !CoveragePage.Rows(n).Any()).Select(n => (Name: n, Body: FunctionProbes.For(n, c.Name))))
            .Select(p => (p.Name, ScriptDomNodes.In(p.Body).Errors))
            .Where(p => p.Errors.Length > 0)
            .Select(p => $"{p.Name}: {p.Errors}")
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_function_list_reads_its_categories_and_their_sources()
    {
        var read = DocumentedFunctions.Read(["# a header", "", "# https://example.com/one", "[One]", "A", "B", "# https://example.com/two", "[Two]", "B"]);

        Assert.Equal(["One", "Two"], read.Categories.Select(c => c.Name));
        Assert.Equal("https://example.com/two", read.Categories[1].Source);
        Assert.Equal(["A", "B"], read.Names);
    }

    [GeneratedRegex(@"case (?<type>\w+Statement)\b")]
    private static partial Regex Case();
}

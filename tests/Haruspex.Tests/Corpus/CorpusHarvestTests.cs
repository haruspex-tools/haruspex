using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.Tests.Snapshotting;

namespace Haruspex.Tests.Corpus;

/// <summary>
/// The harvest and the figures of the fetched corpora: <c>docs/corpus-harvest.md</c> is what haruspex gives on them today. A
/// test that needs a corpus skips when it hasn't been fetched, saying how to fetch it, and fails instead when
/// <c>HARUSPEX_REQUIRE_CORPORA=1</c>, as in CI, so the committed page is always checked there.
/// </summary>
public class CorpusHarvestTests
{
    private static readonly Dictionary<string, Lazy<FetchedCorpusRun>> Runs =
        FetchedCorpus.All.ToDictionary(c => c.Id, c => new Lazy<FetchedCorpusRun>(c.Run), StringComparer.Ordinal);

    public static TheoryData<string> Ids { get; } = [.. FetchedCorpus.All.Select(c => c.Id)];

    [Fact]
    public void The_committed_harvest_is_what_haruspex_gives_today()
    {
        FetchedCorpus.RequireAll(FetchedCorpus.All);
        var path = Path.Combine(RepositoryPaths.Root, CorpusHarvestPage.Path);
        var actual = CorpusHarvestPage.Render(Corpus.Runs, [.. FetchedCorpus.All.Select(c => Runs[c.Id].Value)]);
        var committed = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
        if (committed == actual)
            return;

        // As CorpusFiguresTests does: rewritten only when asked, and never in CI.
        if (Environment.GetEnvironmentVariable(SnapshotStore.AcceptVariable) == "1" && !SnapshotStore.IsCiEnvironment(Environment.GetEnvironmentVariable))
        {
            File.WriteAllText(path, actual);
            return;
        }

        Assert.Fail(
            $"{CorpusHarvestPage.Path} is not what haruspex gives on the corpora today. If the change is meant, rewrite it with " +
            $"{SnapshotStore.AcceptVariable}=1 dotnet test --filter {nameof(CorpusHarvestTests)}, read the diff, and say why the " +
            "figures moved in the commit. The difference:\n" +
            SnapshotStore.UnifiedDiff(committed ?? "", actual, CorpusHarvestPage.Path, "what haruspex gives"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_fetched_corpus_is_exactly_what_its_commit_holds(string id) =>
        FetchedCorpus.All.Single(c => c.Id == id).Require();

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_procedure_rows_of_a_fetched_corpus_add_up_to_every_procedure(string id)
    {
        FetchedCorpus.All.Single(c => c.Id == id).Require();
        var f = Runs[id].Value.Figures;

        Assert.Equal(
            f.Procedures,
            f.ProceduresReturningFields + f.ProceduresWithUnknownResults + f.ProceduresWithUnlistedResults + f.ProceduresWithNoResults + f.ProceduresWithoutFields);
    }

    [Fact]
    public void The_harvest_page_names_nothing_from_a_fetched_corpus()
    {
        // Counts only: the page must not carry a procedure's, table's or file's name out of a corpus whose licence may not
        // allow it. Every term in backticks on the page is haruspex's own (a reason or a code) or the manifest's (a commit, a
        // licence and its files, and what its descriptions quote), never a name read from the code.
        var page = File.ReadAllText(Path.Combine(RepositoryPaths.Root, CorpusHarvestPage.Path));
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        allowed.UnionWith(Enum.GetValues<IrUnknownReason>().Select(r => IrWireNames.Of(r)));
        allowed.UnionWith(FetchedCorpus.All.SelectMany(c => c.Licence.Notices.Append(c.Licence.File).Append(c.Licence.Spdx).Append(c.Commit)));
        allowed.UnionWith(FetchedCorpus.All.SelectMany(c => c.LeftOut.Select(o => o.Why).Append(c.About)).SelectMany(CodeSpans));

        Assert.All(CodeSpans(page).Distinct(StringComparer.Ordinal), t => Assert.Contains(t, allowed));

        // Nor anywhere else on the page: no file the manifest lists, by its path or its leaf name, but the licences and notices
        // the page names as such.
        var licences = FetchedCorpus.All.SelectMany(c => c.Licence.Notices.Append(c.Licence.File)).ToHashSet(StringComparer.Ordinal);
        var named = FetchedCorpus.All.SelectMany(c => c.Files).Select(f => f.Path).Where(p => !licences.Contains(p))
            .SelectMany(p => new[] { p, p[(p.LastIndexOf('/') + 1)..] })
            .Distinct(StringComparer.Ordinal);
        Assert.All(named, n => Assert.DoesNotContain(n, page, StringComparison.Ordinal));
    }

    private static IEnumerable<string> CodeSpans(string markdown) =>
        System.Text.RegularExpressions.Regex.Matches(markdown, "`([^`]+)`").Select(m => m.Groups[1].Value);

    // The summing and the ranking, on figures built by hand.

    private static readonly IrScalar Int = new(IrScalarKind.Number, null, "int");

    private static IrObject Procedure(string id, EquatableArray<IrResult>? results) => new(id, id, IrObjectKind.Procedure, "p.sql:1:1", [], results);

    private static CorpusFigures Figures(params (string Reason, int Fields, int Procedures)[] reasons) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [.. reasons.Select(r => new CorpusFigures.Reason(r.Reason, r.Fields, 0, r.Procedures, 0))]);

    [Fact]
    public void Figures_of_several_databases_are_summed_count_by_count_and_reason_by_reason()
    {
        var a = CorpusFigures.Count(new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.p", [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Row")), [])])],
            new Dictionary<string, IrObjectType> { ["Row"] = new([new IrField("id", Int, IrPresence.Always, []), new IrField("x", new IrUnknownType(IrUnknownReason.UnsupportedExpression), IrPresence.Always, [])]) },
            []));
        var b = CorpusFigures.Count(new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.dynamic", null)],
            new Dictionary<string, IrObjectType>(),
            [new IrUnknown("s.dynamic", "results", IrUnknownReason.DynamicSql, "EXEC (@sql)", "p.sql:2:1")]));

        var sum = CorpusFigures.Sum([a, b]);

        Assert.Equal((2, 1, 1, 2, 1), (sum.Procedures, sum.ProceduresReturningFields, sum.ProceduresWithUnlistedResults, sum.Fields, sum.FieldsTyped));
        Assert.Equal(
            [new CorpusFigures.Reason(IrWireNames.Of(IrUnknownReason.DynamicSql), 0, 0, 1, 0), new CorpusFigures.Reason(IrWireNames.Of(IrUnknownReason.UnsupportedExpression), 1, 0, 0, 0)],
            sum.Unknowns);
    }

    [Fact]
    public void A_corpus_whose_procedures_return_no_fields_says_so_rather_than_typed_none_of_none()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.writes", []), Procedure("s.dynamic", null)],
            new Dictionary<string, IrObjectType>(),
            [new IrUnknown("s.dynamic", "results", IrUnknownReason.DynamicSql, "EXEC (@sql)", "p.sql:2:1")]);

        Assert.Equal(
            "**No procedure in Shop returns fields; the results of 1 of its 2 procedures can't be described.**",
            CorpusFiguresPage.Summary("Shop", CorpusFigures.Count(document)));
    }

    [Fact]
    public void The_harvest_ranks_reasons_by_places_across_corpora_then_by_name()
    {
        var rows = CorpusHarvest.Unknowns([Figures(("b", 2, 0), ("c", 1, 0)), Figures(("a", 0, 3), ("c", 1, 0))]);

        Assert.Equal(
            [new CorpusHarvest.Row("a", 0, 0, 3, 0, 1), new CorpusHarvest.Row("b", 2, 0, 0, 0, 1), new CorpusHarvest.Row("c", 2, 0, 0, 0, 2)],
            rows);
    }

    [Fact]
    public void Diagnostics_are_harvested_by_code_most_often_first()
    {
        var parse = new DiagnosticCode(1001, DiagnosticSeverity.Error, "parse");
        var twice = new DiagnosticCode(1002, DiagnosticSeverity.Error, "twice");

        var rows = CorpusHarvest.Diagnostics([[twice.At(null, "x"), parse.At(null, "y")], [parse.At(null, "z")]]);

        Assert.Equal([new CorpusHarvest.CodeRow(parse, 2, 2), new CorpusHarvest.CodeRow(twice, 1, 1)], rows);
    }
}

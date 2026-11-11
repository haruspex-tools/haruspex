using Haruspex.Ir;
using Haruspex.Tests.Snapshotting;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests;

/// <summary>
/// The coverage figures on Microsoft's sample databases: <c>docs/corpus-figures.md</c> is what haruspex gives on them today,
/// and every figure quoted elsewhere is the page's. Public figures come only from these corpora, written by this test
/// (<c>docs/decisions/0020-public-figures-come-from-public-corpora.md</c>), so none can drift from the code.
/// </summary>
public class CorpusFiguresTests
{
    private static readonly Lazy<IReadOnlyList<CorpusRun>> Runs = new(() => [.. Corpus.All.Select(c => c.Run())]);

    [Fact]
    public void The_committed_figures_are_what_haruspex_gives_today()
    {
        var path = Path.Combine(TestPaths.RepoRoot, CorpusFiguresPage.Path);
        var actual = CorpusFiguresPage.Render(CorpusManifest.Current, Runs.Value);
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
            $"{CorpusFiguresPage.Path} is not what haruspex gives on the corpora today. If the change is meant, rewrite it with " +
            $"{SnapshotStore.AcceptVariable}=1 dotnet test --filter {nameof(CorpusFiguresTests)}, read the diff, and say why the " +
            "figures moved in the commit. The difference:\n" +
            SnapshotStore.UnifiedDiff(committed ?? "", actual, CorpusFiguresPage.Path, "what haruspex gives"));
    }

    [Fact]
    public void The_style_guide_quotes_the_adventure_works_figure()
    {
        // GitHub's Markdown has no include, so the style guide's example is the page's figure, copied, and held to it here.
        var run = Runs.Value.Single(r => r.Corpus.Name == "AdventureWorks");
        var headline = CorpusFiguresPage.Headline("AdventureWorks", CorpusFigures.Count(run.Document));

        Assert.Contains($"\"{headline}\"", File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "docs", "style.md")), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void Every_script_of_a_corpus_is_read_or_left_out_with_a_reason(string name)
    {
        var corpus = Corpus.All.Single(c => c.Name == name);
        var all = Directory.EnumerateFiles(corpus.Directory, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(corpus.Directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        var accounted = corpus.Scripts.Concat(corpus.LeftOut.SelectMany(o => o.Files)).Order(StringComparer.Ordinal);

        Assert.Equal(all, accounted);
        Assert.All(corpus.LeftOut, o => Assert.NotEmpty(o.Files));
    }

    [Fact]
    public void Every_script_read_parses_the_same_with_quoted_identifiers_off()
    {
        // WideWorldImporters builds most scripts with QUOTED_IDENTIFIER off, and Sources parses with it on. The page says
        // that makes no difference here: no script has a double-quoted name, which is the only thing the setting changes.
        var quoted = Corpus.All
            .SelectMany(c => c.Scripts.Select(s => Path.Combine(c.Directory, s)))
            .Where(path => QuotedNames(path) > 0)
            .ToList();

        Assert.Empty(quoted);
    }

    public static TheoryData<string> CorpusNames { get; } = [.. Corpus.All.Select(c => c.Name)];

    // The counting, on documents built by hand: what counts as a field, when it is typed, and what is unknown.

    private static readonly IrScalar Int = new(IrScalarKind.Number, null, "int");

    private static IrField Field(string name, IrType type) => new(name, type, IrPresence.Always, []);

    private static IrObject Procedure(string id, IReadOnlyList<IrResult>? results) => new(id, id, IrObjectKind.Procedure, "p.sql:1:1", [], results);

    private static IrResult Rowset(string type) => new(IrEncoding.Rowset, new IrArray(new IrRef(type)), []);

    [Fact]
    public void A_field_that_holds_an_object_is_not_counted_but_its_fields_are()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.p", [Rowset("Order")])],
            new Dictionary<string, IrObjectType>
            {
                ["Order"] = new([Field("id", Int), Field("lines", new IrJsonText(new IrArray(new IrRef("Line"))))]),
                ["Line"] = new([Field("qty", Int), Field("note", new IrUnknownType(IrUnknownReasons.UnsupportedExpression))]),
            },
            []);

        var figures = CorpusFigures.Count(document);

        Assert.Equal(3, figures.Fields);
        Assert.Equal(2, figures.FieldsTyped);
        Assert.Equal(1, figures.ProceduresReturningFields);
        Assert.Equal(0, figures.ProceduresFullyTyped);
        Assert.Equal([new CorpusFigures.Reason(IrUnknownReasons.UnsupportedExpression, 1, 0, 0, 0)], figures.Unknowns);
    }

    [Fact]
    public void A_type_shared_by_two_results_is_counted_once()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.a", [Rowset("Row")]), Procedure("s.b", [Rowset("Row")])],
            new Dictionary<string, IrObjectType> { ["Row"] = new([Field("id", Int)]) },
            []);

        var figures = CorpusFigures.Count(document);

        Assert.Equal(1, figures.Fields);
        Assert.Equal(2, figures.ProceduresFullyTyped);
    }

    [Fact]
    public void Json_text_of_unknown_content_is_typed_and_counted_apart()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.p", [Rowset("Row")])],
            new Dictionary<string, IrObjectType> { ["Row"] = new([Field("doc", new IrJsonText())]) },
            []);

        var figures = CorpusFigures.Count(document);

        Assert.Equal((1, 1, 1), (figures.Fields, figures.FieldsTyped, figures.FieldsOfJsonText));
        Assert.Equal(1, figures.ProceduresFullyTyped);
    }

    [Fact]
    public void Unlisted_and_wholly_unknown_results_are_counted_by_reason_and_not_as_fields()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [
                Procedure("s.dynamic", null),
                Procedure("s.opaque", [new IrResult(IrEncoding.Rowset, new IrArray(new IrUnknownType(IrUnknownReasons.UnsupportedQuery)), [])]),
                Procedure("s.writes", []),
            ],
            new Dictionary<string, IrObjectType>(),
            [new IrUnknown("s.dynamic", "results", IrUnknownReasons.DynamicSql, "EXEC (@sql)", "p.sql:2:1")]);

        var figures = CorpusFigures.Count(document);

        Assert.Equal((0, 0), (figures.Fields, figures.ProceduresReturningFields));
        Assert.Equal((1, 1, 1), (figures.ProceduresWithUnlistedResults, figures.ResultsUnknown, figures.ProceduresWithNoResults));
        Assert.Equal(
            [new CorpusFigures.Reason(IrUnknownReasons.DynamicSql, 0, 0, 1, 0), new CorpusFigures.Reason(IrUnknownReasons.UnsupportedQuery, 0, 1, 0, 0)],
            figures.Unknowns);
    }

    [Fact]
    public void A_parameter_of_unknown_type_is_counted_by_reason_and_not_as_a_field()
    {
        var parameter = new IrParameter("@rows", new IrUnknownType(IrUnknownReasons.UnsupportedSqlType), HasDefault: false, IsOutput: false);
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.load", []) with { Parameters = [parameter] }],
            new Dictionary<string, IrObjectType>(),
            []);

        var figures = CorpusFigures.Count(document);

        Assert.Equal(0, figures.Fields);
        Assert.Equal([new CorpusFigures.Reason(IrUnknownReasons.UnsupportedSqlType, 0, 0, 0, 1)], figures.Unknowns);
    }

    private static int QuotedNames(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var tokens = new TSql170Parser(initialQuotedIdentifiers: true).GetTokenStream(reader, out _);
        return tokens.Count(t => t.TokenType == TSqlTokenType.QuotedIdentifier && t.Text.StartsWith('"'));
    }
}

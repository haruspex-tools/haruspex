using Haruspex.Ir;
using Haruspex.Tests.Snapshotting;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Corpus;

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
        var path = Path.Combine(RepositoryPaths.Root, CorpusFiguresPage.Path);
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

        Assert.Contains($"\"{headline}\"", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "style.md")), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void Every_script_of_a_corpus_is_read_or_left_out_with_a_reason(string name)
    {
        var corpus = Corpus.All.Single(c => c.Name == name);
        var all = Directory.EnumerateFiles(corpus.ProjectFolder, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(corpus.ProjectFolder, path).Replace('\\', '/'))
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
            .SelectMany(c => c.Scripts.Select(s => Path.Combine(c.ProjectFolder, s)))
            .Where(path => QuotedNames(path) > 0)
            .ToList();

        Assert.Empty(quoted);
    }

    [Theory]
    [MemberData(nameof(CorpusNames))]
    public void The_procedure_rows_add_up_to_every_procedure(string name)
    {
        var f = CorpusFigures.Count(Runs.Value.Single(r => r.Corpus.Name == name).Document);

        Assert.Equal(
            f.Procedures,
            f.ProceduresReturningFields + f.ProceduresWithUnknownResults + f.ProceduresWithUnlistedResults + f.ProceduresWithNoResults + f.ProceduresWithoutFields);
    }

    [Fact]
    public void A_headline_says_how_many_procedures_it_cannot_describe()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.p", [Rowset("Row")]), Procedure("s.dynamic", null)],
            new Dictionary<string, IrObjectType> { ["Row"] = new([Field("id", Int)]) },
            []);

        Assert.Equal(
            "Typed 1 of 1 fields in Shop; the results of 1 of its 2 procedures can't be described, so they have no fields to count",
            CorpusFiguresPage.Headline("Shop", CorpusFigures.Count(document)));
    }

    public static TheoryData<string> CorpusNames { get; } = [.. Corpus.All.Select(c => c.Name)];

    // The counting, on documents built by hand: what counts as a field, when it is typed, and what is unknown.

    private static readonly IrScalar Int = new(IrScalarKind.Number, null, "int");

    private static IrField Field(string name, IrType type) => new(name, type, IrPresence.Always, []);

    private static IrObject Procedure(string id, EquatableArray<IrResult>? results) => new(id, id, IrObjectKind.Procedure, "p.sql:1:1", [], results);

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
                ["Line"] = new([Field("qty", Int), Field("note", new IrUnknownType(IrUnknownReason.UnsupportedExpression))]),
            },
            []);

        var figures = CorpusFigures.Count(document);

        Assert.Equal(3, figures.Fields);
        Assert.Equal(2, figures.FieldsTyped);
        Assert.Equal(1, figures.ProceduresReturningFields);
        Assert.Equal(0, figures.ProceduresFullyTyped);
        Assert.Equal([new CorpusFigures.Reason(IrWireNames.Of(IrUnknownReason.UnsupportedExpression), 1, 0, 0, 0)], figures.Unknowns);
    }

    [Fact]
    public void A_union_or_tuple_of_objects_is_not_a_field_unless_part_of_it_is_unknown()
    {
        var unknown = new IrUnknownType(IrUnknownReason.UnsupportedQuery);
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.p", [Rowset("Holder")])],
            new Dictionary<string, IrObjectType>
            {
                ["Holder"] = new(
                [
                    Field("either", new IrUnion([new IrRef("A"), new IrRef("B")])),
                    Field("pair", new IrTuple([new IrElement(new IrRef("A"), []), new IrElement(Int, [])])),
                    Field("maybe", new IrUnion([new IrRef("A"), unknown])),
                ]),
                ["A"] = new([Field("a", Int)]),
                ["B"] = new([Field("b", Int)]),
            },
            []);

        var figures = CorpusFigures.Count(document);

        // a, b, and "maybe", whose unknown part would otherwise go uncounted.
        Assert.Equal((3, 2), (figures.Fields, figures.FieldsTyped));
        Assert.Equal([new CorpusFigures.Reason(IrWireNames.Of(IrUnknownReason.UnsupportedQuery), 1, 0, 0, 0)], figures.Unknowns);
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
    public void Unlisted_and_unknown_results_are_counted_by_reason_and_not_as_fields()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [
                Procedure("s.dynamic", null),
                Procedure("s.opaque", [new IrResult(IrEncoding.Rowset, new IrArray(new IrUnknownType(IrUnknownReason.UnsupportedQuery)), [])]),
                Procedure("s.writes", []),
            ],
            new Dictionary<string, IrObjectType>(),
            [new IrUnknown("s.dynamic", "results", IrUnknownReason.DynamicSql, "EXEC (@sql)", "p.sql:2:1")]);

        var figures = CorpusFigures.Count(document);

        Assert.Equal((0, 0), (figures.Fields, figures.ProceduresReturningFields));
        Assert.Equal((1, 1, 1), (figures.ProceduresWithUnlistedResults, figures.ProceduresWithUnknownResults, figures.ProceduresWithNoResults));
        Assert.Equal((1, 2), (figures.ResultsUnknown, figures.ProceduresNotDescribed));
        Assert.Equal(
            [new CorpusFigures.Reason(IrWireNames.Of(IrUnknownReason.DynamicSql), 0, 0, 1, 0), new CorpusFigures.Reason(IrWireNames.Of(IrUnknownReason.UnsupportedQuery), 0, 1, 0, 0)],
            figures.Unknowns);
    }

    [Fact]
    public void A_parameter_of_unknown_type_is_counted_by_reason_and_not_as_a_field()
    {
        var parameter = new IrParameter("@rows", new IrUnknownType(IrUnknownReason.UnsupportedSqlType), HasDefault: false, IsOutput: false);
        var document = new IrDocument(
            new IrSource("tsql"),
            [Procedure("s.load", []) with { Parameters = [parameter] }],
            new Dictionary<string, IrObjectType>(),
            []);

        var figures = CorpusFigures.Count(document);

        Assert.Equal(0, figures.Fields);
        Assert.Equal([new CorpusFigures.Reason(IrWireNames.Of(IrUnknownReason.UnsupportedSqlType), 0, 0, 0, 1)], figures.Unknowns);
    }

    private static int QuotedNames(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var tokens = new TSql170Parser(initialQuotedIdentifiers: true).GetTokenStream(reader, out _);
        return tokens.Count(t => t.TokenType == TSqlTokenType.QuotedIdentifier && t.Text.StartsWith('"'));
    }
}

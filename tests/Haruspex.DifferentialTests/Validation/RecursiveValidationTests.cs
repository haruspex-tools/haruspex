using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>
/// The validator and the coverage check on what IR 0.4 adds: the content of JSON text, and object types that refer to
/// themselves. A recursive type is checked at every depth the data reaches, and its cases are counted by the type's
/// name (<c>&lt;Node&gt;.children</c>), so a tree's leaves at any depth cover them and the check ends.
/// </summary>
public class RecursiveValidationTests
{
    private static readonly IrNullReason Because = new(IrNullRules.EmptySubquery, "f.sql:3:5");
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormats.Int32, "int");

    private static readonly Dictionary<string, IrObjectType> Types = new()
    {
        ["Row"] = new([new IrField("tree", new IrArray(new IrRef("Node")), IrPresence.OmittedWhenNull, [Because])]),
        ["Node"] = new([
            new IrField("id", Int, IrPresence.Always, []),
            new IrField("children", new IrArray(new IrRef("Node")), IrPresence.OmittedWhenNull, [Because]),
        ])
        { Recursive = true },
    };

    private static IrDocument Doc(IrType result) => new(
        new IrSource("tsql"),
        [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, result, [])])],
        Types,
        []);

    private static ValidationReport Validate(string json) =>
        IrValidator.Validate(Doc(new IrArray(new IrRef("Row"))), "dbo.p", [new ObservedResultSet(["json"], [[json]])]);

    [Fact]
    public void A_recursive_type_is_checked_at_every_depth()
    {
        Assert.Empty(Validate("""[{"tree":[{"id":1,"children":[{"id":2,"children":[{"id":3}]}]}]}]""").Violations);

        var deep = Assert.Single(Validate("""[{"tree":[{"id":1,"children":[{"id":2,"children":[{"id":null}]}]}]}]""").Violations);
        Assert.Equal("results[0][0].tree[0].children[0].children[0].id", deep.Path);
    }

    [Fact]
    public void Its_cases_are_counted_by_the_types_name()
    {
        var seen = Validate("""[{"tree":[{"id":1,"children":[{"id":2}]}]}]""").Seen;

        Assert.Contains(("<Node>.children", Seen.Value), seen);
        Assert.Contains(("<Node>.children", Seen.Null), seen);
        Assert.Contains(("<Node>.id", Seen.Value), seen);
    }

    [Fact]
    public void Coverage_walks_a_recursive_type_once()
    {
        var missing = Coverage.Missing(Doc(new IrArray(new IrRef("Row"))), new Dictionary<string, IReadOnlySet<(string, Seen)>>());

        Assert.Equal(
            [
                "dbo.p <Node>.children: a value",
                "dbo.p <Node>.children: null",
                "dbo.p <Node>.id: a value",
                "dbo.p results[0]: a value",
                "dbo.p results[0][].tree: a value",
                "dbo.p results[0][].tree: null",
            ],
            missing);
    }

    [Fact]
    public void Json_text_with_content_is_parsed_and_checked_against_it()
    {
        var doc = Doc(new IrArray(new IrRef("Text")));
        var types = new Dictionary<string, IrObjectType>(Types) { ["Text"] = new([new IrField("t", new IrJsonText(new IrArray(new IrRef("Node"))), IrPresence.Always, [])]) };
        doc = doc with { Types = types };

        Assert.Empty(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["json"], [["""[{"t":"[{\"id\":1}]"}]"""]])]).Violations);
        var wrong = Assert.Single(IrValidator.Validate(doc, "dbo.p", [new ObservedResultSet(["json"], [["""[{"t":"[{\"id\":\"x\"}]"}]"""]])]).Violations);
        Assert.Equal("results[0][0].t<json>[0].id", wrong.Path);

        // In a rowset too.
        var rows = new IrDocument(doc.Source, [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Text")), [])])], types, []);
        var rowWrong = Assert.Single(IrValidator.Validate(rows, "dbo.p", [new ObservedResultSet(["t"], [["""[{"id":1,"children":"no"}]"""]])]).Violations);
        Assert.Equal("results[0][0].t<json>[0].children", rowWrong.Path);
    }
}

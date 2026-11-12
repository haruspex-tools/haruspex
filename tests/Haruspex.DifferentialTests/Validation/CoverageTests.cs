using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

public class CoverageTests
{
    private static readonly IrNullReason Because = new(IrNullRule.NullableColumn, "dbo.t.c");

    private static readonly IrDocument Doc = new(
        new IrSource("tsql"),
        [
            new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [
                new IrResult(IrEncoding.Json, new IrArray(new IrRef("Row")), [new IrNullReason(IrNullRule.NoRows, "p.sql:1:1")]),
            ]),
            new IrObject("dbo.q", "Q", IrObjectKind.Procedure, "q.sql:1:1", [], null),
        ],
        new Dictionary<string, IrObjectType>
        {
            ["Row"] = new([
                new IrField("id", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"), IrPresence.Always, []),
                new IrField("lines", new IrArray(new IrRef("Line")), IrPresence.OmittedWhenNull, [Because]),
                new IrField("x", new IrUnknownType(IrUnknownReason.UnsupportedExpression), IrPresence.OmittedWhenNull, [Because]),
            ]),
            ["Line"] = new([new IrField("note", new IrScalar(IrScalarKind.String, null, "nvarchar(10)"), IrPresence.OmittedWhenNull, [Because])]),
        },
        []);

    [Fact]
    public void Every_case_the_ir_allows_is_required_except_under_unknown_types() =>
        Assert.Equal(
            [
                "dbo.p results[0]: a value",
                "dbo.p results[0]: absent",
                "dbo.p results[0][].id: a value",
                "dbo.p results[0][].lines: a value",
                "dbo.p results[0][].lines: null",
                "dbo.p results[0][].lines[].note: a value",
                "dbo.p results[0][].lines[].note: null",
            ],
            Coverage.Missing(Doc, new Dictionary<string, IReadOnlySet<(string, Seen)>>()));

    [Fact]
    public void What_was_seen_is_not_missing()
    {
        var seen = new Dictionary<string, IReadOnlySet<(string, Seen)>>
        {
            ["dbo.p"] = new HashSet<(string, Seen)>
            {
                ("results[0]", Seen.Absent), ("results[0]", Seen.Value), ("results[0][].id", Seen.Value),
                ("results[0][].lines", Seen.Value), ("results[0][].lines", Seen.Null), ("results[0][].lines[].note", Seen.Value),
            },
        };

        Assert.Equal(["dbo.p results[0][].lines[].note: null"], Coverage.Missing(Doc, seen));
    }
}

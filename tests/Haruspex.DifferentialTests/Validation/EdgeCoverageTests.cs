using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

public class EdgeCoverageTests
{
    private static readonly IrFactSource From = new(IrFactSources.Type, "t");

    private static IrDocument Doc(params IrField[] fields) => new(
        new IrSource("tsql"),
        [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Row")), [])])],
        new Dictionary<string, IrObjectType> { ["Row"] = new([.. fields]) },
        []);

    private static IrField Field(string name, IrScalar scalar) => new(name, scalar, IrPresence.Always, []);

    [Fact]
    public void Every_bound_length_and_value_is_an_edge_to_reach()
    {
        var doc = Doc(
            Field("n", new IrScalar(IrScalarKind.Number, IrFormats.Int32, "int", new IrFacets(Min: new IrBound("0", false, From), Max: new IrBound("9", true, From)))),
            Field("s", new IrScalar(IrScalarKind.String, null, "nvarchar(4)", new IrFacets(
                Length: new IrLength(4, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, From),
                MinLength: new IrMinLength(1, From),
                Values: new IrValueSet([new IrValue("a", true), new IrValue("bb", true)], IrValueMatch.Collation, [From])))),
            // Under an exact set only the values are required: its type's bounds may be out of the set's reach.
            Field("e", new IrScalar(IrScalarKind.Number, IrFormats.Int16, "smallint", new IrFacets(
                Min: new IrBound("-32768", true, From), Max: new IrBound("32767", true, From),
                Values: new IrValueSet([new IrValue("1", false)], IrValueMatch.Exact, [From])))),
            // A float's exclusive bound has no nearest step, so nothing to require.
            Field("f", new IrScalar(IrScalarKind.Number, IrFormats.Float, "float", new IrFacets(Min: new IrBound("0", false, From)))));

        Assert.Equal(
            [
                "dbo.p results[0][].e: value 1",
                "dbo.p results[0][].n: max",
                "dbo.p results[0][].n: min",
                "dbo.p results[0][].s: length 4",
                "dbo.p results[0][].s: min length 1",
                "dbo.p results[0][].s: value a",
                "dbo.p results[0][].s: value bb",
            ],
            Coverage.MissingEdges(doc, new Dictionary<string, IReadOnlySet<(string, string)>>()));

        var reached = new Dictionary<string, IReadOnlySet<(string, string)>>
        {
            ["dbo.p"] = new HashSet<(string, string)> { ("results[0][].n", "min"), ("results[0][].s", "length 4") },
        };
        Assert.DoesNotContain("dbo.p results[0][].n: min", Coverage.MissingEdges(doc, reached));
        Assert.DoesNotContain("dbo.p results[0][].s: length 4", Coverage.MissingEdges(doc, reached));
    }
}

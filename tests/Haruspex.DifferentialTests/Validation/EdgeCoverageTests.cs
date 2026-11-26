using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

public class EdgeCoverageTests
{
    private static readonly IrFactSource From = new(IrFactSourceKind.Type, "t");

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
            Field("n", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int", new IrFacets(Min: new IrBound("0", false, From), Max: new IrBound("9", true, From)))),
            Field("s", new IrScalar(IrScalarKind.String, null, "nvarchar(4)", new IrFacets(
                Length: new IrLength(4, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, From),
                MinLength: new IrMinLength(1, From),
                Values: new IrValueSet([new IrValue("a", true), new IrValue("bb", true)], IrValueMatch.Collation, [From])))),
            // Under an exact set only the values are required: its type's bounds may be out of the set's reach.
            Field("e", new IrScalar(IrScalarKind.Number, IrFormat.Int16, "smallint", new IrFacets(
                Min: new IrBound("-32768", true, From), Max: new IrBound("32767", true, From),
                Values: new IrValueSet([new IrValue("1", false)], IrValueMatch.Exact, [From])))),
            // A float's exclusive bound has no nearest step, so nothing to require.
            Field("f", new IrScalar(IrScalarKind.Number, IrFormat.Float, "float", new IrFacets(Min: new IrBound("0", false, From)))));

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

    /// <summary>
    /// Every fixture is held to the edges of facts its types alone don't give: a CHECK's bound, a declared or engine's length,
    /// every value of a set whatever its source, but not a bound or length the type gives, a single row or an array's items.
    /// </summary>
    [Fact]
    public void Only_a_fact_the_type_alone_does_not_give_is_an_edge_every_fixture_reaches()
    {
        var check = new IrFactSource(IrFactSourceKind.Check, "dbo.ck_n");
        var doc = Doc(
            Field("n", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int", new IrFacets(Min: new IrBound("1", true, check), Max: new IrBound("2147483647", true, From)))),
            Field("s", new IrScalar(IrScalarKind.String, null, "nvarchar(200)", new IrFacets(
                Length: new IrLength(200, false, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Declared, "dbo.t.s")),
                MinLength: new IrMinLength(1, From),
                Values: new IrValueSet([new IrValue("a", true)], IrValueMatch.Collation, [From])))));

        Assert.Equal(
            ["dbo.p results[0][].n: min", "dbo.p results[0][].s: length 200", "dbo.p results[0][].s: value a"],
            Coverage.MissingEdges(doc, new Dictionary<string, IReadOnlySet<(string, string)>>(), EdgeScope.NotFromTheType));
    }

    [Theory]
    [InlineData("bazaar.mixed", "bazaar.mixed results[1][].units: min", true)]
    [InlineData("bazaar.mixed results[1][].units", "bazaar.mixed results[1][].units: min", true)]
    [InlineData("bazaar.mixed results[1]", "bazaar.mixed results[1][].units: min", true)]
    [InlineData("bazaar.mixed results[1][].units: min", "bazaar.mixed results[1][].units: min", true)]
    [InlineData("bazaar.mixed results[1][].units: min", "bazaar.mixed results[1][].units: max", false)]
    [InlineData("bazaar.mixed results[1][].unit", "bazaar.mixed results[1][].units: min", false)]
    [InlineData("bazaar.mix", "bazaar.mixed results[1][].units: min", false)]
    [InlineData("bazaar.mixed results[1]", "bazaar.mixed results[10][].units: min", false)]
    public void An_excuse_names_a_procedure_a_path_in_one_or_one_edge(string excuse, string edge, bool excused) =>
        Assert.Equal(excused, Runs.Excuses(excuse, edge));
}

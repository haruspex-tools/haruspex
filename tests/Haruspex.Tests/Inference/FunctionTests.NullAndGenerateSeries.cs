using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void A_bare_null_is_an_int_that_is_always_null()
    {
        // A_bare_null_is_an_int.
        var doc = Procedure("SELECT NULL AS n, CAST(NULL AS date) AS d, DATEADD(day, NULL, da) AS e FROM dbo.t;");
        Assert.Equal("int null-literal", Typed(doc, "n"));
        Assert.Equal("date null-literal", Typed(doc, "d"));

        // DATEADD with the NULL literal as its number does not compile (error 8116).
        Unknown(doc, "e");
    }

    [Fact]
    public void Generate_series_gives_value_of_its_arguments_type_never_null()
    {
        // Generate_series_gives_value_of_its_arguments_type_never_null_and_no_rows_for_a_null_argument.
        var doc = Procedure("""
            SELECT g.value AS a FROM GENERATE_SERIES(1, 10, 2) AS g;
            SELECT value AS b FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(CAST(1 AS tinyint), t.ti) AS g;
            SELECT value AS c FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(t.nd, 3.25) AS g;
            SELECT value AS d FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(t.ti, 10) AS g;
            SELECT t.id, s.value AS e FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(1, t.n) AS s;
            SELECT value AS f FROM dbo.t AS t CROSS APPLY GENERATE_SERIES(t.d, 100.5) AS g;
            """);
        string Of(int index, string name)
        {
            var result = doc.Objects[0].Results![index];
            var row = (IrRef)((IrArray)result.Type).Items;
            var field = Assert.Single(doc.Types[row.Name].Fields, f => f.Name == name);
            return field.Type is IrScalar scalar ? string.Join(" ", [scalar.Sql!, .. field.NullableBecause.Select(r => r.Rule)]) : "unknown";
        }

        Assert.Equal("int", Of(0, "a"));
        Assert.Equal("tinyint", Of(1, "b"));
        Assert.Equal("numeric(6,2)", Of(2, "c"));

        // Arguments of different types are an error on the server.
        Assert.Equal("unknown", Of(3, "d"));
        Assert.Equal("int", Of(4, "e"));

        // decimal beside a numeric literal is an error too (error 5373): the names must be the same.
        Assert.Equal("unknown", Of(5, "f"));
    }
}

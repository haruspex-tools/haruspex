using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class ExecTests
{
    [Fact]
    public void A_procedure_that_calls_itself_reaches_a_fixed_point()
    {
        var doc = Infer(("p", """
            CREATE PROCEDURE dbo.p @n int AS
            IF @n <= 0 SELECT t.id FROM dbo.t AS t;
            ELSE BEGIN DECLARE @m int = @n - 1; EXEC dbo.p @m; END
            """));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
        Assert.False(Results(doc)[0].MayBeAbsent);
    }

    [Fact]
    public void Procedures_that_call_each_other_reach_a_fixed_point()
    {
        var doc = Infer(
            ("even", """
                CREATE PROCEDURE dbo.even @n int AS
                IF @n = 0 SELECT t.id FROM dbo.t AS t;
                ELSE BEGIN DECLARE @m int = @n - 1; EXEC dbo.odd @m; END
                """),
            ("odd", """
                CREATE PROCEDURE dbo.odd @n int AS
                IF @n = 0 SELECT t.name FROM dbo.t AS t;
                ELSE BEGIN DECLARE @m int = @n - 1; EXEC dbo.even @m; END
                """));

        foreach (var id in new[] { "dbo.even", "dbo.odd" })
        {
            var result = Assert.Single(Results(doc, id));
            var union = Assert.IsType<IrUnion>(result.Type);
            Assert.Equal(["id", "name"], union.Variants.SelectMany(v => Names(doc, v)).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void A_recursion_that_returns_more_at_each_depth_is_unknown()
    {
        var why = ResultsUnknown(Infer(("p", """
            CREATE PROCEDURE dbo.p @n int AS
            SELECT t.id FROM dbo.t AS t;
            IF @n > 0 BEGIN DECLARE @m int = @n - 1; EXEC dbo.p @m; END
            """)));

        Assert.Equal(IrUnknownReason.UnresolvedRecursion, why.Reason);
    }

    [Fact]
    public void A_recursive_call_makes_the_tables_it_writes_unknown()
    {
        var doc = Infer(("p", """
            CREATE PROCEDURE dbo.p @n int AS
            IF @n > 0 BEGIN INSERT INTO #acc (a) VALUES (@n); DECLARE @m int = @n - 1; EXEC dbo.p @m; END
            """),
            ("outer_p", """
                CREATE PROCEDURE dbo.outer_p AS
                SELECT t.id AS a INTO #acc FROM dbo.t AS t;
                EXEC dbo.p 3;
                SELECT c.a FROM #acc AS c;
                """));

        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.outer_p" && u.Path == "results[0][].a");
    }

    [Fact]
    public void A_recursive_call_from_changed_tables_is_read_from_those_tables()
    {
        // The recursive call reads #r after its caller wrote a null into it, so its result may be null.
        var doc = Infer(
            ("rec", """
                CREATE PROCEDURE dbo.rec @n int AS
                IF @n <= 0 SELECT r.x FROM #r AS r;
                ELSE BEGIN INSERT INTO #r (x) SELECT i.qty FROM dbo.item AS i; DECLARE @m int = @n - 1; EXEC dbo.rec @m; END
                """),
            ("rec_caller", """
                CREATE PROCEDURE dbo.rec_caller @n int AS
                DECLARE @v int = 1;
                SELECT @v AS x INTO #r;
                EXEC dbo.rec @n;
                """));

        var result = Assert.Single(Results(doc, "dbo.rec_caller"));
        var variants = result.Type is IrUnion union ? union.Variants : [result.Type];
        Assert.All(variants, v => Assert.True(ClaimsNothingNonNull(Assert.Single(Fields(doc, v), f => f.Name == "x"))));
    }
}

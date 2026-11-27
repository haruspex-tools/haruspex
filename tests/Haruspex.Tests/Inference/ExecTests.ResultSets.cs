using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class ExecTests
{
    [Fact]
    public void A_callees_result_sets_arrive_where_it_is_called_in_order()
    {
        // Observed: A_callees_result_sets_arrive_in_the_callers_stream_where_it_is_called.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id AS b FROM dbo.t AS t; SELECT t.name AS c FROM dbo.t AS t;"),
            ("quiet", "CREATE PROCEDURE dbo.quiet AS DECLARE @x int = 1;"),
            ("p", "CREATE PROCEDURE dbo.p AS SELECT t.id AS a FROM dbo.t AS t; EXEC dbo.q; EXECUTE dbo.quiet; SELECT t.code AS d FROM dbo.t AS t;"));

        var results = Results(doc);
        Assert.Equal(4, results.Count);
        Assert.Equal(["a"], Names(doc, results[0].Type));
        Assert.Equal(["b"], Names(doc, results[1].Type));
        Assert.Equal(["c"], Names(doc, results[2].Type));
        Assert.Equal(["d"], Names(doc, results[3].Type));
        Assert.All(results, r => Assert.False(r.MayBeAbsent));
        Assert.False(Field(doc, "b", 1).Nullable);
        Assert.Equal([new IrNullReason(IrNullRule.NullableColumn, "dbo.t.name")], Field(doc, "c", 2).NullableBecause);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p");
    }

    [Theory]
    [InlineData("EXEC dbo.q;")]
    [InlineData("EXECUTE dbo.q;")]
    [InlineData("EXEC q;")]
    [InlineData("EXEC dbo.q @a = 1;")]
    [InlineData("EXEC dbo.q 1;")]
    [InlineData("DECLARE @rc int; EXEC @rc = dbo.q;")]
    [InlineData("DECLARE @rc int; EXECUTE @rc = dbo.q @a = DEFAULT;")]
    public void Every_form_of_exec_contributes_the_callees_results(string call)
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q @a int = NULL AS SELECT t.id FROM dbo.t AS t;"),
            ("p", $"CREATE PROCEDURE dbo.p AS {call}"));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void A_call_with_result_sets_undefined_contributes_the_callees_results()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q WITH RESULT SETS UNDEFINED;"));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void The_return_code_is_a_non_null_int_after_the_call()
    {
        // Observed: The_return_code_is_never_null.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS RETURN NULL;"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @rc int; EXEC @rc = dbo.q; SELECT @rc AS rc;"));

        Assert.False(Field(doc, "rc").Nullable);
    }

    [Fact]
    public void An_output_argument_may_be_null_after_the_call()
    {
        // Observed: An_output_parameter_the_callee_does_not_set_keeps_the_value_passed_in. The callee may set it to null.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q @v int OUTPUT AS SET @v = NULL;"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @x int = 3; EXEC dbo.q @v = @x OUTPUT; SELECT @x AS x;"));

        var x = Field(doc, "x");
        Assert.True(x.Nullable);
        Assert.Equal("int", Assert.IsType<IrScalar>(x.Type).Sql);
    }

    [Fact]
    public void A_call_in_a_branch_may_not_run()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("r", "CREATE PROCEDURE dbo.r AS SELECT t.name FROM dbo.t AS t; SELECT t.code FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p @a bit AS IF @a = 1 EXEC dbo.q; ELSE EXEC dbo.r;"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        var union = Assert.IsType<IrUnion>(results[0].Type);
        Assert.Equal(["id", "name"], union.Variants.SelectMany(v => Names(doc, v)).Order(StringComparer.Ordinal));
        Assert.False(results[0].MayBeAbsent);
        Assert.Equal(IrNullRule.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
    }

    [Fact]
    public void A_callees_result_on_one_branch_may_not_arrive_and_a_later_one_takes_its_place()
    {
        // Observed in StatementFlowObservationTests (a branch not taken returns no result set at all); a callee's own branch
        // is no different, so the caller's next result may arrive first.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q @b bit AS IF @b = 1 SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p @b bit AS EXEC dbo.q @b; SELECT t.name FROM dbo.t AS t;"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.Equal(["id", "name"], Assert.IsType<IrUnion>(results[0].Type).Variants.SelectMany(v => Names(doc, v)).Order(StringComparer.Ordinal));
        Assert.False(results[0].MayBeAbsent);
        Assert.Equal(IrNullRule.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
    }

    [Fact]
    public void A_callee_that_throws_on_every_path_ends_the_callers_path()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS THROW 50000, N'no', 1;"),
            ("p", "CREATE PROCEDURE dbo.p @a bit AS SELECT t.id FROM dbo.t AS t; IF @a = 1 EXEC dbo.q; SELECT t.name FROM dbo.t AS t;"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.False(results[1].MayBeAbsent);
    }

    [Fact]
    public void An_error_in_a_callee_inside_try_reaches_catch_with_the_results_sent_before_it()
    {
        // The callee's results may stop after any of them, and CATCH's follow.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id AS a FROM dbo.t AS t; SELECT t.id AS b FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS BEGIN TRY EXEC dbo.q; END TRY BEGIN CATCH SELECT t.id AS c FROM dbo.t AS t; END CATCH"));

        var results = Results(doc);
        Assert.Equal(3, results.Count);
        string[] Variants(IrResult result) => result.Type is IrUnion union ? [.. union.Variants.SelectMany(v => Names(doc, v)).Order(StringComparer.Ordinal)] : Names(doc, result.Type);
        Assert.Equal(["a", "c"], Variants(results[0]));
        Assert.Equal(["b", "c"], Variants(results[1]));
        Assert.Equal(["c"], Variants(results[2]));
        Assert.False(results[0].MayBeAbsent);
        Assert.True(results[1].MayBeAbsent);
        Assert.True(results[2].MayBeAbsent);
    }

    [Fact]
    public void A_call_that_returns_results_inside_a_loop_varies()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @i int = 0; WHILE @i < 3 BEGIN EXEC dbo.q; SET @i = 1; END"));

        Assert.Equal(IrUnknownReason.VaryingResults, ResultsUnknown(doc).Reason);
    }

    [Fact]
    public void A_call_that_returns_nothing_inside_a_loop_is_fine()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS DECLARE @x int = 1;"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @i int = 0; WHILE @i < 3 BEGIN EXEC dbo.q; SET @i = 1; END SELECT t.id FROM dbo.t AS t;"));

        Assert.Single(Results(doc));
    }
}

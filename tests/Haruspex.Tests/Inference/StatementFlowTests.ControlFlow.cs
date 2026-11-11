using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class StatementFlowTests
{
    [Fact]
    public void Branches_returning_the_same_shape_give_one_result_that_always_runs()
    {
        var doc = Procedure("IF @x = 1 SELECT t.id FROM dbo.t AS t WHERE t.code = 1;\nELSE SELECT t.id FROM dbo.t AS t;", "@x int");

        var result = Assert.Single(Results(doc));
        Assert.False(result.MayBeAbsent);
        Assert.False(Field(doc, "id").Nullable);
    }

    [Fact]
    public void Branches_returning_different_shapes_give_their_union()
    {
        var doc = Procedure("IF @x = 1 SELECT t.id FROM dbo.t AS t;\nELSE SELECT u.name FROM dbo.u AS u;", "@x int");

        var result = Assert.Single(Results(doc));
        var union = Assert.IsType<IrUnion>(result.Type);
        Assert.All(union.Variants, v => Assert.IsType<IrArray>(v));
        Assert.Equal(2, union.Variants.Count);
    }

    [Fact]
    public void A_result_in_a_branch_may_not_run_and_a_later_result_can_take_its_place()
    {
        // Observed: A_branch_not_taken_returns_no_result_set_at_all_so_later_ones_move_up.
        var doc = Procedure("IF @x = 1 SELECT t.id FROM dbo.t AS t;\nSELECT u.name FROM dbo.u AS u;", "@x int");

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.False(results[0].MayBeAbsent);
        Assert.IsType<IrUnion>(results[0].Type);
        Assert.Equal(IrNullRules.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
        Assert.Equal("name", Assert.Single(Fields(doc, 1)).Name);
    }

    [Fact]
    public void An_early_return_means_later_results_may_not_run()
    {
        var doc = Procedure("SELECT t.id FROM dbo.t AS t;\nIF @x IS NULL RETURN;\nSELECT u.name FROM dbo.u AS u;", "@x int");

        var results = Results(doc);
        Assert.False(results[0].MayBeAbsent);
        Assert.Equal(IrNullRules.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
    }

    [Theory]
    [InlineData("THROW 50000, N'required', 1;")]
    [InlineData("RAISERROR(N'required', 16, 1);")]
    [InlineData("BEGIN RAISERROR(N'required', 11, 1); RETURN; END")]
    public void A_guard_that_raises_an_error_leaves_the_result_always_there(string guard)
    {
        // Observed: the call fails, and the IR describes calls that succeed.
        var doc = Procedure($"IF @x IS NULL {guard}\nSELECT t.id FROM dbo.t AS t;", "@x int");

        Assert.False(Assert.Single(Results(doc)).MayBeAbsent);
    }

    [Fact]
    public void Raiserror_of_low_severity_is_a_message_and_the_path_goes_on()
    {
        var doc = Procedure("IF @x IS NULL BEGIN RAISERROR(N'note', 10, 1); RETURN; END\nSELECT t.id FROM dbo.t AS t;", "@x int");

        Assert.True(Assert.Single(Results(doc)).MayBeAbsent);
    }

    [Fact]
    public void A_procedure_whose_every_path_fails_returns_nothing_it_can_describe()
    {
        var doc = Procedure("THROW 50000, N'never', 1;\nSELECT t.id FROM dbo.t AS t;");

        Assert.Empty(Results(doc));
    }

    [Fact]
    public void Try_with_a_catch_that_rethrows_returns_what_the_try_returns()
    {
        var doc = Procedure("""
            BEGIN TRY
                SELECT t.id FROM dbo.t AS t;
            END TRY
            BEGIN CATCH
                THROW;
            END CATCH
            """);

        Assert.False(Assert.Single(Results(doc)).MayBeAbsent);
    }

    [Fact]
    public void A_catch_that_returns_a_result_can_follow_any_point_of_the_try()
    {
        // Observed: An_error_in_try_goes_to_catch_and_the_results_before_it_still_arrive, and a result cut short still
        // arrives (A_result_cut_short_by_an_error_in_try_still_arrives_with_the_rows_sent_before_it).
        var doc = Procedure("""
            BEGIN TRY
                SELECT t.id FROM dbo.t AS t;
                SELECT u.name FROM dbo.u AS u;
            END TRY
            BEGIN CATCH
                SELECT n.label FROM dbo.node AS n;
            END CATCH
            """);

        var results = Results(doc);
        Assert.Equal(3, results.Count);
        Assert.False(results[0].MayBeAbsent);
        Assert.True(results[1].MayBeAbsent);
        Assert.True(results[2].MayBeAbsent);
        Assert.Equal(["id", "label"], VariantFields(doc, results[0]));
        Assert.Equal(["label", "name"], VariantFields(doc, results[1]));
    }

    private static string[] VariantFields(IrDocument doc, IrResult result) =>
        [.. Assert.IsType<IrUnion>(result.Type).Variants.SelectMany(v => Fields(doc, v)).Select(f => f.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public void An_error_in_a_nested_try_reaches_its_own_catch_and_one_there_the_outer_catch()
    {
        var doc = Procedure("""
            BEGIN TRY
                BEGIN TRY
                    SELECT t.id FROM dbo.t AS t;
                END TRY
                BEGIN CATCH
                    THROW;
                END CATCH
            END TRY
            BEGIN CATCH
                SELECT n.label FROM dbo.node AS n;
            END CATCH
            """);

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.False(results[0].MayBeAbsent);
        Assert.Equal(IrNullRules.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
    }

    [Fact]
    public void A_table_changed_in_one_branch_joins_both_states()
    {
        var doc = Procedure("""
            SELECT t.id, t.code INTO #t FROM dbo.t AS t;
            IF @x = 1 UPDATE x SET code = n.parent_id FROM #t AS x JOIN dbo.node AS n ON n.id = x.id;
            SELECT y.id, y.code FROM #t AS y;
            """, "@x int");

        Assert.True(Field(doc, "code").Nullable);
        Assert.False(Field(doc, "id").Nullable);
    }

    [Fact]
    public void A_loop_that_writes_a_table_reaches_a_fixed_point()
    {
        var doc = Procedure("""
            SELECT n.id, n.label, n.note INTO #t FROM dbo.node AS n WHERE n.parent_id IS NULL;
            WHILE @@ROWCOUNT > 0
            BEGIN
                IF @x = 1 BREAK;
                INSERT INTO #t (id, label, note) SELECT c.id, c.label, c.note FROM dbo.node AS c JOIN #t AS p ON c.parent_id = p.id;
                IF @x = 2 CONTINUE;
            END
            SELECT t.id, t.label, t.note FROM #t AS t;
            """, "@x int");

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "id").Nullable);
        Assert.False(Field(doc, "label").Nullable);
        Assert.True(Field(doc, "note").Nullable);
    }

    [Fact]
    public void A_loop_that_carries_a_null_from_one_pass_to_the_next_is_joined_until_nothing_changes()
    {
        // The first pass writes b's nulls into a; only a second pass shows it.
        var doc = Procedure("""
            SELECT t.id AS a, t.code AS b INTO #t FROM dbo.t AS t;
            WHILE @x > 0
            BEGIN
                UPDATE #t SET a = b;
                UPDATE x SET b = n.parent_id FROM #t AS x JOIN dbo.node AS n ON n.id = x.a;
            END
            SELECT y.a FROM #t AS y;
            """, "@x int");

        Assert.True(Field(doc, "a").Nullable);
    }

    [Fact]
    public void A_result_inside_a_loop_makes_the_results_unknown()
    {
        var doc = Procedure("WHILE @x > 0 BEGIN SELECT t.id FROM dbo.t AS t; SET @x = 0; END", "@x int");

        AssertUnknown(doc, "results", IrUnknownReasons.VaryingResults);
    }

    [Fact]
    public void Results_of_different_encodings_at_one_position_make_the_results_unknown()
    {
        var doc = Procedure("IF @x = 1 SELECT t.id FROM dbo.t AS t;\nELSE SELECT t.id FROM dbo.t AS t FOR JSON PATH;", "@x int");

        AssertUnknown(doc, "results", IrUnknownReasons.VaryingResults);
    }

    [Theory]
    [InlineData("IF @x = 1 WAITFOR DELAY '00:00:01';")]
    [InlineData("WHILE @x > 0 BEGIN WAITFOR DELAY '00:00:01'; END")]
    [InlineData("BEGIN TRY SELECT t.id FROM dbo.t AS t; END TRY BEGIN CATCH WAITFOR DELAY '00:00:01'; END CATCH")]
    [InlineData("GOTO done;\ndone:")]
    public void A_statement_not_modelled_anywhere_makes_the_results_unknown(string body)
    {
        var doc = Procedure(body + "\nSELECT t.id FROM dbo.t AS t;", "@x int");

        AssertUnknown(doc, "results", IrUnknownReasons.UnsupportedStatement);
    }

    [Theory]
    [InlineData("PRINT N'hello';")]
    [InlineData("BEGIN TRANSACTION; COMMIT TRANSACTION;")]
    [InlineData("CREATE TABLE #t (a int); CREATE INDEX ix ON #t (a);")]
    public void A_statement_that_returns_nothing_and_changes_no_state_is_passed_over(string body) =>
        Assert.Single(Results(Procedure(body + "\nSELECT t.id FROM dbo.t AS t;")));
}

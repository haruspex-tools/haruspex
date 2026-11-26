using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The statements that return nothing but change where control goes or how later statements run: <c>GOTO</c> and its labels,
/// <c>SET ROWCOUNT</c>, <c>WAITFOR</c>, <c>EXECUTE AS</c> and <c>REVERT</c>, and <c>SET LANGUAGE</c> with the date settings it
/// sets. Observed in the differential tests' <c>StatementFlowObservationTests.Statements</c>, named where they matter.
/// </summary>
public partial class StatementFlowTests
{
    // ---- GOTO ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_goto_that_skips_a_select_makes_it_a_result_that_may_not_run()
    {
        // Observed: A_goto_jumps_forward_or_back_out_of_blocks_and_into_them_but_not_into_try.
        var doc = Procedure("IF @x = 1 GOTO done;\nSELECT t.id FROM dbo.t AS t;\ndone:\nSELECT u.name FROM dbo.u AS u;", "@x int");

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.IsType<IrUnion>(results[0].Type);
        Assert.Equal(IrNullRule.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
    }

    [Fact]
    public void A_goto_taken_on_every_path_leaves_out_what_it_skips()
    {
        var doc = Procedure("GOTO done;\nSELECT t.id FROM dbo.t AS t;\ndone:\nSELECT u.name FROM dbo.u AS u;");

        var result = Assert.Single(Results(doc));
        Assert.False(result.MayBeAbsent);
        Assert.Equal("name", Assert.Single(Fields(doc)).Name);
    }

    [Fact]
    public void A_label_joins_the_state_each_goto_jumps_with()
    {
        // The jump leaves @n NULL; the path that falls through to the label sets it. A label that took only the path before it
        // would claim @n is never NULL.
        var doc = Procedure("DECLARE @n int = 1;\nIF @x = 1 BEGIN SET @n = NULL; GOTO done; END\nSET @n = 2;\ndone:\nSELECT @n AS n;", "@x int");

        Assert.True(Field(doc, "n").Nullable);
    }

    [Fact]
    public void A_label_no_path_falls_through_to_holds_only_what_jumps_to_it()
    {
        var doc = Procedure("DECLARE @n int;\nSET @n = 1;\nGOTO done;\nSET @n = NULL;\ndone:\nSELECT @n AS n;");

        Assert.False(Field(doc, "n").Nullable);
    }

    [Theory]
    [InlineData("WHILE @x > 0 BEGIN IF @x = 5 GOTO done; SET @x = @x - 1; END\ndone:")]
    [InlineData("BEGIN TRY GOTO done; END TRY BEGIN CATCH GOTO done; END CATCH\ndone:")]
    [InlineData("IF @x = 1 BEGIN BEGIN GOTO Done; END END\ndone:")]
    public void A_goto_forward_out_of_a_block_is_followed(string body)
    {
        // A label is matched ignoring case, as the server matches it.
        var doc = Procedure(body + "\nSELECT t.id FROM dbo.t AS t;", "@x int");

        Assert.Single(Results(doc));
    }

    [Fact]
    public void A_goto_out_of_a_loop_joins_the_state_of_every_pass()
    {
        // The first pass jumps with @n NULL, a later one with @n set: the label holds both.
        var doc = Procedure("DECLARE @n int;\nWHILE @x > 0 BEGIN IF @x = 5 GOTO done; SET @n = 1; SET @x = @x - 1; END\nSET @n = 2;\ndone:\nSELECT @n AS n;", "@x int");

        Assert.True(Field(doc, "n").Nullable);
    }

    [Theory]
    [InlineData("again:\nSET @x = @x - 1;\nIF @x > 0 GOTO again;", "GOTO again jumps back, making a loop, which is not followed")]
    [InlineData("GOTO nowhere;", "GOTO nowhere names 0 labels of the body, not one")]
    [InlineData("GOTO inside;\nIF @x = 1 BEGIN inside: SET @x = 2; END", "GOTO inside jumps into a block it is not in, which is not followed")]
    [InlineData("GOTO inside;\nBEGIN TRY inside: SET @x = 2; END TRY BEGIN CATCH END CATCH", "GOTO inside jumps into a block it is not in, which is not followed")]
    [InlineData("GOTO inside;\nWHILE @x > 0 BEGIN inside: SET @x = @x - 1; END", "GOTO inside jumps into a block it is not in, which is not followed")]
    public void A_goto_back_or_into_a_block_makes_the_results_unknown(string body, string why)
    {
        var doc = Procedure(body + "\nSELECT t.id FROM dbo.t AS t;", "@x int");

        var unknown = Unknown(doc, "results");
        Assert.Equal(IrUnknownReason.UnsupportedStatement, unknown.Reason);
        Assert.StartsWith(why, unknown.Detail, StringComparison.Ordinal);
    }

    // ---- SET ROWCOUNT ------------------------------------------------------------------------------------------

    [Fact]
    public void Set_rowcount_1_leaves_a_query_at_most_one_row()
    {
        // Observed: Set_rowcount_caps_the_rows_of_a_query_and_lasts_until_the_procedure_returns.
        var doc = Procedure("SET ROWCOUNT 1;\nSELECT t.id FROM dbo.t AS t;\nSELECT t.id FROM dbo.t AS t FOR JSON PATH;");

        Assert.All(Results(doc), r => Assert.Equal(IrCardinality.AtMostOne, r.Cardinality));
    }

    [Theory]
    [InlineData("SET ROWCOUNT 2;")]
    [InlineData("SET ROWCOUNT 1;\nSET ROWCOUNT 0;")]
    [InlineData("SET ROWCOUNT @x;")]
    [InlineData("IF @x = 1 SET ROWCOUNT 1;")]
    [InlineData("SET ROWCOUNT 1;\nIF @x = 1 SET ROWCOUNT 0;")]
    [InlineData("SET ROWCOUNT 1;\nBEGIN TRY SET ROWCOUNT 0; SELECT 1 / @x AS z INTO #z; END TRY BEGIN CATCH END CATCH")]
    public void Any_other_row_limit_leaves_a_query_many_rows(string set)
    {
        var doc = Procedure($"{set}\nSELECT t.id FROM dbo.t AS t;", "@x int");

        Assert.Equal(IrCardinality.Many, Results(doc)[^1].Cardinality);
    }

    [Fact]
    public void Set_rowcount_1_leaves_a_query_of_exactly_one_row_one_row()
    {
        var doc = Procedure("SET ROWCOUNT 1;\nSELECT COUNT(*) AS n FROM dbo.t AS t;");

        Assert.Equal(IrCardinality.One, Assert.Single(Results(doc)).Cardinality);
    }

    [Fact]
    public void A_callees_row_limit_ends_when_it_returns_and_the_callers_holds_in_it_unclaimed()
    {
        // The callee's SET ROWCOUNT 0 ends when it returns, so the caller's limit of 1 still holds after the call. The caller's
        // limit caps the callee's queries too, which is not claimed: many rows claims more rows than it may return, never fewer.
        var doc = InferProcedure.Infer(
            "dbo.p",
            ("tables.sql", Tables),
            ("q.sql", "CREATE PROCEDURE dbo.q AS SET ROWCOUNT 0; SELECT u.name FROM dbo.u AS u;"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SET ROWCOUNT 1; EXEC dbo.q; SELECT t.id FROM dbo.t AS t;"));

        var results = Results(doc);
        Assert.Equal(IrCardinality.Many, results[0].Cardinality);
        Assert.Equal(IrCardinality.AtMostOne, results[1].Cardinality);
    }

    // ---- WAITFOR ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("WAITFOR DELAY '00:00:01';")]
    [InlineData("WAITFOR TIME '23:59:59';")]
    [InlineData("DECLARE @d char(8) = '00:00:01'; WAITFOR DELAY @d;")]
    public void Waitfor_delay_or_time_returns_nothing(string wait) =>
        Assert.Single(Results(Procedure(wait + "\nSELECT t.id FROM dbo.t AS t;")));

    [Theory]
    [InlineData("WAITFOR (RECEIVE TOP (1) message_body FROM dbo.q), TIMEOUT 10;")]
    [InlineData("DECLARE @g uniqueidentifier; WAITFOR (GET CONVERSATION GROUP @g FROM dbo.q), TIMEOUT 10;")]
    public void Waitfor_a_service_broker_read_makes_the_results_unknown(string wait)
    {
        // Observed: Waitfor_delay_and_time_return_nothing_and_waitfor_receive_returns_a_result_set.
        var doc = Procedure(wait + "\nSELECT t.id FROM dbo.t AS t;");

        AssertUnknown(doc, "results", IrUnknownReason.UnsupportedStatement);
    }

    // ---- EXECUTE AS … REVERT ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("EXECUTE AS USER = 'clerk';")]
    [InlineData("EXECUTE AS LOGIN = 'clerk';")]
    [InlineData("EXECUTE AS CALLER;")]
    [InlineData("EXECUTE AS USER = 'clerk';\nREVERT;")]
    public void Execute_as_and_revert_return_nothing_and_change_no_type(string impersonate)
    {
        // Observed: Execute_as_changes_permissions_and_masks_but_not_names_and_ends_when_the_procedure_returns.
        var doc = Procedure(impersonate + "\nSELECT t.id, t.status FROM dbo.t AS t;");

        Assert.False(Field(doc, "id").Nullable);
        Assert.NotNull(Scalar(Field(doc, "status")).Facets!.Values);
    }

    [Fact]
    public void Execute_as_with_a_cookie_makes_the_results_unknown()
    {
        var doc = Procedure("DECLARE @c varbinary(100);\nEXECUTE AS USER = 'clerk' WITH COOKIE INTO @c;\nSELECT t.id FROM dbo.t AS t;");

        AssertUnknown(doc, "results", IrUnknownReason.UnsupportedStatement);
    }

    // ---- SET LANGUAGE, DATEFIRST and DATEFORMAT ----------------------------------------------------------------

    [Theory]
    [InlineData("SET LANGUAGE French;")]
    [InlineData("SET LANGUAGE British;")]
    [InlineData("SET LANGUAGE N'us_english';")]
    [InlineData("DECLARE @l sysname = N'Deutsch'; SET LANGUAGE @l;")]
    [InlineData("SET DATEFIRST 1;")]
    [InlineData("SET DATEFORMAT dmy;")]
    public void Set_language_and_the_date_settings_change_no_type_or_claimed_fact(string set)
    {
        // Observed: The_language_changes_values_but_no_type_or_claimed_fact_and_lasts_until_the_procedure_returns. DATENAME
        // gives each language's own names, so no claim on its value would stand; none is made, here or before the SET.
        const string Query = "SELECT DATENAME(month, @d) AS month, DATEPART(weekday, @d) AS day, CAST(@d AS varchar(30)) AS text, CASE WHEN @d IS NULL THEN 'none' ELSE 'some' END AS known, TRY_CAST('2026-01-13' AS datetime) AS parsed;";
        var before = Procedure($"DECLARE @unused int;\n{Query}", "@d date");
        var after = Procedure($"{set}\n{Query}", "@d date");

        Assert.Equal(Results(before), Results(after));
        Assert.Equal(before.Types, after.Types);
        Assert.Null(Scalar(Field(after, "month")).Facets?.Values);

        // A string that reads as a date in one language is NULL under TRY_CAST in another (British reads it day first), so a
        // literal's conversion to a date is never claimed to succeed.
        Assert.True(Field(after, "parsed").Nullable);
    }

    [Theory]
    [InlineData("SET DEADLOCK_PRIORITY LOW;")]
    [InlineData("SET LOCK_TIMEOUT 100;")]
    public void Any_other_set_command_makes_the_results_unknown(string set)
    {
        var doc = Procedure($"{set}\nSELECT t.id FROM dbo.t AS t;");

        AssertUnknown(doc, "results", IrUnknownReason.UnsupportedStatement);
    }
}

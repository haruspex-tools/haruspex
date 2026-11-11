using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// <c>EXEC</c> followed into procedures of the project: a callee's result sets spliced into its caller's, what a
/// callee does to its caller's temporary tables, <c>INSERT … EXEC</c>, recursion between procedures, and the calls that stay
/// unknown. The engine rules each test leans on are observed in the differential tests' <c>ExecObservationTests</c>.
/// </summary>
public class ExecTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL);
        GO
        CREATE TABLE dbo.item (id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, qty int NULL);
        GO
        """;

    /// <summary>Whether a field may be null or is unknown: anything but a claim that it is never null.</summary>
    private static bool ClaimsNothingNonNull(IrField field) => field.Nullable || field.Type is IrUnknownType;

    /// <summary>Infers every procedure of the files, over the standard tables.</summary>
    private static IrDocument Infer(params (string Name, string Sql)[] procedures) => Infer("*.*", procedures);

    /// <summary>Infers the procedures <paramref name="include"/> selects.</summary>
    private static IrDocument Infer(string include, params (string Name, string Sql)[] procedures)
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        foreach (var (name, sql) in procedures)
            dir.File($"{name}.sql", sql);

        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse(include)]).Procedures);
    }

    private static IrObject Object(IrDocument doc, string id) => Assert.Single(doc.Objects, o => o.Id == id);

    private static IReadOnlyList<IrResult> Results(IrDocument doc, string id = "dbo.p") =>
        Object(doc, id).Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Object == id).Select(u => u.Detail)));

    private static IrUnknown ResultsUnknown(IrDocument doc, string id = "dbo.p")
    {
        Assert.Null(Object(doc, id).Results);
        return Assert.Single(doc.Unknowns, u => u.Object == id && u.Path == "results");
    }

    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => type switch
    {
        IrArray { Items: var items } => Fields(doc, items),
        IrRef r => doc.Types[r.Name].Fields,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    private static IrField Field(IrDocument doc, string name, int result = 0, string id = "dbo.p") =>
        Assert.Single(Fields(doc, Results(doc, id)[result].Type), f => f.Name == name);

    private static string[] Names(IrDocument doc, IrType type) => [.. Fields(doc, type).Select(f => f.Name)];

    // ---- result sets through a call --------------------------------------------------------------------

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
        Assert.Equal([new IrNullReason(IrNullRules.NullableColumn, "dbo.t.name")], Field(doc, "c", 2).NullableBecause);
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
        Assert.Equal(IrNullRules.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
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
        Assert.Equal(IrNullRules.NotRun, Assert.Single(results[1].AbsentBecause).Rule);
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

        Assert.Equal(IrUnknownReasons.VaryingResults, ResultsUnknown(doc).Reason);
    }

    [Fact]
    public void A_call_that_returns_nothing_inside_a_loop_is_fine()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS DECLARE @x int = 1;"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @i int = 0; WHILE @i < 3 BEGIN EXEC dbo.q; SET @i = 1; END SELECT t.id FROM dbo.t AS t;"));

        Assert.Single(Results(doc));
    }

    // ---- temporary tables across a call ----------------------------------------------------------------

    [Fact]
    public void A_callee_reads_its_callers_temporary_table()
    {
        // Followed from its caller, a callee's read of #t is of the caller's table; alone, it is the caller-temp-table unknown.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT x.a, x.b FROM #x AS x;"),
            ("p", "CREATE PROCEDURE dbo.p AS CREATE TABLE #x (a int NOT NULL, b nvarchar(5) NULL); EXEC dbo.q;"));

        Assert.False(Field(doc, "a").Nullable);
        Assert.Equal("nvarchar(5)", Assert.IsType<IrScalar>(Field(doc, "b").Type).Sql);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p");
        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.q" && u.Reason == IrUnknownReasons.CallerTempTable);
    }

    [Fact]
    public void A_callees_insert_into_its_callers_table_widens_it()
    {
        // Observed: A_callee_writes_its_callers_temporary_table_and_the_rows_stay.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS INSERT INTO #x (a) VALUES (NULL);"),
            ("p", "CREATE PROCEDURE dbo.p AS SELECT t.id AS a, t.code AS b INTO #x FROM dbo.t AS t; EXEC dbo.q; SELECT x.a, x.b FROM #x AS x;"));

        Assert.True(Field(doc, "a").Nullable);
        Assert.True(Field(doc, "b").Nullable);
    }

    [Fact]
    public void A_callee_that_does_not_write_the_table_leaves_it_as_it_was()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT x.a FROM #x AS x;"),
            ("p", "CREATE PROCEDURE dbo.p AS SELECT t.id AS a INTO #x FROM dbo.t AS t; EXEC dbo.q; SELECT x.a FROM #x AS x;"));

        Assert.False(Field(doc, "a", 1).Nullable);
    }

    [Fact]
    public void A_callees_alter_and_drop_apply_to_its_callers_table()
    {
        // Observed: A_callee_can_alter_and_drop_its_callers_temporary_table.
        var widened = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS ALTER TABLE #x ADD extra nvarchar(3) NULL;"),
            ("p", "CREATE PROCEDURE dbo.p AS CREATE TABLE #x (a int NOT NULL); EXEC dbo.q; SELECT x.a, x.extra FROM #x AS x;"));
        Assert.True(Field(widened, "extra").Nullable);
        Assert.DoesNotContain(widened.Unknowns, u => u.Object == "dbo.p");

        var dropped = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS DROP TABLE #x;"),
            ("p", "CREATE PROCEDURE dbo.p AS CREATE TABLE #x (a int NOT NULL); EXEC dbo.q; SELECT x.a FROM #x AS x;"));
        Assert.Contains(dropped.Unknowns, u => u.Object == "dbo.p" && u.Reason == IrUnknownReasons.UnresolvedTable);
    }

    [Fact]
    public void A_table_the_callee_creates_is_gone_after_the_call()
    {
        // Observed: A_temporary_table_a_callee_creates_is_gone_when_it_returns. A read of it afterwards is of a table the
        // caller's own caller may have.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS CREATE TABLE #made (a int NOT NULL);"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q; SELECT m.a FROM #made AS m;"));

        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.p" && u.Reason == IrUnknownReasons.CallerTempTable);
    }

    [Fact]
    public void A_callee_that_creates_its_own_table_of_the_callers_name_leaves_the_callers_unknown()
    {
        // What it did to the caller's table before creating its own is not followed.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS INSERT INTO #x (a) VALUES (NULL); CREATE TABLE #x (b int);"),
            ("p", "CREATE PROCEDURE dbo.p AS SELECT t.id AS a INTO #x FROM dbo.t AS t; EXEC dbo.q; SELECT x.a FROM #x AS x;"));

        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.p" && u.Path == "results[0][].a");
    }

    // ---- what stays unknown ----------------------------------------------------------------------------

    [Theory]
    [InlineData("DECLARE @sql nvarchar(max) = N'SELECT 1 AS a'; EXEC (@sql);")]
    [InlineData("EXEC (N'SELECT 1 AS a');")]
    [InlineData("EXECUTE (N'SELECT ' + N'1 AS a');")]
    [InlineData("EXEC sp_executesql N'SELECT 1 AS a';")]
    [InlineData("EXEC sys.sp_executesql N'SELECT 1 AS a';")]
    [InlineData("DECLARE @rc int; EXEC @rc = dbo.sp_executesql N'SELECT 1 AS a';")]
    public void Dynamic_sql_makes_the_results_unknown(string call)
    {
        var why = ResultsUnknown(Infer(("p", $"CREATE PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t; {call}")));

        Assert.Equal(IrUnknownReasons.DynamicSql, why.Reason);
        Assert.Contains("dynamic SQL", why.Detail, StringComparison.Ordinal);
        Assert.StartsWith("p.sql:1:", why.At, StringComparison.Ordinal);
    }

    [Fact]
    public void A_procedure_named_in_a_variable_is_unknown()
    {
        var why = ResultsUnknown(Infer(("p", "CREATE PROCEDURE dbo.p AS DECLARE @name sysname = N'dbo.q'; EXEC @name;")));

        Assert.Equal(IrUnknownReasons.ProcedureVariable, why.Reason);
    }

    [Theory]
    [InlineData("EXEC sp_who;")]
    [InlineData("EXEC sys.sp_who;")]
    [InlineData("EXEC dbo.sp_help N'dbo.t';")]
    [InlineData("EXEC master.dbo.xp_fixeddrives;")]
    [InlineData("EXEC xp_fixeddrives;")]
    public void A_system_procedure_is_unknown(string call)
    {
        var why = ResultsUnknown(Infer(("p", $"CREATE PROCEDURE dbo.p AS {call}")));

        Assert.Equal(IrUnknownReasons.SystemProcedure, why.Reason);
    }

    [Fact]
    public void A_procedure_of_the_project_named_like_a_system_procedure_is_unknown()
    {
        // Observed: A_system_procedures_name_runs_the_system_procedure_whatever_the_project_defines. Which system procedures
        // exist is not known here, so any sp_ name may be one.
        var why = ResultsUnknown(Infer(
            ("sp_mine", "CREATE PROCEDURE dbo.sp_mine AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.sp_mine;")));

        Assert.Equal(IrUnknownReasons.SystemProcedure, why.Reason);
        Assert.Contains("dbo.sp_mine", why.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EXEC dbo.nowhere;")]
    [InlineData("EXEC nowhere;")]
    [InlineData("EXEC other_db.dbo.q;")]
    [InlineData("EXEC remote_server.other_db.dbo.q;")]
    [InlineData("EXEC (N'SELECT 1') AT remote_server;")]
    public void A_procedure_not_in_the_project_is_unknown(string call)
    {
        var why = ResultsUnknown(Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", $"CREATE PROCEDURE dbo.p AS {call}")));

        Assert.Equal(IrUnknownReasons.MissingProcedure, why.Reason);
    }

    [Fact]
    public void An_unqualified_name_two_schemas_have_is_ambiguous()
    {
        var why = ResultsUnknown(Infer(
            "dbo.p",
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("q2", "CREATE SCHEMA other;\nGO\nCREATE PROCEDURE other.q AS SELECT t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC q;")));

        Assert.Equal(IrUnknownReasons.AmbiguousProcedure, why.Reason);
    }

    [Fact]
    public void A_clr_procedure_is_unknown()
    {
        var why = ResultsUnknown(Infer(
            ("q", "CREATE PROCEDURE dbo.q AS EXTERNAL NAME Lib.[Lib.Procs].Q;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;")));

        Assert.Equal(IrUnknownReasons.ExternalCode, why.Reason);
    }

    [Fact]
    public void A_callees_reason_reaches_its_caller_naming_the_call()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS DECLARE @sql nvarchar(max) = N'SELECT 1 AS a'; EXEC (@sql);"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;"));

        var why = ResultsUnknown(doc);
        Assert.Equal(IrUnknownReasons.DynamicSql, why.Reason);
        Assert.StartsWith("q.sql:1:", why.At, StringComparison.Ordinal);
        Assert.Contains("dbo.q", why.Detail, StringComparison.Ordinal);
        Assert.Contains("p.sql:1:", why.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_redefined_callee_is_unknown()
    {
        var why = ResultsUnknown(Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;\nGO\nALTER PROCEDURE dbo.q AS SELECT t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;")));

        Assert.Equal(IrUnknownReasons.Redefined, why.Reason);
    }

    // ---- INSERT … EXEC ---------------------------------------------------------------------------------

    [Fact]
    public void Insert_exec_types_the_rows_the_table_receives_and_returns_none()
    {
        // Observed: Insert_exec_inserts_the_rows_of_every_result_set_and_returns_none_to_the_client.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id, t.name FROM dbo.t AS t; SELECT t.code, t.name FROM dbo.t AS t;"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                SELECT t.id AS a, t.name AS b INTO #x FROM dbo.t AS t WHERE 1 = 0;
                INSERT INTO #x EXEC dbo.q;
                SELECT x.a, x.b FROM #x AS x;
                """));

        var result = Assert.Single(Results(doc));
        Assert.Equal(["a", "b"], Names(doc, result.Type));
        Assert.False(Field(doc, "a").Nullable);
        Assert.True(Field(doc, "b").Nullable);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p");
    }

    [Fact]
    public void Insert_exec_leaves_declared_columns_as_declared()
    {
        // Observed: Insert_exec_converts_each_value_to_the_columns_type_or_fails.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.name FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS CREATE TABLE #x (a nvarchar(3) NOT NULL); INSERT INTO #x (a) EXEC dbo.q; SELECT x.a FROM #x AS x;"));

        Assert.False(Field(doc, "a").Nullable);
        Assert.Equal("nvarchar(3)", Assert.IsType<IrScalar>(Field(doc, "a").Type).Sql);
    }

    [Fact]
    public void Insert_exec_never_receives_a_json_result_or_one_of_another_width()
    {
        // Observed: Insert_exec_of_a_procedure_that_runs_for_json_fails and
        // Insert_exec_fails_on_a_result_set_with_the_wrong_number_of_columns_even_with_no_rows. So neither can have put a
        // null into a.
        var doc = Infer(
            ("q", """
                CREATE PROCEDURE dbo.q @a int AS
                SELECT t.id FROM dbo.t AS t;
                IF @a = 1 SELECT t.name FROM dbo.t AS t FOR JSON PATH;
                IF @a = 2 SELECT t.name, t.code FROM dbo.t AS t;
                """),
            ("p", "CREATE PROCEDURE dbo.p AS SELECT t.id AS a INTO #x FROM dbo.t AS t; INSERT INTO #x EXEC dbo.q 1; SELECT x.a FROM #x AS x;"));

        Assert.False(Field(doc, "a").Nullable);
    }

    [Fact]
    public void Under_insert_exec_a_for_json_query_ends_the_path()
    {
        // Observed: Insert_exec_of_a_procedure_that_runs_for_json_fails. So the callee's write after it never happens, and
        // the caller's CATCH sees #x as it was before.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t FOR JSON PATH; INSERT INTO #x (a) VALUES (NULL);"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                SELECT t.id AS a INTO #x FROM dbo.t AS t;
                CREATE TABLE #y (j nvarchar(max));
                BEGIN TRY INSERT INTO #y EXEC dbo.q; END TRY BEGIN CATCH PRINT N'failed'; END CATCH
                SELECT x.a FROM #x AS x;
                """));

        Assert.False(Field(doc, "a").Nullable);
    }

    [Fact]
    public void Insert_exec_without_a_column_list_skips_an_identity_column_select_into_copied()
    {
        // Observed: Select_into_copies_identity_and_insert_exec_without_a_column_list_skips_it. So the callee's one column goes
        // into n, which may then be null.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT i.qty FROM dbo.item AS i;"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                DECLARE @v int = 1;
                SELECT i.id, @v AS n INTO #x FROM dbo.item AS i;
                INSERT INTO #x EXEC dbo.q;
                SELECT x.id, x.n FROM #x AS x;
                """));

        Assert.True(ClaimsNothingNonNull(Field(doc, "n")));
    }

    [Fact]
    public void Insert_exec_without_a_column_list_skips_a_declared_identity_column()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.code FROM dbo.t AS t;"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                CREATE TABLE #x (id int IDENTITY(1, 1) NOT NULL, n int NOT NULL);
                INSERT INTO #x EXEC dbo.q;
                SELECT x.id, x.n FROM #x AS x;
                """));

        Assert.False(Field(doc, "n").Nullable);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p");
    }

    [Fact]
    public void Insert_exec_into_a_table_variable_is_followed()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @x TABLE (a int NOT NULL); INSERT INTO @x EXEC dbo.q; SELECT x.a FROM @x AS x;"));

        Assert.False(Field(doc, "a").Nullable);
    }

    [Fact]
    public void Insert_exec_cannot_be_nested_so_that_path_fails()
    {
        // Observed: Insert_exec_cannot_be_nested. The callee always fails under INSERT … EXEC, so nothing after it runs.
        var doc = Infer(
            ("r", "CREATE PROCEDURE dbo.r AS SELECT t.id FROM dbo.t AS t;"),
            ("q", "CREATE PROCEDURE dbo.q AS CREATE TABLE #m (a int); INSERT INTO #m EXEC dbo.r; SELECT m.a FROM #m AS m;"),
            ("p", "CREATE PROCEDURE dbo.p @a bit AS SELECT t.id FROM dbo.t AS t; CREATE TABLE #x (a int); IF @a = 1 INSERT INTO #x EXEC dbo.q; SELECT t.name FROM dbo.t AS t;"));

        Assert.Equal(2, Results(doc).Count);
        Assert.Single(Results(doc, "dbo.q"));
    }

    [Fact]
    public void Insert_exec_of_dynamic_sql_makes_temporary_tables_unknown_but_not_the_results()
    {
        // Observed: Dynamic_sql_can_change_its_callers_temporary_tables.
        var doc = Infer(("p", """
            CREATE PROCEDURE dbo.p AS
            CREATE TABLE #x (a int NOT NULL);
            INSERT INTO #x EXEC sp_executesql N'SELECT 1';
            SELECT t.id FROM dbo.t AS t;
            SELECT x.a FROM #x AS x;
            """));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.p" && u.Path == "results[1][].a" && u.Reason == IrUnknownReasons.DynamicSql);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p" && u.Path.StartsWith("results[0]", StringComparison.Ordinal));
    }

    // ---- recursion ---------------------------------------------------------------------------------------

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

        Assert.Equal(IrUnknownReasons.UnresolvedRecursion, why.Reason);
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

    // ---- the Zod -------------------------------------------------------------------------------------------

    [Fact]
    public void A_callers_zod_holds_the_callees_results()
    {
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT t.id FROM dbo.t AS t;"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q;"));

        var zod = ZodEmitter.Emit(doc);
        Assert.Contains("export const PRowSchema = z.object({", zod, StringComparison.Ordinal);
        Assert.Contains("export const PSchema = z.array(PRowSchema)", zod, StringComparison.Ordinal);
    }
}

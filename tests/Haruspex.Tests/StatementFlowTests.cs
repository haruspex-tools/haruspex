using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// A procedure's statements walked in order (slice 6): temporary tables, table variables, variables, control flow and
/// CTEs. The engine rules each test leans on are observed in the differential tests'
/// <c>StatementFlowObservationTests</c>, named where they matter.
/// </summary>
public class StatementFlowTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL,
            status varchar(6) NOT NULL CONSTRAINT ck_t_status CHECK (status IN ('open', 'shut')));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, name nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.node (id int NOT NULL PRIMARY KEY, parent_id int NULL, label nvarchar(20) NOT NULL, note nvarchar(20) NULL);
        GO
        CREATE TYPE dbo.code FROM nvarchar(4) NOT NULL;
        GO
        """;

    private static IrDocument Infer(params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);

        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse("*.*")]).Procedures);
    }

    /// <summary>Infers <c>dbo.p</c>, whose body is <paramref name="body"/>, over the standard tables.</summary>
    private static IrDocument Procedure(string body, string parameters = "") =>
        Infer(("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    private static IReadOnlyList<IrResult> Results(IrDocument doc) =>
        doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));

    private static IrUnknown Unknown(IrDocument doc, string path) => Assert.Single(doc.Unknowns, u => u.Path == path);

    private static void AssertUnknown(IrDocument doc, string path, string reason) => Assert.Equal(reason, Unknown(doc, path).Reason);

    private static IReadOnlyList<IrField> Fields(IrDocument doc, int result = 0) => Fields(doc, Results(doc)[result].Type);

    private static IReadOnlyList<IrField> Fields(IrDocument doc, IrType type) => type switch
    {
        IrArray { Items: var items } => Fields(doc, items),
        IrRef r => doc.Types[r.Name].Fields,
        var other => throw new InvalidOperationException($"Unexpected result type {other}."),
    };

    private static IrField Field(IrDocument doc, string name, int result = 0) => Assert.Single(Fields(doc, result), f => f.Name == name);

    private static IrScalar Scalar(IrField field) => Assert.IsType<IrScalar>(field.Type);

    private static string[] Rules(IrField field) => [.. field.NullableBecause.Select(r => r.Rule)];

    // ---- temporary tables ----------------------------------------------------------------------------

    [Fact]
    public void A_temporary_table_is_read_as_it_was_created()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL, b nvarchar(10) NULL, c int IDENTITY(1, 1), d int, e int NOT NULL PRIMARY KEY);
            SELECT t.a, t.b, t.c, t.d, t.e FROM #t AS t;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "a").Nullable);
        Assert.Equal("nvarchar(10)", Scalar(Field(doc, "b")).Sql);
        Assert.Equal(10, Scalar(Field(doc, "b")).Facets!.Length!.Value);
        Assert.Equal([new IrNullReason(IrNullRules.NullableColumn, "#t.b")], Field(doc, "b").NullableBecause);
        Assert.False(Field(doc, "c").Nullable);
        Assert.True(Field(doc, "d").Nullable);
        Assert.False(Field(doc, "e").Nullable);
    }

    [Fact]
    public void A_temporary_tables_check_constraint_narrows_its_values()
    {
        var doc = Procedure("""
            CREATE TABLE #t (s varchar(6) NOT NULL CONSTRAINT ck_s CHECK (s IN ('a', 'b')));
            SELECT t.s FROM #t AS t;
            """);

        var values = Scalar(Field(doc, "s")).Facets!.Values!;
        Assert.Equal(["a", "b"], values.Items.Select(v => v.Text));
        Assert.Equal([new IrFactSource(IrFactSources.Check, "#t.ck_s")], values.From);
    }

    [Fact]
    public void A_disabled_check_on_a_temporary_table_proves_nothing()
    {
        var doc = Procedure("""
            CREATE TABLE #t (s varchar(6) NOT NULL CHECK (s IN ('a', 'b')));
            ALTER TABLE #t NOCHECK CONSTRAINT ALL;
            SELECT t.s FROM #t AS t;
            """);

        Assert.Null(Scalar(Field(doc, "s")).Facets!.Values);
    }

    [Fact]
    public void An_insert_never_makes_a_declared_column_non_null_nor_a_not_null_one_nullable()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL, b nvarchar(10) NULL);
            INSERT INTO #t (a, b) SELECT t.id, u.name FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;
            SELECT t.a, t.b FROM #t AS t;
            """);

        Assert.False(Field(doc, "a").Nullable);
        Assert.True(Field(doc, "b").Nullable);
    }

    [Fact]
    public void Select_into_takes_the_selects_types_nullability_and_facets_with_their_origins()
    {
        var doc = Procedure("""
            SELECT t.id, t.name, t.status INTO #t FROM dbo.t AS t;
            SELECT x.id, x.name, x.status FROM #t AS x;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal([new IrNullReason(IrNullRules.NullableColumn, "dbo.t.name")], Field(doc, "name").NullableBecause);
        var values = Scalar(Field(doc, "status")).Facets!.Values!;
        Assert.Equal(["open", "shut"], values.Items.Select(v => v.Text));
        Assert.Equal([new IrFactSource(IrFactSources.Check, "dbo.ck_t_status")], values.From);
    }

    [Fact]
    public void A_left_join_selected_into_a_temporary_table_stays_nullable()
    {
        var doc = Procedure("""
            SELECT t.id, u.name INTO #t FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.id;
            SELECT x.name FROM #t AS x;
            """);

        Assert.Equal([IrNullRules.OuterJoin], Rules(Field(doc, "name")));
    }

    [Fact]
    public void An_insert_into_a_select_into_column_joins_with_what_it_held()
    {
        // SELECT … INTO decides a column's nullability by SQL Server's own rules, so a later write is joined with what
        // the column held rather than refused (observed: Select_into_a_column_from_a_not_null_column_refuses_null_but_others_take_it).
        var doc = Procedure("""
            SELECT t.id, t.code INTO #t FROM dbo.t AS t;
            INSERT INTO #t (id, code) SELECT u.id, x.code FROM dbo.u AS u LEFT JOIN dbo.t AS x ON x.id = u.t_id;
            SELECT y.id, y.code FROM #t AS y;
            """);

        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal([IrNullRules.OuterJoin], Rules(Field(doc, "code")));
    }

    [Fact]
    public void A_column_left_out_of_an_insert_into_a_select_into_table_becomes_nullable()
    {
        var doc = Procedure("""
            SELECT t.id, t.code INTO #t FROM dbo.t AS t;
            INSERT INTO #t (id) SELECT u.id FROM dbo.u AS u;
            SELECT y.id, y.code FROM #t AS y;
            """);

        Assert.False(Field(doc, "id").Nullable);
        Assert.True(Field(doc, "code").Nullable);
    }

    [Fact]
    public void A_value_written_into_a_select_into_column_is_converted_to_the_columns_type()
    {
        // The column's type is the one SELECT … INTO gave it; a write converts to it (or fails), whatever it writes.
        var doc = Procedure("""
            SELECT t.id INTO #t FROM dbo.t AS t;
            INSERT INTO #t (id) SELECT u.name FROM dbo.u AS u;
            SELECT y.id FROM #t AS y;
            """);

        Assert.Equal("int", Scalar(Field(doc, "id")).Sql);
        Assert.False(Field(doc, "id").Nullable);
    }

    [Fact]
    public void Values_with_other_facets_widen_a_select_into_column_to_what_both_allow()
    {
        var doc = Procedure("""
            SELECT t.status INTO #t FROM dbo.t AS t;
            UPDATE #t SET status = 'x';
            SELECT y.status FROM #t AS y;
            """);

        // The literal is not modelled, so nothing is known of it beyond the column's type, which the write converts to.
        var status = Scalar(Field(doc, "status"));
        Assert.Equal("varchar(6)", status.Sql);
        Assert.Null(status.Facets!.Values);
        Assert.Equal(6, status.Facets.Length!.Value);
        Assert.True(Field(doc, "status").Nullable);
    }

    [Fact]
    public void An_update_joins_the_value_it_writes_into_the_column()
    {
        var doc = Procedure("""
            SELECT t.id, t.code INTO #t FROM dbo.t AS t;
            UPDATE x SET code = n.parent_id FROM #t AS x JOIN dbo.node AS n ON n.id = x.id;
            SELECT y.id, y.code FROM #t AS y;
            """);

        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal([IrNullRules.NullableColumn], Rules(Field(doc, "code")));
        Assert.Equal("dbo.node.parent_id", Field(doc, "code").NullableBecause[0].At);
    }

    [Fact]
    public void Live_json_stored_in_a_temporary_table_is_unknown()
    {
        // Text once stored (observed), unless its SQL type is json, which is not tracked yet.
        var doc = Procedure("""
            SELECT t.id, json_object('n': t.name) AS j INTO #t FROM dbo.t AS t;
            SELECT y.j FROM #t AS y FOR JSON PATH;
            """);

        AssertUnknown(doc, "results[0][].j", IrUnknownReasons.UnsupportedExpression);
    }

    [Fact]
    public void A_column_added_by_alter_table_is_read_as_declared_and_a_dropped_one_is_gone()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL, b int NULL);
            ALTER TABLE #t ADD c int NOT NULL DEFAULT 0, d nvarchar(5) NULL;
            ALTER TABLE #t DROP COLUMN b;
            SELECT t.a, t.c, t.d FROM #t AS t;
            SELECT t.b FROM #t AS t;
            """);

        Assert.False(Field(doc, "c").Nullable);
        Assert.True(Field(doc, "d").Nullable);
        AssertUnknown(doc, "results[1][].b", IrUnknownReasons.UnresolvedColumn);
    }

    [Fact]
    public void A_temporary_table_the_procedure_does_not_create_is_the_callers()
    {
        var doc = Procedure("SELECT t.a FROM #t AS t;");

        AssertUnknown(doc, "results[0][].a", IrUnknownReasons.CallerTempTable);
    }

    [Fact]
    public void A_temporary_table_created_on_some_paths_only_may_be_the_callers()
    {
        var doc = Procedure("""
            IF @x = 1 CREATE TABLE #t (a int NOT NULL);
            SELECT t.a FROM #t AS t;
            """, "@x int");

        AssertUnknown(doc, "results[0][].a", IrUnknownReasons.CallerTempTable);
    }

    [Fact]
    public void A_dropped_temporary_table_is_unknown_where_it_may_be_missing()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL);
            IF @x = 1 DROP TABLE #t;
            SELECT t.a FROM #t AS t;
            """, "@x int");

        AssertUnknown(doc, "results[0][].a", IrUnknownReasons.UnresolvedTable);
    }

    [Fact]
    public void Drop_if_exists_before_create_is_the_usual_preamble_and_types()
    {
        var doc = Procedure("""
            IF OBJECT_ID('tempdb..#t') IS NOT NULL DROP TABLE #t;
            DROP TABLE IF EXISTS #t;
            CREATE TABLE #t (a int NOT NULL);
            SELECT t.a FROM #t AS t;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "a").Nullable);
    }

    [Fact]
    public void A_temporary_table_created_again_after_a_drop_is_read_as_the_new_one()
    {
        // SQL Server refuses such a procedure when it is created (2714, observed), so this never runs; the model still
        // reads the last definition rather than mixing the two.
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL);
            DROP TABLE #t;
            CREATE TABLE #t (a nvarchar(5) NULL);
            SELECT t.a FROM #t AS t;
            """);

        Assert.Equal("nvarchar(5)", Scalar(Field(doc, "a")).Sql);
        Assert.True(Field(doc, "a").Nullable);
    }

    [Fact]
    public void A_cte_over_a_temporary_table_reads_its_current_state()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL, b nvarchar(5) NULL);
            WITH x AS (SELECT t.a, t.b FROM #t AS t) SELECT x.a, x.b FROM x;
            """);

        Assert.False(Field(doc, "a").Nullable);
        Assert.Equal([new IrNullReason(IrNullRules.NullableColumn, "#t.b")], Field(doc, "b").NullableBecause);
    }

    [Fact]
    public void A_temporary_table_on_the_optional_side_of_a_left_join_may_be_null()
    {
        var doc = Procedure("""
            CREATE TABLE #t (id int NOT NULL);
            SELECT u.id, x.id AS t_id FROM dbo.u AS u LEFT JOIN #t AS x ON x.id = u.t_id;
            """);

        Assert.Equal([IrNullRules.OuterJoin], Rules(Field(doc, "t_id")));
    }

    [Fact]
    public void A_column_declared_on_some_paths_only_joins_later_writes()
    {
        // SQL Server makes a SELECT … INTO column read from a variable nullable (observed), so NOT NULL holds only on the
        // path that altered it.
        var doc = Procedure("""
            DECLARE @v int = 5;
            SELECT @v AS v INTO #t;
            IF @x = 1 ALTER TABLE #t ALTER COLUMN v int NOT NULL;
            INSERT INTO #t (v) VALUES (NULL);
            SELECT t.v FROM #t AS t;
            """, "@x int");

        Assert.True(Field(doc, "v").Nullable);
    }

    [Fact]
    public void A_column_declared_differently_on_two_paths_is_not_read_again_from_either_declaration()
    {
        var doc = Procedure("""
            CREATE TABLE #t (v int NULL);
            IF @x = 1 ALTER TABLE #t ALTER COLUMN v int NOT NULL;
            ELSE ALTER TABLE #t ALTER COLUMN v int NULL;
            INSERT INTO #t (v) VALUES (NULL);
            ALTER TABLE #t NOCHECK CONSTRAINT ALL;
            SELECT t.v FROM #t AS t;
            """, "@x int");

        Assert.True(Field(doc, "v").Nullable);
    }

    [Fact]
    public void A_temporary_table_column_of_an_alias_type_is_unknown()
    {
        // tempdb does not have the database's alias types (observed: 2715), so the name does not mean this project's type.
        var doc = Procedure("CREATE TABLE #t (c dbo.code);\nSELECT t.c FROM #t AS t;");

        AssertUnknown(doc, "results[0][].c", IrUnknownReasons.UnsupportedSqlType);
    }

    [Fact]
    public void A_write_to_a_temporary_table_that_is_not_modelled_makes_it_unknown_but_not_the_results()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL);
            MERGE #t AS x USING dbo.t AS s ON x.a = s.id WHEN NOT MATCHED THEN INSERT (a) VALUES (s.id);
            SELECT t.a FROM #t AS t;
            SELECT u.id FROM dbo.u AS u;
            """);

        Assert.Equal(2, Results(doc).Count);
        AssertUnknown(doc, "results[0][].a", IrUnknownReasons.UnsupportedStatement);
    }

    [Fact]
    public void A_rollback_makes_every_temporary_table_unknown()
    {
        // Temporary tables take part in transactions (observed: A_rollback_undoes_a_temporary_tables_creation).
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL);
            BEGIN TRANSACTION;
            ROLLBACK TRANSACTION;
            SELECT t.a FROM #t AS t;
            """);

        AssertUnknown(doc, "results[0][].a", IrUnknownReasons.UnsupportedStatement);
    }

    [Fact]
    public void Deleting_or_truncating_rows_changes_no_column()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL);
            DELETE FROM #t WHERE a = 1;
            TRUNCATE TABLE #t;
            SELECT t.a FROM #t AS t;
            """);

        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void An_output_clause_returns_a_result_set_so_it_is_not_modelled()
    {
        var doc = Procedure("CREATE TABLE #t (a int NOT NULL);\nINSERT INTO #t (a) OUTPUT inserted.a SELECT t.id FROM dbo.t AS t;");

        AssertUnknown(doc, "results", IrUnknownReasons.UnsupportedStatement);
    }

    [Fact]
    public void A_write_to_a_permanent_table_is_not_modelled_yet()
    {
        // A trigger may return result sets of its own.
        var doc = Procedure("INSERT INTO dbo.u (id, t_id, name) SELECT t.id, t.id, N'x' FROM dbo.t AS t;\nSELECT t.id FROM dbo.t AS t;");

        AssertUnknown(doc, "results", IrUnknownReasons.UnsupportedStatement);
    }

    // ---- table variables ---------------------------------------------------------------------------------

    [Fact]
    public void A_table_variable_is_read_as_declared_whatever_is_inserted()
    {
        var doc = Procedure("""
            DECLARE @v TABLE (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, c dbo.code);
            INSERT INTO @v (id, name, c) SELECT u.id, u.name, N'x' FROM dbo.u AS u;
            SELECT v.id, v.name, v.c FROM @v AS v;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal([new IrNullReason(IrNullRules.NullableColumn, "@v.name")], Field(doc, "name").NullableBecause);

        // A table variable takes the database's alias types, NOT NULL included (observed).
        Assert.False(Field(doc, "c").Nullable);
        Assert.Equal(4, Scalar(Field(doc, "c")).Facets!.Length!.Value);
    }

    [Fact]
    public void A_table_variable_read_without_an_alias_binds_by_its_name()
    {
        var doc = Procedure("DECLARE @v TABLE (id int NOT NULL);\nSELECT id FROM @v;");

        Assert.False(Field(doc, "id").Nullable);
    }

    [Fact]
    public void A_table_variable_declared_in_a_branch_exists_after_it()
    {
        // Declarations are compiled, not run (observed).
        var doc = Procedure("IF @x = 1 BEGIN DECLARE @v TABLE (id int NOT NULL); END\nSELECT v.id FROM @v AS v;", "@x int");

        Assert.False(Field(doc, "id").Nullable);
    }

    // ---- variables ---------------------------------------------------------------------------------------

    [Fact]
    public void A_variable_has_its_declared_type_and_is_null_until_assigned()
    {
        var doc = Procedure("DECLARE @n nvarchar(5);\nSELECT @n AS n;");

        Assert.Equal("nvarchar(5)", Scalar(Field(doc, "n")).Sql);
        Assert.Equal([IrNullRules.Variable], Rules(Field(doc, "n")));
    }

    [Fact]
    public void A_variable_assigned_a_value_that_is_never_null_is_never_null()
    {
        var doc = Procedure("DECLARE @n int = 0;\nDECLARE @m int;\nSET @m = 1;\nSELECT @n AS n, @m AS m;");

        Assert.False(Field(doc, "n").Nullable);
        Assert.False(Field(doc, "m").Nullable);
    }

    [Fact]
    public void A_select_that_may_assign_nothing_leaves_a_variable_as_it_was()
    {
        var doc = Procedure("DECLARE @n int;\nSELECT @n = t.code FROM dbo.t AS t;\nSELECT @n AS n;");

        Assert.Equal([IrNullRules.Variable], Rules(Field(doc, "n")));
    }

    [Fact]
    public void A_variable_assigned_on_some_paths_only_may_be_null()
    {
        var doc = Procedure("DECLARE @n int;\nIF @x = 1 SET @n = 1;\nSELECT @n AS n;", "@x int");

        Assert.True(Field(doc, "n").Nullable);
    }

    [Fact]
    public void A_parameter_read_in_a_procedure_has_its_type_and_may_be_null()
    {
        var doc = Procedure("SELECT @x AS x;", "@x tinyint");

        Assert.Equal("tinyint", Scalar(Field(doc, "x")).Sql);
        Assert.Equal([IrNullRules.Parameter], Rules(Field(doc, "x")));
    }

    // ---- expressions the flow needs ------------------------------------------------------------------------

    [Fact]
    public void An_integer_literal_is_a_non_null_int()
    {
        var doc = Procedure("SELECT 0 AS zero, 2147483647 AS biggest;");

        Assert.Equal("int", Scalar(Field(doc, "zero")).Sql);
        Assert.False(Field(doc, "zero").Nullable);
        Assert.Equal("int", Scalar(Field(doc, "biggest")).Sql);
    }

    [Fact]
    public void A_larger_integer_literal_is_not_an_int_and_is_not_modelled()
    {
        AssertUnknown(Procedure("SELECT 2147483648 AS big;"), "results[0][].big", IrUnknownReasons.UnsupportedExpression);
    }

    [Fact]
    public void A_cast_of_a_string_to_a_string_type_has_that_type_and_the_operands_nullability()
    {
        var doc = Procedure("SELECT CAST(t.name AS nvarchar(200)) AS a, CAST(u.name AS varchar(3)) AS b FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;");

        Assert.Equal(200, Scalar(Field(doc, "a")).Facets!.Length!.Value);
        Assert.True(Field(doc, "a").Nullable);
        Assert.Equal("varchar(3)", Scalar(Field(doc, "b")).Sql);
        Assert.False(Field(doc, "b").Nullable);
    }

    [Theory]
    [InlineData("CAST(t.name AS varchar)", "varchar(30)")]
    [InlineData("CONVERT(nvarchar, t.name)", "nvarchar(30)")]
    [InlineData("CAST(t.name AS char)", "char(30)")]
    public void A_cast_to_a_string_type_without_a_length_is_30_long(string expression, string sql)
    {
        // Observed: A_string_type_without_a_length_is_30_long_in_a_cast_and_1_in_a_declaration.
        var scalar = Scalar(Field(Procedure($"SELECT {expression} AS a FROM dbo.t AS t;"), "a"));

        Assert.Equal(sql, scalar.Sql);
        Assert.Equal(30, scalar.Facets!.Length!.Value);
    }

    [Fact]
    public void A_cast_of_anything_else_is_not_modelled_yet()
    {
        AssertUnknown(Procedure("SELECT CAST(t.id AS nvarchar(5)) AS a FROM dbo.t AS t;"), "results[0][].a", IrUnknownReasons.UnsupportedExpression);
    }

    // ---- control flow --------------------------------------------------------------------------------------

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
    [InlineData("IF @x = 1 EXEC dbo.other;")]
    [InlineData("WHILE @x > 0 BEGIN EXEC dbo.other; END")]
    [InlineData("BEGIN TRY SELECT t.id FROM dbo.t AS t; END TRY BEGIN CATCH EXEC dbo.other; END CATCH")]
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

    // ---- CTEs ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_cte_is_read_like_the_query_it_names()
    {
        var doc = Procedure("WITH x AS (SELECT t.id, t.name, t.status FROM dbo.t AS t) SELECT x.id, x.name, x.status FROM x;");

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal("dbo.t.name", Assert.Single(Field(doc, "name").NullableBecause).At);
        Assert.NotNull(Scalar(Field(doc, "status")).Facets!.Values);
    }

    [Fact]
    public void A_ctes_column_list_renames_its_columns()
    {
        var doc = Procedure("WITH x (k, v) AS (SELECT t.id, t.name FROM dbo.t AS t) SELECT x.k, v FROM x;");

        Assert.False(Field(doc, "k").Nullable);
        Assert.True(Field(doc, "v").Nullable);
    }

    [Fact]
    public void A_cte_may_read_an_earlier_one_in_the_same_with()
    {
        var doc = Procedure("""
            WITH a AS (SELECT t.id, t.name FROM dbo.t AS t),
                 b AS (SELECT a.id, u.name FROM a JOIN dbo.u AS u ON u.t_id = a.id)
            SELECT b.id, b.name FROM b;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "name").Nullable);
    }

    [Fact]
    public void A_cte_hides_a_table_of_the_same_name_unless_the_schema_is_written()
    {
        var doc = Procedure("WITH u AS (SELECT t.name FROM dbo.t AS t) SELECT u.name FROM u;\nWITH u AS (SELECT t.name FROM dbo.t AS t) SELECT x.name FROM dbo.u AS x;");

        Assert.True(Field(doc, "name", 0).Nullable);
        Assert.False(Field(doc, "name", 1).Nullable);
    }

    [Fact]
    public void A_cte_keeps_json_live()
    {
        // Observed: Json_stored_in_a_temporary_table_is_text_but_json_through_a_cte_stays_json.
        var doc = Procedure("WITH c AS (SELECT t.id, json_object('n': t.name) AS j FROM dbo.t AS t) SELECT c.id, c.j FROM c FOR JSON PATH;");

        Assert.IsType<IrRef>(Field(doc, "j").Type);
    }

    [Fact]
    public void A_cte_can_feed_select_into_and_insert()
    {
        var doc = Procedure("""
            WITH a AS (SELECT t.id FROM dbo.t AS t) SELECT a.id INTO #t FROM a;
            WITH b AS (SELECT u.id FROM dbo.u AS u) INSERT INTO #t (id) SELECT b.id FROM b;
            SELECT x.id FROM #t AS x;
            """);

        Assert.False(Field(doc, "id").Nullable);
    }

    [Fact]
    public void A_cte_whose_query_is_a_union_is_not_modelled_yet()
    {
        var doc = Procedure("WITH x AS (SELECT t.id FROM dbo.t AS t UNION SELECT u.id FROM dbo.u AS u) SELECT x.id FROM x;");

        AssertUnknown(doc, "results[0][].id", IrUnknownReasons.UnsupportedQuery);
    }

    [Fact]
    public void A_recursive_cte_types_its_columns_from_the_anchor_and_joins_nullability_to_a_fixed_point()
    {
        // b is never null in the anchor, but the recursive member writes note into it, which can be; a is fed b by the
        // recursive member, so it can be null only from the second pass.
        var doc = Procedure("""
            WITH w (id, a, b, depth) AS (
                SELECT n.id, n.label, n.label, 0 FROM dbo.node AS n WHERE n.parent_id IS NULL
                UNION ALL
                SELECT c.id, w.b, c.note, w.depth + 1 FROM dbo.node AS c JOIN w ON c.parent_id = w.id)
            SELECT w.id, w.a, w.b, w.depth FROM w;
            """);

        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal("nvarchar(20)", Scalar(Field(doc, "a")).Sql);
        Assert.True(Field(doc, "a").Nullable);
        Assert.True(Field(doc, "b").Nullable);

        // The recursive member's depth + 1 is not modelled, but a recursive column has its anchor's type exactly
        // (observed: A_recursive_member_of_another_type_than_the_anchor_is_an_error), so only its nullability is unknown.
        Assert.Equal("int", Scalar(Field(doc, "depth")).Sql);
        Assert.Equal([IrNullRules.Unknown], Rules(Field(doc, "depth")));
    }

    [Fact]
    public void A_hierarchy_walked_by_a_recursive_cte_types_where_every_member_reads_columns()
    {
        var doc = Procedure("""
            WITH tree AS (
                SELECT n.id, n.parent_id, n.label FROM dbo.node AS n WHERE n.parent_id IS NULL
                UNION ALL
                SELECT c.id, c.parent_id, c.label FROM dbo.node AS c JOIN tree AS p ON c.parent_id = p.id)
            SELECT tree.id, tree.parent_id, tree.label FROM tree FOR JSON PATH;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "label").Nullable);
        Assert.Equal(20, Scalar(Field(doc, "label")).Facets!.Length!.Value);
        Assert.True(Field(doc, "parent_id").Nullable);
    }

    // ---- the IR and Zod -----------------------------------------------------------------------------------

    [Fact]
    public void A_single_object_result_that_may_not_run_is_nullable_in_zod()
    {
        var ts = ZodEmitter.Emit(Procedure("IF @x IS NULL RETURN;\nSELECT t.id FROM dbo.t AS t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;", "@x int"));

        Assert.Contains("export const PSchema = PRowSchema.nullable()\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_union_puts_a_variant_with_more_keys_first_and_each_rejects_the_others_keys()
    {
        // z.union takes the first variant that accepts a row, and z.object strips keys it does not know.
        var doc = Procedure("IF @x = 1 SELECT u.id FROM dbo.u AS u;\nELSE SELECT t.id, t.code FROM dbo.t AS t;", "@x int");

        Assert.Contains("export const PSchema = z.union([z.array(PRow2Schema), z.array(PRowSchema.extend({ code: z.never().optional() }))])\n", ZodEmitter.Emit(doc), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_json_variant_rejects_the_keys_only_the_others_have_so_none_drops_them()
    {
        // A key left out when null is optional, so either variant would accept the other's objects without this.
        var doc = Procedure("IF @x = 1 SELECT t.id, t.name FROM dbo.t AS t FOR JSON PATH;\nELSE SELECT u.id, u.t_id FROM dbo.u AS u FOR JSON PATH;", "@x int");

        Assert.Contains(
            "z.union([z.array(PRowSchema.extend({ t_id: z.never().optional() })), z.array(PRow2Schema.extend({ name: z.never().optional() }))]).nullable()",
            ZodEmitter.Emit(doc),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_rowset_that_may_not_run_reads_as_no_rows()
    {
        var doc = Procedure("IF @x IS NULL RETURN;\nSELECT t.id FROM dbo.t AS t;", "@x int");

        Assert.Contains("export const PSchema = z.array(PRowSchema)\n", ZodEmitter.Emit(doc), StringComparison.Ordinal);
        Assert.Contains("[] when it does not run", ZodEmitter.Emit(doc), StringComparison.Ordinal);
    }
}

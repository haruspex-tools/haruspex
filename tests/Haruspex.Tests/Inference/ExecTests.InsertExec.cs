using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class ExecTests
{
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
        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.p" && u.Path == "results[1][].a" && u.Reason == IrUnknownReason.DynamicSql);
        Assert.DoesNotContain(doc.Unknowns, u => u.Object == "dbo.p" && u.Path.StartsWith("results[0]", StringComparison.Ordinal));
    }
}

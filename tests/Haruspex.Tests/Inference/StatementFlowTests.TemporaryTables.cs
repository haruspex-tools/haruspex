using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class StatementFlowTests
{
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
        Assert.Equal([new IrNullReason(IrNullRule.NullableColumn, "#t.b")], Field(doc, "b").NullableBecause);
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
        Assert.Equal([new IrFactSource(IrFactSourceKind.Check, "#t.ck_s")], values.From);
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
        Assert.Equal([new IrNullReason(IrNullRule.NullableColumn, "dbo.t.name")], Field(doc, "name").NullableBecause);
        var values = Scalar(Field(doc, "status")).Facets!.Values!;
        Assert.Equal(["open", "shut"], values.Items.Select(v => v.Text));
        Assert.Equal([new IrFactSource(IrFactSourceKind.Check, "dbo.ck_t_status")], values.From);
    }

    [Fact]
    public void A_left_join_selected_into_a_temporary_table_stays_nullable()
    {
        var doc = Procedure("""
            SELECT t.id, u.name INTO #t FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.id;
            SELECT x.name FROM #t AS x;
            """);

        Assert.Equal([IrNullRule.OuterJoin], Rules(Field(doc, "name")));
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
        Assert.Equal([IrNullRule.OuterJoin], Rules(Field(doc, "code")));
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

        // The literal is a varchar(1) never null, outside the CHECK set: the column keeps its type, which the write converts
        // to, and loses the set.
        var status = Scalar(Field(doc, "status"));
        Assert.Equal("varchar(6)", status.Sql);
        Assert.Null(status.Facets!.Values);
        Assert.Equal(6, status.Facets.Length!.Value);
        Assert.False(Field(doc, "status").Nullable);
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
        Assert.Equal([IrNullRule.NullableColumn], Rules(Field(doc, "code")));
        Assert.Equal("dbo.node.parent_id", Field(doc, "code").NullableBecause[0].At);
    }

    [Fact]
    public void Live_json_stored_in_a_temporary_table_is_text_of_its_content_unless_it_is_of_type_json()
    {
        // Text once stored (observed): SELECT … INTO makes nvarchar(max) of a constructor and json of one RETURNING json, which
        // is not tracked yet (Select_into_makes_nvarchar_max_of_built_json_and_json_of_returning_json).
        var doc = Procedure("""
            SELECT t.id, json_object('n': t.name) AS j, json_object('n': t.name RETURNING json) AS k INTO #t FROM dbo.t AS t;
            SELECT y.j, json_query(y.j) AS q, y.k FROM #t AS y FOR JSON PATH;
            """);

        Assert.IsType<IrJsonText>(Field(doc, "j").Type);
        Assert.IsType<IrRef>(Field(doc, "q").Type);
        AssertUnknown(doc, "results[0][].k", IrUnknownReason.UnsupportedExpression);
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
        AssertUnknown(doc, "results[1][].b", IrUnknownReason.UnresolvedColumn);
    }

    [Fact]
    public void A_temporary_table_the_procedure_does_not_create_is_the_callers()
    {
        var doc = Procedure("SELECT t.a FROM #t AS t;");

        AssertUnknown(doc, "results[0][].a", IrUnknownReason.CallerTempTable);
    }

    [Fact]
    public void A_temporary_table_created_on_some_paths_only_may_be_the_callers()
    {
        var doc = Procedure("""
            IF @x = 1 CREATE TABLE #t (a int NOT NULL);
            SELECT t.a FROM #t AS t;
            """, "@x int");

        AssertUnknown(doc, "results[0][].a", IrUnknownReason.CallerTempTable);
    }

    [Fact]
    public void A_dropped_temporary_table_is_unknown_where_it_may_be_missing()
    {
        var doc = Procedure("""
            CREATE TABLE #t (a int NOT NULL);
            IF @x = 1 DROP TABLE #t;
            SELECT t.a FROM #t AS t;
            """, "@x int");

        AssertUnknown(doc, "results[0][].a", IrUnknownReason.UnresolvedTable);
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
        Assert.Equal([new IrNullReason(IrNullRule.NullableColumn, "#t.b")], Field(doc, "b").NullableBecause);
    }

    [Fact]
    public void A_temporary_table_on_the_optional_side_of_a_left_join_may_be_null()
    {
        var doc = Procedure("""
            CREATE TABLE #t (id int NOT NULL);
            SELECT u.id, x.id AS t_id FROM dbo.u AS u LEFT JOIN #t AS x ON x.id = u.t_id;
            """);

        Assert.Equal([IrNullRule.OuterJoin], Rules(Field(doc, "t_id")));
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

        AssertUnknown(doc, "results[0][].c", IrUnknownReason.UnsupportedSqlType);
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
        AssertUnknown(doc, "results[0][].a", IrUnknownReason.UnsupportedStatement);
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

        AssertUnknown(doc, "results[0][].a", IrUnknownReason.UnsupportedStatement);
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
    public void An_output_clause_returns_a_result_set_of_the_rows_written()
    {
        // OUTPUT without INTO is the rows written, typed from inserted (DmlTests has the rest).
        var doc = Procedure("CREATE TABLE #t (a int NOT NULL);\nINSERT INTO #t (a) OUTPUT inserted.a SELECT t.id FROM dbo.t AS t;");

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "a").Nullable);
    }

    [Fact]
    public void A_write_to_a_permanent_table_returns_no_result_set()
    {
        // No trigger in the project returns results, so the write returns none (DmlTests has the rest).
        var doc = Procedure("INSERT INTO dbo.u (id, t_id, name) SELECT t.id, t.id, N'x' FROM dbo.t AS t;\nSELECT t.id FROM dbo.t AS t;");

        Assert.Empty(doc.Unknowns);
        Assert.Single(Assert.IsType<EquatableArray<IrResult>>(doc.Objects[0].Results));
    }
}

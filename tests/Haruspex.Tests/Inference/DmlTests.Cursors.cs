using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class DmlTests
{
    [Fact]
    public void Cursor_statements_and_fetch_into_return_nothing()
    {
        // Observed: Cursor_statements_and_fetch_into_return_nothing_and_fetch_without_into_returns_one_row.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @id int = 0, @label nvarchar(10);
            DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT i.id, i.label FROM dbo.item AS i WHERE i.label IS NOT NULL;
            OPEN c;
            FETCH NEXT FROM c INTO @id, @label;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                UPDATE dbo.item SET qty = qty + 1 WHERE id = @id;
                FETCH NEXT FROM c INTO @id, @label;
            END
            CLOSE c;
            DEALLOCATE c;
            SELECT @id AS id, @label AS label;
            """));

        // FETCH INTO may not assign (observed: Fetch_into_leaves_the_variables_as_they_were_past_the_end), and a cursor's
        // WHERE proves nothing of the row fetched (observed: A_keyset_cursor_fetches_a_row_changed_after_open_that_its_where_no_longer_keeps).
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal([IrNullRules.Variable, IrNullRules.NullableColumn], Rules(Field(doc, "label")));
    }

    [Fact]
    public void Fetch_without_into_makes_the_results_unknown()
    {
        // Observed: the row carries a hidden ROWSTAT column, which some drivers list and others leave out.
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS DECLARE c CURSOR LOCAL FOR SELECT i.id FROM dbo.item AS i; OPEN c; FETCH NEXT FROM c; CLOSE c; DEALLOCATE c;"));

        Assert.Equal(IrUnknownReasons.HiddenColumn, ResultsUnknown(doc).Reason);
    }

    [Fact]
    public void Fetch_from_a_cursor_this_procedure_does_not_declare_once_assigns_unknown_values()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @id int = 0, @c CURSOR;
            SET @c = CURSOR LOCAL FOR SELECT i.id FROM dbo.item AS i;
            OPEN @c; FETCH NEXT FROM @c INTO @id; CLOSE @c; DEALLOCATE @c;
            FETCH NEXT FROM GLOBAL other INTO @id;
            SELECT @id AS id;
            """));

        // A variable keeps its declared type, which FETCH converts to; only whether it is null is not known.
        Assert.Contains(IrNullRules.Unknown, Rules(Field(doc, "id")));
    }

    [Fact]
    public void A_cursor_over_a_temporary_table_altered_after_it_is_declared_reads_unknown_values()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @one int = 1, @n int = 0;
            SELECT @one AS n INTO #t;
            DECLARE c CURSOR LOCAL STATIC FOR SELECT t.n FROM #t AS t;
            OPEN c;
            ALTER TABLE #t ALTER COLUMN n int NOT NULL;
            FETCH NEXT FROM c INTO @n;
            SELECT @n AS n;
            """));

        Assert.Contains(IrNullRules.Unknown, Rules(Field(doc, "n")));
    }

    [Fact]
    public void A_variable_in_a_cursors_query_is_any_value_of_its_type()
    {
        // Observed: A_cursors_query_takes_its_variables_as_they_are_when_it_is_declared.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @x int, @v int = 0;
            DECLARE c CURSOR LOCAL STATIC FOR SELECT @x AS v;
            SET @x = 5;
            OPEN c; FETCH NEXT FROM c INTO @v; CLOSE c; DEALLOCATE c;
            SELECT @v AS v;
            """));

        Assert.True(Field(doc, "v").Nullable);
    }

    [Fact]
    public void A_cursor_over_a_temporary_table_reads_it_when_fetched()
    {
        // Observed: A_dynamic_cursor_fetches_a_temporary_tables_rows_as_they_are_when_fetched.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @one int = 1, @n int = 0;
            SELECT @one AS n INTO #t;
            DECLARE c CURSOR LOCAL DYNAMIC FOR SELECT t.n FROM #t AS t;
            OPEN c;
            INSERT INTO #t (n) VALUES (NULL);
            FETCH NEXT FROM c INTO @n;
            CLOSE c; DEALLOCATE c;
            SELECT @n AS n;
            """));

        Assert.True(Field(doc, "n").Nullable);
    }
}

using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class ExecTests
{
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
        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.q" && u.Reason == IrUnknownReason.CallerTempTable);
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
        Assert.Contains(dropped.Unknowns, u => u.Object == "dbo.p" && u.Reason == IrUnknownReason.UnresolvedTable);
    }

    [Fact]
    public void A_table_the_callee_creates_is_gone_after_the_call()
    {
        // Observed: A_temporary_table_a_callee_creates_is_gone_when_it_returns. A read of it afterwards is of a table the
        // caller's own caller may have.
        var doc = Infer(
            ("q", "CREATE PROCEDURE dbo.q AS CREATE TABLE #made (a int NOT NULL);"),
            ("p", "CREATE PROCEDURE dbo.p AS EXEC dbo.q; SELECT m.a FROM #made AS m;"));

        Assert.Contains(doc.Unknowns, u => u.Object == "dbo.p" && u.Reason == IrUnknownReason.CallerTempTable);
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
}

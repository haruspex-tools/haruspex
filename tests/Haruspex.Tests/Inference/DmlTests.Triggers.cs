using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class DmlTests
{
    [Fact]
    public void A_trigger_that_returns_a_result_set_makes_the_results_unknown()
    {
        // Observed: A_triggers_select_reaches_the_client_even_when_no_row_is_affected.
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.item_loud ON dbo.item AFTER UPDATE AS SELECT COUNT_BIG(*) AS changed FROM inserted;"),
            ("p", "CREATE PROCEDURE dbo.p AS UPDATE dbo.item SET qty = 1; SELECT 1 AS a;"));

        var unknown = ResultsUnknown(doc);
        Assert.Equal(IrUnknownReason.TriggerResults, unknown.Reason);
        Assert.Contains("dbo.item_loud", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_insert_that_nests_a_write_ends_what_can_be_listed()
    {
        // Observed: A_write_nested_in_an_insert_fires_its_tables_triggers. The walker reads only the outer write, so the
        // nested one's triggers would be missed.
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.item_gone ON dbo.item AFTER DELETE AS SELECT N'fired' AS what;"),
            ("p", "CREATE PROCEDURE dbo.p AS DECLARE @t TABLE (id int); INSERT INTO @t (id) SELECT d.id FROM (DELETE FROM dbo.item OUTPUT deleted.id) AS d; SELECT 1 AS a;"));

        var unknown = ResultsUnknown(doc);
        Assert.Equal(IrUnknownReason.UnsupportedStatement, unknown.Reason);
        Assert.Contains("nested", unknown.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sales", false)]
    [InlineData("dbo", true)]
    // Observed: A_name_in_a_trigger_is_looked_for_in_its_tables_schema. The trigger on sales.shelf writes note without a
    // schema: sales.note, whatever the caller's default schema, so only sales.note's trigger is fired (dbo.p's own rule would
    // take dbo.note).
    public void A_name_written_in_a_trigger_is_its_tables_schemas(string loud, bool known)
    {
        var doc = Doc(
            ("tables", """
                CREATE TABLE sales.shelf (id int NOT NULL);
                GO
                CREATE TABLE sales.note (text nvarchar(50) NULL);
                GO
                CREATE TABLE dbo.note (text nvarchar(50) NULL);
                GO
                """),
            ("trigger", "CREATE TRIGGER sales.shelf_noted ON sales.shelf AFTER UPDATE AS INSERT INTO note (text) VALUES (N'changed');"),
            ("loud", $"CREATE TRIGGER {loud}.note_loud ON {loud}.note AFTER INSERT AS SELECT N'noted' AS what;"),
            ("p", "CREATE PROCEDURE dbo.p AS UPDATE sales.shelf SET id = 1; SELECT 1 AS a;"));

        if (known)
            Assert.Single(Results(doc));
        else
            Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(doc).Reason);
    }

    [Fact]
    public void A_trigger_that_returns_nothing_adds_nothing()
    {
        var doc = Doc(
            ("trigger", """
                CREATE TRIGGER dbo.item_audit ON dbo.item AFTER INSERT, UPDATE, DELETE AS
                SET NOCOUNT ON;
                DECLARE @n int;
                SELECT @n = COUNT(*) FROM inserted;
                IF @n > 0 INSERT INTO dbo.note (text) SELECT i.label FROM inserted AS i;
                """),
            ("p", "CREATE PROCEDURE dbo.p AS UPDATE dbo.item SET qty = 1; SELECT 1 AS a;"));

        Assert.Single(Results(doc));
    }

    [Theory]
    [InlineData("AFTER INSERT", "UPDATE dbo.item SET qty = 1;", true)]
    [InlineData("AFTER UPDATE", "UPDATE dbo.item SET qty = 1;", false)]
    [InlineData("AFTER DELETE", "TRUNCATE TABLE dbo.item;", true)]
    [InlineData("AFTER DELETE", "DELETE FROM dbo.item;", false)]
    [InlineData("INSTEAD OF INSERT", "INSERT INTO dbo.item (id) VALUES (1);", false)]
    [InlineData("AFTER UPDATE", "MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id);", true)]
    [InlineData("AFTER INSERT", "MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id);", false)]
    [InlineData("AFTER DELETE", "MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id WHEN NOT MATCHED BY SOURCE THEN DELETE;", false)]
    [InlineData("AFTER UPDATE", "WITH c AS (SELECT i.id, i.label FROM dbo.item AS i) UPDATE c SET label = N'x';", false)]
    public void Only_a_trigger_for_the_statement_counts(string kind, string dml, bool typed)
    {
        // Observed: Truncate_does_not_fire_a_delete_trigger, Merge_fires_the_trigger_of_each_action_it_has,
        // Dml_through_a_view_or_a_cte_fires_the_triggers_of_the_table_underneath.
        var doc = Doc(
            ("trigger", $"CREATE TRIGGER dbo.item_loud ON dbo.item {kind} AS SELECT N'fired' AS what;"),
            ("p", $"CREATE PROCEDURE dbo.p AS {dml} SELECT 1 AS a;"));

        if (typed)
            Assert.Single(Results(doc));
        else
            Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(doc).Reason);
    }

    [Fact]
    public void Dml_through_a_view_reaches_its_instead_of_trigger_or_the_triggers_of_the_tables_it_reads()
    {
        // Observed: An_instead_of_trigger_on_a_view_returns_its_result_set,
        // Dml_through_a_view_or_a_cte_fires_the_triggers_of_the_table_underneath.
        const string View = "CREATE VIEW dbo.item_view AS SELECT i.id, i.label FROM dbo.item AS i;";
        var instead = Doc(
            ("view", View),
            ("trigger", "CREATE TRIGGER dbo.item_view_insert ON dbo.item_view INSTEAD OF INSERT AS SELECT N'instead' AS what;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item_view (id) VALUES (1); SELECT 1 AS a;"));
        var underneath = Doc(
            ("view", View),
            ("trigger", "CREATE TRIGGER dbo.item_loud ON dbo.item AFTER UPDATE AS SELECT N'fired' AS what;"),
            ("p", "CREATE PROCEDURE dbo.p AS UPDATE dbo.item_view SET label = N'v'; SELECT 1 AS a;"));
        var quiet = Doc(
            ("view", View),
            ("trigger", "CREATE TRIGGER dbo.item_loud ON dbo.item AFTER UPDATE AS SELECT N'fired' AS what;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item_view (id) VALUES (1); SELECT 1 AS a;"));

        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(instead).Reason);
        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(underneath).Reason);
        Assert.Single(Results(quiet));
    }

    [Fact]
    public void A_write_through_a_view_defined_more_than_once_is_not_followed_to_the_tables_underneath()
    {
        // Which of the two definitions is in effect depends on the order the scripts run, so neither is read for the tables a
        // write through it reaches; the table here has no trigger, and the write is unknown all the same.
        var doc = Doc(
            ("view", "CREATE VIEW dbo.item_view AS SELECT i.id, i.label FROM dbo.item AS i;"),
            ("view_again", "ALTER VIEW dbo.item_view AS SELECT i.id, i.label FROM dbo.item AS i WHERE i.id > 0;"),
            ("p", "CREATE PROCEDURE dbo.p AS UPDATE dbo.item_view SET label = N'v'; SELECT 1 AS a;"));

        var unknown = ResultsUnknown(doc);
        Assert.Equal(IrUnknownReason.TriggerResults, unknown.Reason);
        Assert.Contains("writes the view dbo.item_view, which is defined more than once, so the tables it writes underneath are not known", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_through_a_synonym_reaches_the_triggers_of_its_table()
    {
        // Observed: A_write_through_a_synonym_fires_the_triggers_of_its_table.
        var inside = Infer(
            ("synonym", "CREATE SYNONYM dbo.item_syn FOR dbo.item;"),
            ("trigger", "CREATE TRIGGER dbo.item_loud ON dbo.item AFTER INSERT AS SELECT N'fired' AS what;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item_syn (id) VALUES (1); SELECT 2 AS two;"));
        var outside = Infer(
            ("synonym", "CREATE SYNONYM dbo.far_syn FOR other_db.dbo.far;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.far_syn (id) VALUES (1); SELECT 2 AS two;"));

        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(inside.Doc).Reason);
        Assert.Empty(inside.Warnings);
        Assert.Single(Results(outside.Doc));
        var warning = Assert.Single(outside.Warnings);
        Assert.Contains("dbo.far_syn", warning.Message, StringComparison.Ordinal);
        Assert.Contains("other_db.dbo.far", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cascading_key_reaches_the_triggers_of_the_referencing_table()
    {
        // Observed: A_cascading_delete_fires_the_triggers_of_the_referencing_table. The key may be added by ALTER TABLE.
        const string Trigger = "CREATE TRIGGER dbo.child_loud ON dbo.child AFTER DELETE AS SELECT N'child' AS what;";
        const string Procedure = "CREATE PROCEDURE dbo.p AS DELETE FROM dbo.item WHERE id = 1; SELECT 1 AS a;";
        var inline = Doc(
            ("child", "CREATE TABLE dbo.child (id int PRIMARY KEY, item_id int NOT NULL REFERENCES dbo.item (id) ON DELETE CASCADE);"),
            ("trigger", Trigger),
            ("p", Procedure));
        var altered = Doc(
            ("child", "CREATE TABLE dbo.child (id int PRIMARY KEY, item_id int NULL);\nGO\nALTER TABLE dbo.child ADD CONSTRAINT fk_child_item FOREIGN KEY (item_id) REFERENCES dbo.item (id) ON DELETE SET NULL;"),
            ("trigger", Trigger.Replace("AFTER DELETE", "AFTER UPDATE", StringComparison.Ordinal)),
            ("p", Procedure));
        var plain = Doc(
            ("child", "CREATE TABLE dbo.child (id int PRIMARY KEY, item_id int NOT NULL REFERENCES dbo.item (id));"),
            ("trigger", Trigger),
            ("p", Procedure));

        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(inline).Reason);
        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(altered).Reason);
        Assert.Single(Results(plain));
    }

    [Fact]
    public void A_triggers_own_dml_and_calls_are_followed()
    {
        // Observed: A_triggers_own_dml_fires_other_triggers_whose_results_reach_the_client.
        const string Chain = "CREATE TRIGGER dbo.item_chain ON dbo.item AFTER INSERT AS INSERT INTO dbo.note (text) VALUES (N'x');";
        const string Procedure = "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item (id) VALUES (1); SELECT 1 AS a;";
        var nested = Doc(("chain", Chain), ("loud", "CREATE TRIGGER dbo.note_loud ON dbo.note AFTER INSERT AS SELECT N'nested' AS what;"), ("p", Procedure));
        var called = Doc(
            ("chain", Chain),
            ("q", "CREATE PROCEDURE dbo.q AS SELECT N'called' AS what;"),
            ("loud", "CREATE TRIGGER dbo.note_calls ON dbo.note AFTER INSERT AS EXEC dbo.q;"),
            ("p", Procedure));
        var quietCall = Doc(
            ("chain", Chain),
            ("q", "CREATE PROCEDURE dbo.q AS UPDATE dbo.item SET qty = 0;"),
            ("loud", "CREATE TRIGGER dbo.note_calls ON dbo.note AFTER INSERT AS EXEC dbo.q;"),
            ("p", Procedure));
        var recursive = Doc(
            ("self", "CREATE TRIGGER dbo.item_self ON dbo.item AFTER INSERT, UPDATE AS UPDATE dbo.item SET qty = qty + 0 WHERE id IN (SELECT id FROM inserted);"),
            ("p", Procedure));

        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(nested).Reason);
        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(called).Reason);
        Assert.Single(Results(quietCall));
        Assert.Single(Results(recursive));
    }

    [Theory]
    [InlineData("EXEC (N'SELECT 1');")]
    [InlineData("EXEC sp_who;")]
    [InlineData("DECLARE c CURSOR LOCAL FOR SELECT id FROM inserted; OPEN c; FETCH NEXT FROM c;")]
    [InlineData("UPDATE dbo.note SET text = N'x' OUTPUT inserted.id;")]
    [InlineData("WAITFOR (RECEIVE TOP (1) message_body FROM dbo.q), TIMEOUT 10;")]
    [InlineData("DBCC CHECKIDENT ('dbo.note');")]
    public void A_trigger_statement_that_may_return_results_counts(string statement)
    {
        // Anything not known to return nothing counts as returning something.
        var doc = Doc(
            ("trigger", $"CREATE TRIGGER dbo.item_odd ON dbo.item AFTER INSERT AS {statement}"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item (id) VALUES (1); SELECT 1 AS a;"));

        Assert.Equal(IrUnknownReason.TriggerResults, ResultsUnknown(doc).Reason);
    }

    [Theory]
    [InlineData("WAITFOR DELAY '00:00:01';")]
    [InlineData("WAITFOR TIME '23:59:59';")]
    [InlineData("EXECUTE AS USER = 'clerk'; REVERT;")]
    [InlineData("IF 1 = 1 GOTO done; SET ROWCOUNT 1; done:")]
    public void A_trigger_statement_known_to_return_nothing_adds_nothing(string statement)
    {
        // Observed: Waitfor_delay_and_time_return_nothing_and_waitfor_receive_returns_a_result_set and
        // Execute_as_changes_permissions_and_masks_and_names_only_outside_a_module_and_ends_when_the_procedure_returns.
        var doc = Doc(
            ("trigger", $"CREATE TRIGGER dbo.item_quiet ON dbo.item AFTER INSERT AS {statement}"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item (id) VALUES (1); SELECT 1 AS a;"));

        Assert.Single(Results(doc));
    }

    [Fact]
    public void A_cursor_select_in_a_trigger_returns_nothing()
    {
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.item_cursor ON dbo.item AFTER INSERT AS DECLARE @id int; DECLARE c CURSOR LOCAL FOR SELECT id FROM inserted; OPEN c; FETCH NEXT FROM c INTO @id; CLOSE c; DEALLOCATE c;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item (id) VALUES (1); SELECT 1 AS a;"));

        Assert.Single(Results(doc));
    }

    [Fact]
    public void A_table_outside_the_project_is_written_with_one_warning()
    {
        // A trigger there could return results, unseen; SQL Server allows them by default (observed:
        // Results_from_triggers_are_allowed_by_default). So the results stand, and the gap is said once per table.
        var inferred = Infer(
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.audit_trail (what) VALUES (N'p'); UPDATE dbo.audit_trail SET what = N'q'; SELECT 1 AS a;"),
            ("q", "CREATE PROCEDURE dbo.q AS DELETE FROM dbo.audit_trail; INSERT INTO other_db.dbo.log (what) VALUES (N'q');"));

        Assert.Single(Results(inferred.Doc));
        Assert.Empty(Results(inferred.Doc, "dbo.q"));
        Assert.Equal(2, inferred.Warnings.Count);
        Assert.All(inferred.Warnings, w => Assert.Equal(DiagnosticSeverity.Warning, w.Severity));
        Assert.Single(inferred.Warnings, w => w.Message.Contains("dbo.audit_trail", StringComparison.Ordinal));
        Assert.Single(inferred.Warnings, w => w.Message.Contains("other_db.dbo.log", StringComparison.Ordinal));
    }
}

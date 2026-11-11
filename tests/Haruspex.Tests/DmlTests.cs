using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// DML on permanent tables, <c>OUTPUT</c>, triggers, <c>WITH</c> before DML, and cursors. The engine rules each
/// test leans on are observed in the differential tests' <c>DmlObservationTests</c>, named in each test.
/// </summary>
public class DmlTests
{
    private const string Tables = """
        CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, label nvarchar(10) NULL, qty int NOT NULL DEFAULT 0);
        GO
        CREATE TABLE dbo.note (id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, text nvarchar(20) NULL);
        GO
        CREATE TABLE dbo.error_log (id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, message nvarchar(4000) NULL, at datetime2(0) NOT NULL);
        GO
        """;

    private sealed record Inferred(IrDocument Doc, IReadOnlyList<Diagnostic> Warnings);

    /// <summary>Infers every procedure of the files, over the standard tables.</summary>
    private static Inferred Infer(params (string Name, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        foreach (var (name, sql) in files)
            dir.File($"{name}.sql", sql);

        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        var warnings = new List<Diagnostic>();
        var doc = project.Infer(project.Select([ObjectPattern.Parse("*.*")]).Procedures, warnings);
        return new Inferred(doc, warnings);
    }

    private static IrDocument Doc(params (string Name, string Sql)[] files) => Infer(files).Doc;

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

    private static string[] Rules(IrField field) => [.. field.NullableBecause.Select(r => r.Rule)];

    // ---- DML returns no result set ---------------------------------------------------------------------------

    [Theory]
    [InlineData("INSERT INTO dbo.item (id, label) VALUES (1, N'a');")]
    [InlineData("INSERT INTO dbo.item (id) SELECT i.id + 0 FROM dbo.item AS i;")]
    [InlineData("INSERT INTO dbo.note DEFAULT VALUES;")]
    [InlineData("INSERT dbo.note (text) VALUES (NULL);")]
    [InlineData("UPDATE dbo.item SET qty = 1 WHERE id = 1;")]
    [InlineData("UPDATE i SET label = n.text FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    [InlineData("DELETE FROM dbo.item WHERE id = 1;")]
    [InlineData("DELETE i FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    [InlineData("MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id WHEN MATCHED THEN UPDATE SET label = s.text WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) WHEN NOT MATCHED BY SOURCE THEN DELETE;")]
    [InlineData("TRUNCATE TABLE dbo.note;")]
    [InlineData("WITH c AS (SELECT n.id FROM dbo.note AS n) INSERT INTO dbo.item (id) SELECT c.id FROM c;")]
    [InlineData("WITH c AS (SELECT i.id, i.label FROM dbo.item AS i) UPDATE c SET label = N'x';")]
    [InlineData("WITH c AS (SELECT i.id FROM dbo.item AS i) DELETE FROM c;")]
    [InlineData("WITH c AS (SELECT n.id FROM dbo.note AS n) MERGE dbo.item AS t USING c ON t.id = c.id WHEN MATCHED THEN DELETE;")]
    public void Dml_on_a_permanent_table_returns_no_result_set(string dml)
    {
        // Observed: Dml_on_a_permanent_table_returns_no_result_set.
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS SELECT i.id AS a FROM dbo.item AS i; {dml} SELECT i.qty AS b FROM dbo.item AS i;"));

        var results = Results(doc);
        Assert.Equal(2, results.Count);
        Assert.Equal(["a"], Names(doc, results[0].Type));
        Assert.Equal(["b"], Names(doc, results[1].Type));
    }

    [Fact]
    public void Insert_exec_into_a_permanent_table_returns_none_of_the_callees_result_sets()
    {
        var doc = Doc(
            ("q", "CREATE PROCEDURE dbo.q AS SELECT n.text FROM dbo.note AS n;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.note (text) EXEC dbo.q; SELECT i.id FROM dbo.item AS i;"));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void A_read_whose_catch_calls_a_logger_that_inserts_is_typed()
    {
        // The pattern that left a whole corpus untyped: every procedure's CATCH calls a shared logger that writes a table.
        var doc = Doc(
            ("log", "CREATE PROCEDURE dbo.log_error @message nvarchar(4000) AS SET NOCOUNT ON; INSERT INTO dbo.error_log (message, at) VALUES (@message, SYSUTCDATETIME());"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                BEGIN TRY
                    SELECT i.id, i.label FROM dbo.item AS i;
                END TRY
                BEGIN CATCH
                    DECLARE @m nvarchar(4000) = ERROR_MESSAGE();
                    EXEC dbo.log_error @m;
                    THROW;
                END CATCH
                """));

        var result = Assert.Single(Results(doc));
        Assert.Equal(["id", "label"], Names(doc, result.Type));
        Assert.False(Field(doc, "id").Nullable);
        Assert.Empty(Results(doc, "dbo.log_error"));
    }

    // ---- triggers --------------------------------------------------------------------------------------------

    [Fact]
    public void A_trigger_that_returns_a_result_set_makes_the_results_unknown()
    {
        // Observed: A_triggers_select_reaches_the_client_even_when_no_row_is_affected.
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.item_loud ON dbo.item AFTER UPDATE AS SELECT COUNT_BIG(*) AS changed FROM inserted;"),
            ("p", "CREATE PROCEDURE dbo.p AS UPDATE dbo.item SET qty = 1; SELECT 1 AS a;"));

        var unknown = ResultsUnknown(doc);
        Assert.Equal(IrUnknownReasons.TriggerResults, unknown.Reason);
        Assert.Contains("dbo.item_loud", unknown.Detail, StringComparison.Ordinal);
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
            Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(doc).Reason);
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

        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(instead).Reason);
        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(underneath).Reason);
        Assert.Single(Results(quiet));
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

        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(inside.Doc).Reason);
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

        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(inline).Reason);
        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(altered).Reason);
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

        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(nested).Reason);
        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(called).Reason);
        Assert.Single(Results(quietCall));
        Assert.Single(Results(recursive));
    }

    [Theory]
    [InlineData("EXEC (N'SELECT 1');")]
    [InlineData("EXEC sp_who;")]
    [InlineData("DECLARE c CURSOR LOCAL FOR SELECT id FROM inserted; OPEN c; FETCH NEXT FROM c;")]
    [InlineData("UPDATE dbo.note SET text = N'x' OUTPUT inserted.id;")]
    [InlineData("WAITFOR DELAY '00:00:01';")]
    [InlineData("DBCC CHECKIDENT ('dbo.note');")]
    public void A_trigger_statement_that_may_return_results_counts(string statement)
    {
        // Anything not known to return nothing counts as returning something.
        var doc = Doc(
            ("trigger", $"CREATE TRIGGER dbo.item_odd ON dbo.item AFTER INSERT AS {statement}"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item (id) VALUES (1); SELECT 1 AS a;"));

        Assert.Equal(IrUnknownReasons.TriggerResults, ResultsUnknown(doc).Reason);
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

    // ---- OUTPUT ----------------------------------------------------------------------------------------------

    [Fact]
    public void Output_without_into_returns_the_rows_written()
    {
        // Observed: Output_without_into_returns_a_result_set_even_when_no_row_is_affected,
        // Output_columns_are_the_values_converted_to_the_target_columns.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            INSERT INTO dbo.item (id) OUTPUT inserted.id, inserted.label, inserted.qty AS amount VALUES (1);
            UPDATE dbo.item SET label = NULL OUTPUT deleted.label AS was, inserted.label AS now WHERE id = 1;
            DELETE FROM dbo.note OUTPUT deleted.id;
            """));

        var results = Results(doc);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(IrCardinality.Many, r.Cardinality));
        Assert.All(results, r => Assert.False(r.MayBeAbsent));
        Assert.Equal(["id", "label", "amount"], Names(doc, results[0].Type));
        Assert.False(Field(doc, "id").Nullable);
        Assert.False(Field(doc, "amount").Nullable);
        Assert.True(Field(doc, "label").Nullable);
        Assert.Equal(["was", "now"], Names(doc, results[1].Type));
        Assert.True(Field(doc, "was", 1).Nullable);
        Assert.False(Field(doc, "id", 2).Nullable);
    }

    [Fact]
    public void Output_star_lists_the_targets_columns()
    {
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS DELETE FROM dbo.item OUTPUT deleted.*;"));

        Assert.Equal(["id", "label", "qty"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void Merge_output_columns_are_null_for_the_actions_that_have_no_value()
    {
        // Observed: Merge_output_has_null_deleted_columns_for_inserted_rows_null_inserted_columns_for_deleted_rows_and_null_source_columns_when_not_matched_by_source.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET label = s.text
            WHEN NOT MATCHED BY TARGET THEN INSERT (id) VALUES (s.id)
            WHEN NOT MATCHED BY SOURCE THEN DELETE
            OUTPUT $action, inserted.id AS new_id, deleted.id AS old_id, s.id AS source_id;
            MERGE dbo.item AS t USING dbo.note AS s ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET label = s.text
            OUTPUT $action AS what, inserted.id AS new_id, deleted.id AS old_id, s.id AS source_id;
            """));

        Assert.Equal(["$action", "new_id", "old_id", "source_id"], Names(doc, Results(doc)[0].Type));
        Assert.Equal([IrNullRules.MergeAction], Rules(Field(doc, "new_id")));
        Assert.Equal([IrNullRules.MergeAction], Rules(Field(doc, "old_id")));
        Assert.Equal([IrNullRules.MergeAction], Rules(Field(doc, "source_id")));
        var action = Assert.IsType<IrScalar>(Field(doc, "$action").Type);
        Assert.False(Field(doc, "$action").Nullable);
        Assert.Equal(["DELETE", "INSERT", "UPDATE"], action.Facets!.Values!.Items.Select(v => v.Text));
        Assert.Equal(IrValueMatch.Exact, action.Facets.Values.Match);

        // Only updates: nothing is null, and the action is one value.
        Assert.False(Field(doc, "new_id", 1).Nullable);
        Assert.False(Field(doc, "old_id", 1).Nullable);
        Assert.False(Field(doc, "source_id", 1).Nullable);
        Assert.Equal(["UPDATE"], Assert.IsType<IrScalar>(Field(doc, "what", 1).Type).Facets!.Values!.Items.Select(v => v.Text));
    }

    [Fact]
    public void Every_source_of_a_joined_using_is_null_for_rows_not_matched_by_source()
    {
        // USING a JOIN b ON … is a source of several tables, each null-extended for those rows.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            MERGE dbo.item AS t USING dbo.note AS s JOIN dbo.error_log AS e ON e.id = s.id ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET label = N'm'
            WHEN NOT MATCHED BY SOURCE THEN DELETE
            OUTPUT s.id AS note_id, e.at;
            """));

        Assert.Equal([IrNullRules.MergeAction], Rules(Field(doc, "note_id")));
        Assert.Equal([IrNullRules.MergeAction], Rules(Field(doc, "at")));
    }

    [Theory]
    [InlineData("UPDATE i SET qty = 0 OUTPUT inserted.label AS now, deleted.qty AS was FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    [InlineData("DELETE i OUTPUT deleted.label AS now, deleted.qty AS was FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id;")]
    public void Output_of_a_write_to_an_alias_in_from_is_typed_from_its_table(string dml)
    {
        var doc = Doc(("p", $"CREATE PROCEDURE dbo.p AS {dml}"));

        Assert.Equal([IrNullRules.NullableColumn], Rules(Field(doc, "now")));
        Assert.False(Field(doc, "was").Nullable);
        Assert.IsType<IrScalar>(Field(doc, "was").Type);
    }

    [Fact]
    public void Output_of_a_write_through_a_cte_is_unknown()
    {
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS WITH c AS (SELECT i.id, i.label FROM dbo.item AS i) UPDATE c SET label = N'x' OUTPUT inserted.label;"));

        Assert.IsType<IrUnknownType>(Field(doc, "label").Type);
    }

    [Fact]
    public void Output_of_the_from_sources_is_refined_by_the_where()
    {
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS UPDATE i SET qty = 0 OUTPUT n.text FROM dbo.item AS i JOIN dbo.note AS n ON n.id = i.id WHERE n.text IS NOT NULL;"));

        Assert.False(Field(doc, "text").Nullable);
    }

    [Fact]
    public void Output_reading_the_target_by_its_own_name_is_unknown()
    {
        // Whether it is the row before or after the update is not observed, and a filter about it may not hold after.
        var doc = Doc(("p", "CREATE PROCEDURE dbo.p AS UPDATE i SET label = NULL OUTPUT i.label FROM dbo.item AS i WHERE i.label IS NOT NULL;"));

        Assert.IsType<IrUnknownType>(Field(doc, "label").Type);
    }

    [Fact]
    public void Output_on_a_table_with_a_trigger_that_returns_nothing_is_the_output()
    {
        // Observed: Output_without_into_fails_on_a_table_with_an_enabled_trigger_and_works_when_it_is_disabled.
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.item_quiet ON dbo.item AFTER INSERT AS DECLARE @n int = 1;"),
            ("p", "CREATE PROCEDURE dbo.p AS INSERT INTO dbo.item (id) OUTPUT inserted.id VALUES (1);"));

        Assert.Equal(["id"], Names(doc, Assert.Single(Results(doc)).Type));
    }

    [Fact]
    public void Output_into_returns_nothing_and_writes_its_temporary_table()
    {
        // Observed: Output_into_returns_nothing_and_writes_its_rows; Output_into_a_table_with_an_enabled_trigger_fails, so the
        // OUTPUT target's own triggers are not consulted.
        var doc = Doc(
            ("trigger", "CREATE TRIGGER dbo.note_loud ON dbo.note AFTER INSERT AS SELECT N'note' AS what;"),
            ("p", """
                CREATE PROCEDURE dbo.p AS
                DECLARE @one int = 1;
                SELECT @one AS id, N'x' AS label INTO #moved;
                DECLARE @t table (id int NOT NULL, label nvarchar(10) NULL);
                UPDATE dbo.item SET qty = 0 OUTPUT inserted.id, deleted.label INTO #moved (id, label);
                UPDATE dbo.item SET qty = 0 OUTPUT inserted.id, deleted.label INTO @t;
                UPDATE dbo.item SET qty = 0 OUTPUT inserted.id, deleted.label INTO dbo.note (id, text);
                SELECT m.label FROM #moved AS m;
                """));

        var result = Assert.Single(Results(doc));
        Assert.Contains(IrNullRules.NullableColumn, Rules(Field(doc, "label")));
        Assert.Equal(["label"], Names(doc, result.Type));
    }

    [Fact]
    public void Output_from_a_temporary_table_reads_it_before_and_after()
    {
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @one int = 1;
            SELECT @one AS n INTO #t;
            UPDATE #t SET n = NULL OUTPUT deleted.n AS was, inserted.n AS now;
            """));

        // deleted is the table before the update, whose column held only the variable's value; inserted, after it.
        Assert.True(Field(doc, "now").Nullable);
        Assert.False(Field(doc, "was").Nullable);
    }

    // ---- WITH before DML -------------------------------------------------------------------------------------

    [Fact]
    public void A_cte_target_over_a_temporary_table_leaves_it_unknown()
    {
        // The write goes through to the table underneath.
        var doc = Doc(("p", """
            CREATE PROCEDURE dbo.p AS
            DECLARE @one int = 1;
            SELECT @one AS n INTO #t;
            WITH c AS (SELECT t.n FROM #t AS t) UPDATE c SET n = NULL;
            SELECT t.n FROM #t AS t;
            """));

        Assert.IsType<IrUnknownType>(Field(doc, "n").Type);
    }

    // ---- cursors ---------------------------------------------------------------------------------------------

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

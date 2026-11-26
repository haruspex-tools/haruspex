using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server returns for DML on permanent tables, for <c>OUTPUT</c>, for triggers and for cursors, observed and kept
/// as tests: the rules haruspex's reading of writes rests on (Haruspex.SqlServer.Inference's README.md, "DML" and
/// "Cursors"). Each test creates what it needs in a write session, rolled back, alone in the test database while it lasts.
/// Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public partial class DmlObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages = false) =>
        ObservedCall.Run(server, setup, call, errorsAsMessages);

    private const string Item = "CREATE TABLE dbo.obs_item (id int NOT NULL PRIMARY KEY, label nvarchar(10) NULL, qty int NOT NULL DEFAULT 0);";
    private const string Note = "CREATE TABLE dbo.obs_note (id int IDENTITY PRIMARY KEY, text nvarchar(20) NULL);";
    private const string Seed = "INSERT INTO dbo.obs_item (id, label, qty) VALUES (1, N'one', 1), (2, NULL, 2);";

    // ---- DML returns no result set -----------------------------------------------------------------------

    [Fact]
    public async Task Dml_on_a_permanent_table_returns_no_result_set()
    {
        // With NOCOUNT off, each statement sends a row count, which a client sees as no result set (no columns): only the
        // final SELECT arrives. Every form haruspex models: INSERT … VALUES, … SELECT, … EXEC, … DEFAULT VALUES; UPDATE, with
        // FROM and a join; DELETE, with FROM; MERGE; TRUNCATE TABLE; and each behind a WITH.
        string[] setup = [
            Item, Note,
            "CREATE PROCEDURE dbo.obs_source AS SELECT N'from exec' AS text;",
            """
            CREATE PROCEDURE dbo.obs_writes AS
            SET NOCOUNT OFF;
            INSERT INTO dbo.obs_item (id, label) VALUES (1, N'a'), (2, NULL);
            INSERT INTO dbo.obs_item (id, label) SELECT 3, N'c';
            INSERT INTO dbo.obs_note (text) EXEC dbo.obs_source;
            INSERT INTO dbo.obs_note DEFAULT VALUES;
            UPDATE dbo.obs_item SET qty = qty + 1 WHERE id = 1;
            UPDATE i SET label = n.text FROM dbo.obs_item AS i JOIN dbo.obs_note AS n ON n.id = i.id;
            DELETE FROM dbo.obs_item WHERE id = 3;
            DELETE i FROM dbo.obs_item AS i JOIN dbo.obs_note AS n ON n.id = i.id WHERE n.text IS NULL;
            MERGE dbo.obs_item AS t USING (VALUES (1, N'm'), (9, N'n')) AS s (id, label) ON t.id = s.id
              WHEN MATCHED THEN UPDATE SET label = s.label WHEN NOT MATCHED THEN INSERT (id, label) VALUES (s.id, s.label);
            WITH c AS (SELECT id FROM dbo.obs_item WHERE id = 9) DELETE FROM dbo.obs_item WHERE id IN (SELECT id FROM c);
            WITH c AS (SELECT id, label FROM dbo.obs_item) UPDATE c SET label = N'u' WHERE id = 1;
            WITH c AS (SELECT 5 AS id) INSERT INTO dbo.obs_item (id) SELECT id FROM c;
            WITH c AS (SELECT 5 AS id) MERGE dbo.obs_item AS t USING c ON t.id = c.id WHEN MATCHED THEN DELETE;
            SELECT COUNT_BIG(*) AS items FROM dbo.obs_item;
            TRUNCATE TABLE dbo.obs_note;
            SELECT COUNT_BIG(*) AS notes FROM dbo.obs_note;
            """,
        ];

        Assert.Equal("items: 1 | notes: 0", (await Run(setup, "EXEC dbo.obs_writes")).ToString());
    }

    // ---- OUTPUT ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Output_without_into_returns_a_result_set_even_when_no_row_is_affected()
    {
        // One result set per statement, named by the column (or its alias), with no rows when nothing matched: so it is
        // always there, a rowset of many rows.
        string[] setup = [Item, Seed];

        Assert.Equal("id,label: 3,c", (await Run(setup, "INSERT INTO dbo.obs_item (id, label) OUTPUT inserted.id, inserted.label VALUES (3, N'c');")).ToString());
        Assert.Equal("id,was: ", (await Run(setup, "UPDATE dbo.obs_item SET qty = 5 OUTPUT inserted.id, deleted.qty AS was WHERE id = 99;")).ToString());
        Assert.Equal("id: 1;2", (await Run(setup, "DELETE FROM dbo.obs_item OUTPUT deleted.id;")).ToString());
    }

    [Fact]
    public async Task Output_into_returns_nothing_and_writes_its_rows()
    {
        Assert.Equal("id,label: 1,one;2,null", (await Run([Item, Seed],
            "DECLARE @t table (id int, label nvarchar(10)); UPDATE dbo.obs_item SET qty = 0 OUTPUT inserted.id, deleted.label INTO @t; SELECT id, label FROM @t ORDER BY id;")).ToString());
    }

    [Fact]
    public async Task Merge_output_has_null_deleted_columns_for_inserted_rows_null_inserted_columns_for_deleted_rows_and_null_source_columns_when_not_matched_by_source()
    {
        // Columns of NOT NULL ones all, so every null seen is the action's. $action is the column's name, and its values are
        // INSERT, UPDATE and DELETE.
        string[] setup = [Item, "INSERT INTO dbo.obs_item (id, label, qty) VALUES (1, N'one', 1), (2, N'two', 2);"];
        const string Merge = """
            MERGE dbo.obs_item AS t
            USING (VALUES (1, 10), (3, 30)) AS s (id, n) ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET qty = s.n
            WHEN NOT MATCHED BY TARGET THEN INSERT (id, qty) VALUES (s.id, s.n)
            WHEN NOT MATCHED BY SOURCE THEN DELETE
            OUTPUT $action, inserted.id AS new_id, inserted.qty AS new_qty, deleted.id AS old_id, deleted.qty AS old_qty, s.id AS source_id, s.n;
            """;

        var outcome = (await Run(setup, Merge)).ToString();
        Assert.StartsWith("$action,new_id,new_qty,old_id,old_qty,source_id,n: ", outcome, StringComparison.Ordinal);
        var rows = outcome.Split(": ")[1].Split(';').Order(StringComparer.Ordinal);
        Assert.Equal(["DELETE,null,null,2,2,null,null", "INSERT,3,30,null,null,3,30", "UPDATE,1,10,1,1,1,10"], rows);
    }

    [Fact]
    public async Task Merge_into_a_temporary_table_writes_each_actions_rows_and_may_set_a_variable()
    {
        // Into a table SELECT … INTO made (n nullable, from a variable): the update sets 2's n to NULL, the insert leaves 3's
        // out, the delete removes 1. The UPDATE's SET @v sets the variable as an UPDATE's does: to the row's value, here NULL.
        var outcome = await Run([], """
            DECLARE @ten int = 10, @v int = 5;
            SELECT 1 AS hr, @ten AS n INTO #obs_hourly UNION ALL SELECT 2, 20;
            MERGE #obs_hourly AS t USING (VALUES (2, CAST(NULL AS int)), (3, 30)) AS s (hr, n) ON t.hr = s.hr
            WHEN MATCHED THEN UPDATE SET n = s.n, @v = s.n
            WHEN NOT MATCHED BY TARGET THEN INSERT (hr) VALUES (s.hr)
            WHEN NOT MATCHED BY SOURCE THEN DELETE
            OUTPUT $action AS what, inserted.n AS now, deleted.n AS was;
            SELECT h.hr, h.n, ISNULL(CONVERT(varchar(5), @v), 'null') AS v FROM #obs_hourly AS h ORDER BY h.hr;
            """);
        var sets = outcome.Sets;
        Assert.Equal(["DELETE,null,10", "INSERT,null,null", "UPDATE,null,20"], sets[0].Split(": ")[1].Split(';').Order(StringComparer.Ordinal));
        Assert.Equal("hr,n,v: 2,null,null;3,null,null", sets[1]);
    }

    [Fact]
    public async Task An_update_under_with_xmlnamespaces_sets_its_variables() =>
        // WITH XMLNAMESPACES before a write changes nothing it writes: the variable is set, here to the row's NULL.
        Assert.Equal("v: null", (await Run([Item, Seed], "DECLARE @v nvarchar(10) = N'x'; WITH XMLNAMESPACES ('urn:shop' AS s) UPDATE dbo.obs_item SET @v = label WHERE id = 2; SELECT ISNULL(@v, N'null') AS v;")).ToString());

    [Fact]
    public async Task A_merge_action_reads_the_target_through_its_alias() =>
        // MERGE #t AS t: an action reads the target's row as t, which OUTPUT can't name (4104, as for an UPDATE's alias).
        Assert.Equal("hr,n,x: 1,11,1", (await Run([], """
            DECLARE @x int = 0;
            CREATE TABLE #obs_hourly (hr int NOT NULL, n int NULL);
            INSERT INTO #obs_hourly VALUES (1, 10);
            MERGE #obs_hourly AS t USING (VALUES (1)) AS s (hr) ON t.hr = s.hr WHEN MATCHED THEN UPDATE SET n = t.n + 1, @x = t.hr;
            SELECT h.hr, h.n, @x AS x FROM #obs_hourly AS h;
            """)).ToString());

    [Fact]
    public async Task A_merge_action_reads_only_the_rows_it_has()
    {
        // WHEN NOT MATCHED [BY TARGET] has no target row and WHEN NOT MATCHED BY SOURCE no source row: a column of the other is
        // refused (4104), so an action never reads a row it lacks.
        const string Table = "CREATE TABLE #obs_h (hr int NOT NULL, n int NULL);";
        Assert.Equal(4104, (await Run([], Table + " MERGE #obs_h AS t USING (SELECT 2 AS hr, 7 AS n) AS s ON t.hr = s.hr WHEN NOT MATCHED THEN INSERT (hr, n) VALUES (t.hr, s.n);")).Error);
        Assert.Equal(4104, (await Run([], Table + " MERGE #obs_h AS t USING (SELECT 2 AS hr, 7 AS n) AS s ON t.hr = s.hr WHEN NOT MATCHED BY SOURCE THEN UPDATE SET n = s.n;")).Error);
    }

    [Fact]
    public async Task Merge_action_is_an_nvarchar_10()
    {
        // As the server describes the result: $action is nvarchar(10). Its values are the three above.
        string[] setup = [Item];
        const string Describe = """
            SELECT name, system_type_name AS type FROM sys.dm_exec_describe_first_result_set(N'
              MERGE dbo.obs_item AS t USING (VALUES (1)) AS s (id) ON t.id = s.id
              WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) OUTPUT $action, inserted.id;', NULL, 0) ORDER BY column_ordinal;
            """;

        Assert.Equal("name,type: $action,nvarchar(10);id,int", (await Run(setup, Describe)).ToString());
    }

    [Fact]
    public async Task Output_columns_are_the_values_converted_to_the_target_columns() =>
        // inserted holds what the row holds: a value converted to the column's type (here a string into int) and a default
        // where the insert leaves the column out; never a null where the column is NOT NULL.
        Assert.Equal("id,qty,label: 7,0,null", (await Run([Item], "INSERT INTO dbo.obs_item (id) OUTPUT inserted.id, inserted.qty, inserted.label VALUES ('7');")).ToString());

    [Fact]
    public async Task Output_without_into_fails_on_a_table_with_an_enabled_trigger_and_works_when_it_is_disabled()
    {
        // Error 334. So a target with a trigger that returns nothing gives the OUTPUT result set, or fails: typing it as the
        // OUTPUT result is sound either way.
        string[] setup = [Item, "CREATE TRIGGER dbo.obs_item_quiet ON dbo.obs_item AFTER INSERT AS DECLARE @n int = 1;"];

        Assert.Equal(334, (await Run(setup, "INSERT INTO dbo.obs_item (id) OUTPUT inserted.id VALUES (1);")).Error);

        // Disabled in a batch before: in the same batch the statement is compiled while the trigger is still enabled, and fails.
        Assert.Equal("id: 1", (await Run([.. setup, "DISABLE TRIGGER dbo.obs_item_quiet ON dbo.obs_item;"], "INSERT INTO dbo.obs_item (id) OUTPUT inserted.id VALUES (1);")).ToString());
    }

    [Fact]
    public async Task Output_into_a_table_with_an_enabled_trigger_fails()
    {
        // Error 331: so the OUTPUT … INTO target's own triggers never run, and only the statement's target's are checked.
        string[] setup = [Item, Note, "CREATE TRIGGER dbo.obs_note_loud ON dbo.obs_note AFTER INSERT AS SELECT N'note trigger' AS t;"];

        Assert.Equal(331, (await Run(setup, "INSERT INTO dbo.obs_item (id) OUTPUT N'x' INTO dbo.obs_note (text) VALUES (1);")).Error);
    }

    // ---- triggers ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Results_from_triggers_are_allowed_by_default() =>
        // The server option is off unless an administrator turns it on, and the feature is deprecated, not removed.
        Assert.Equal("value_in_use: 0", (await Run([], "SELECT CONVERT(int, value_in_use) AS value_in_use FROM sys.configurations WHERE name = N'disallow results from triggers';")).ToString());

    [Fact]
    public async Task A_triggers_select_reaches_the_client_even_when_no_row_is_affected()
    {
        // An AFTER trigger fires once per statement, rows or not, and its result set arrives in the caller's stream where the
        // statement is. A trigger that returns nothing adds nothing.
        string[] setup = [
            Item, Note,
            "CREATE TRIGGER dbo.obs_item_loud ON dbo.obs_item AFTER UPDATE AS SELECT COUNT_BIG(*) AS changed FROM inserted;",
            "CREATE TRIGGER dbo.obs_note_quiet ON dbo.obs_note AFTER INSERT AS SET NOCOUNT ON; DECLARE @n int = 1;",
            "CREATE PROCEDURE dbo.obs_touch AS SELECT 1 AS a; UPDATE dbo.obs_item SET qty = 1 WHERE id = 99; INSERT INTO dbo.obs_note (text) VALUES (N'x'); SELECT 2 AS b;",
        ];

        Assert.Equal("a: 1 | changed: 0 | b: 2", (await Run(setup, "EXEC dbo.obs_touch")).ToString());
    }

    [Fact]
    public async Task An_instead_of_trigger_on_a_view_returns_its_result_set()
    {
        string[] setup = [
            Item,
            "CREATE VIEW dbo.obs_item_view AS SELECT id, label FROM dbo.obs_item;",
            "CREATE TRIGGER dbo.obs_item_view_insert ON dbo.obs_item_view INSTEAD OF INSERT AS SELECT N'instead' AS what;",
        ];

        Assert.Equal("what: instead | n: 0", (await Run(setup, "INSERT INTO dbo.obs_item_view (id, label) VALUES (1, N'x'); SELECT COUNT_BIG(*) AS n FROM dbo.obs_item;")).ToString());
    }

    [Fact]
    public async Task Dml_through_a_view_or_a_cte_fires_the_triggers_of_the_table_underneath()
    {
        string[] setup = [
            Item, Seed,
            "CREATE VIEW dbo.obs_item_view AS SELECT id, label FROM dbo.obs_item;",
            "CREATE TRIGGER dbo.obs_item_loud ON dbo.obs_item AFTER UPDATE AS SELECT N'fired' AS what;",
        ];

        Assert.Equal("what: fired", (await Run(setup, "UPDATE dbo.obs_item_view SET label = N'v' WHERE id = 1;")).ToString());
        Assert.Equal("what: fired", (await Run(setup, "WITH c AS (SELECT id, label FROM dbo.obs_item) UPDATE c SET label = N'c' WHERE id = 1;")).ToString());
    }

    [Fact]
    public async Task A_write_nested_in_an_insert_fires_its_tables_triggers()
    {
        // INSERT … SELECT … FROM (DELETE … OUTPUT …) AS d: the DELETE is a write of its own, and its table's trigger returns its
        // result set as a DELETE alone would.
        string[] setup = [
            Item, Seed,
            "CREATE TRIGGER dbo.obs_item_gone ON dbo.obs_item AFTER DELETE AS SELECT N'fired' AS what;",
        ];

        Assert.Equal(
            "what: fired | a: 1",
            (await Run(setup, "DECLARE @t TABLE (id int); INSERT INTO @t (id) SELECT d.id FROM (DELETE FROM dbo.obs_item OUTPUT deleted.id) AS d; SELECT 1 AS a;")).ToString());
    }

    [Fact]
    public async Task A_write_through_a_synonym_fires_the_triggers_of_its_table()
    {
        string[] setup = [
            Item,
            "CREATE SYNONYM dbo.obs_item_syn FOR dbo.obs_item;",
            "CREATE TRIGGER dbo.obs_item_loud ON dbo.obs_item AFTER INSERT AS SELECT N'fired' AS what;",
        ];

        Assert.Equal("what: fired", (await Run(setup, "INSERT INTO dbo.obs_item_syn (id) VALUES (1);")).ToString());
    }

    [Fact]
    public async Task Truncate_does_not_fire_a_delete_trigger()
    {
        string[] setup = [Item, Seed, "CREATE TRIGGER dbo.obs_item_loud ON dbo.obs_item AFTER DELETE AS SELECT N'fired' AS what;"];

        Assert.Equal("n: 0", (await Run(setup, "TRUNCATE TABLE dbo.obs_item; SELECT COUNT_BIG(*) AS n FROM dbo.obs_item;")).ToString());
        Assert.Equal("what: fired", (await Run(setup, "DELETE FROM dbo.obs_item WHERE id = 99;")).ToString());
    }

    [Fact]
    public async Task Merge_fires_the_trigger_of_each_action_it_has()
    {
        // An insert-only MERGE fires the INSERT trigger and not the UPDATE one.
        string[] setup = [
            Item,
            "CREATE TRIGGER dbo.obs_item_on_insert ON dbo.obs_item AFTER INSERT AS SELECT N'insert' AS what;",
            "CREATE TRIGGER dbo.obs_item_on_update ON dbo.obs_item AFTER UPDATE AS SELECT N'update' AS what;",
        ];

        Assert.Equal("what: insert", (await Run(setup, "MERGE dbo.obs_item AS t USING (VALUES (1)) AS s (id) ON t.id = s.id WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id);")).ToString());
    }

    [Fact]
    public async Task A_cascading_delete_fires_the_triggers_of_the_referencing_table()
    {
        string[] setup = [
            Item, Seed,
            "CREATE TABLE dbo.obs_child (id int PRIMARY KEY, item_id int NOT NULL REFERENCES dbo.obs_item (id) ON DELETE CASCADE);",
            "INSERT INTO dbo.obs_child VALUES (10, 1);",
            "CREATE TRIGGER dbo.obs_child_loud ON dbo.obs_child AFTER DELETE AS SELECT N'child' AS what;",
        ];

        Assert.Equal("what: child", (await Run(setup, "DELETE FROM dbo.obs_item WHERE id = 1;")).ToString());
    }

    [Fact]
    public async Task A_triggers_own_dml_fires_other_triggers_whose_results_reach_the_client()
    {
        // Nested triggers are on by default: a trigger's INSERT fires the next table's trigger, and a procedure a trigger
        // calls returns its result sets to the client too.
        string[] setup = [
            Item, Note,
            "CREATE PROCEDURE dbo.obs_from_trigger AS SELECT N'called' AS what;",
            "CREATE TRIGGER dbo.obs_note_loud ON dbo.obs_note AFTER INSERT AS SELECT N'nested' AS what; EXEC dbo.obs_from_trigger;",
            "CREATE TRIGGER dbo.obs_item_chain ON dbo.obs_item AFTER INSERT AS INSERT INTO dbo.obs_note (text) VALUES (N'x');",
        ];

        Assert.Equal("what: nested | what: called", (await Run(setup, "INSERT INTO dbo.obs_item (id) VALUES (1);")).ToString());
    }

    // ---- cursors ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cursor_statements_and_fetch_into_return_nothing_and_fetch_without_into_returns_one_row()
    {
        // FETCH without INTO returns a result set of the cursor's columns with the one row fetched; past the end, the same
        // result set with no rows. Each also carries ROWSTAT, a column the server marks hidden, which SqlClient lists among
        // the columns (FieldCount, GetName) and a driver that honours the flag leaves out: so which keys the row has depends
        // on the client, and the result is unknown (hidden-column).
        string[] setup = [
            Item, Seed,
            """
            CREATE PROCEDURE dbo.obs_cursor AS
            SET NOCOUNT OFF;
            DECLARE @id int, @label nvarchar(10);
            DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT id, label FROM dbo.obs_item ORDER BY id;
            DECLARE g CURSOR GLOBAL STATIC FOR SELECT id FROM dbo.obs_item WHERE id = 99;
            OPEN c; OPEN g;
            FETCH NEXT FROM c INTO @id, @label;
            FETCH NEXT FROM c;
            FETCH NEXT FROM c;
            FETCH NEXT FROM g;
            CLOSE c; CLOSE g; DEALLOCATE c; DEALLOCATE g;
            SELECT @id AS id;
            """,
        ];

        Assert.Equal("id,label,ROWSTAT: 2,null,1 | id,label,ROWSTAT:  | id,ROWSTAT:  | id: 1", (await Run(setup, "EXEC dbo.obs_cursor")).ToString());
    }

    [Fact]
    public async Task Fetch_into_leaves_the_variables_as_they_were_past_the_end()
    {
        string[] setup = [
            Item,
            """
            CREATE PROCEDURE dbo.obs_fetch_end AS
            DECLARE @id int = 5;
            DECLARE c CURSOR LOCAL FOR SELECT id FROM dbo.obs_item;
            OPEN c; FETCH NEXT FROM c INTO @id; CLOSE c; DEALLOCATE c;
            SELECT @id AS id, @@FETCH_STATUS AS status;
            """,
        ];

        Assert.Equal("id,status: 5,-1", (await Run(setup, "EXEC dbo.obs_fetch_end")).ToString());
    }

    [Fact]
    public async Task Fetch_into_from_a_keyset_cursor_whose_row_was_deleted_assigns_null_or_zeros_whatever_the_checks_say()
    {
        // @@FETCH_STATUS -2: the variables are assigned, not left alone. A nullable column gives NULL; a NOT NULL one zero,
        // blanks, the earliest date or the empty GUID, inside its type and NOT NULL but outside its CHECK. Sound because a
        // variable keeps only its declared type and why it may be null, never a column's CHECK.
        string[] setup = [
            "CREATE TABLE dbo.obs_gone (id int NOT NULL PRIMARY KEY, q int NOT NULL CHECK (q > 0), s varchar(10) NOT NULL CHECK (s IN ('open', 'done')), d date NOT NULL, c char(3) NOT NULL, g uniqueidentifier NOT NULL, n int NULL);",
            "INSERT INTO dbo.obs_gone VALUES (1, 5, 'open', '2026-11-10', 'abc', NEWID(), 7);",
            """
            CREATE PROCEDURE dbo.obs_gone_fetch AS
            DECLARE @q int = -1, @s varchar(10) = 'x', @d date = '2000-01-01', @c char(3) = 'x', @g uniqueidentifier = NEWID(), @n int = -1;
            DECLARE k CURSOR LOCAL KEYSET FOR SELECT q, s, d, c, g, n FROM dbo.obs_gone;
            OPEN k;
            DELETE FROM dbo.obs_gone;
            FETCH NEXT FROM k INTO @q, @s, @d, @c, @g, @n;
            SELECT @@FETCH_STATUS AS status, @q AS q, DATALENGTH(@s) AS s_bytes, LEN(@s) AS s_len, CONVERT(char(10), @d, 23) AS d, DATALENGTH(@c) AS c_bytes, @g AS g, @n AS n;
            CLOSE k; DEALLOCATE k;
            """,
        ];

        Assert.Equal("status,q,s_bytes,s_len,d,c_bytes,g,n: -2,0,10,0,0001-01-01,3,00000000-0000-0000-0000-000000000000,null", (await Run(setup, "EXEC dbo.obs_gone_fetch")).ToString());
    }

    [Fact]
    public async Task A_cursors_query_takes_its_variables_as_they_are_when_it_is_declared()
    {
        // Set after DECLARE and before OPEN, the variable is still NULL in the rows fetched, for STATIC and DYNAMIC alike. So a
        // variable read in a cursor's query is any value of its type, or null.
        string[] setup = [
            """
            CREATE PROCEDURE dbo.obs_cursor_variable @kind int AS
            DECLARE @x int, @v int = 0;
            IF @kind = 1 DECLARE c CURSOR LOCAL STATIC FOR SELECT @x AS v;
            ELSE DECLARE c CURSOR LOCAL DYNAMIC FOR SELECT @x AS v;
            SET @x = 5;
            OPEN c; FETCH NEXT FROM c INTO @v; CLOSE c; DEALLOCATE c;
            SELECT @v AS v;
            """,
        ];

        Assert.Equal("v: null", (await Run(setup, "EXEC dbo.obs_cursor_variable 1")).ToString());
        Assert.Equal("v: null", (await Run(setup, "EXEC dbo.obs_cursor_variable 2")).ToString());
    }

    [Fact]
    public async Task A_keyset_cursor_fetches_a_row_changed_after_open_that_its_where_no_longer_keeps()
    {
        // So a cursor's WHERE proves nothing of the row a FETCH returns: the row is read again when it is fetched.
        string[] setup = [
            Item, Seed,
            """
            CREATE PROCEDURE dbo.obs_keyset AS
            DECLARE c CURSOR LOCAL KEYSET FOR SELECT id, label FROM dbo.obs_item WHERE label IS NOT NULL;
            OPEN c;
            UPDATE dbo.obs_item SET label = NULL WHERE id = 1;
            FETCH NEXT FROM c;
            CLOSE c; DEALLOCATE c;
            """,
        ];

        Assert.Equal("id,label,ROWSTAT: 1,null,1", (await Run(setup, "EXEC dbo.obs_keyset")).ToString());
    }

    [Fact]
    public async Task A_dynamic_cursor_fetches_a_temporary_tables_rows_as_they_are_when_fetched()
    {
        // A row inserted after OPEN is fetched, with what was inserted: so a FETCH reads the tables as they stand then.
        string[] setup = [
            """
            CREATE PROCEDURE dbo.obs_dynamic AS
            CREATE TABLE #t (id int NOT NULL PRIMARY KEY, label nvarchar(10) NULL);
            DECLARE c CURSOR LOCAL DYNAMIC FOR SELECT id, label FROM #t ORDER BY id;
            OPEN c;
            INSERT INTO #t VALUES (1, NULL);
            FETCH NEXT FROM c;
            CLOSE c; DEALLOCATE c;
            """,
        ];

        Assert.Equal("id,label,ROWSTAT: 1,null,1", (await Run(setup, "EXEC dbo.obs_dynamic")).ToString());
    }

    [Fact]
    public async Task A_cursor_is_declared_when_its_declare_runs_not_when_the_procedure_is_compiled()
    {
        // Unlike a variable's DECLARE: fetching from a cursor whose DECLARE was in a branch not taken fails (16916).
        string[] setup = [
            Item,
            """
            CREATE PROCEDURE dbo.obs_branch_cursor @a int AS
            IF @a = 1 DECLARE c CURSOR LOCAL FOR SELECT id FROM dbo.obs_item;
            OPEN c; FETCH NEXT FROM c; CLOSE c; DEALLOCATE c;
            """,
        ];

        Assert.Equal("id,ROWSTAT: ", (await Run(setup, "EXEC dbo.obs_branch_cursor 1")).ToString());
        Assert.Equal(16916, (await Run(setup, "EXEC dbo.obs_branch_cursor 0")).Error);
    }

    // ---- .WRITE ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Write_changes_part_of_a_value_in_place_keeping_its_type_and_fails_on_null()
    {
        // On a temporary table, a table variable and a column SELECT … INTO made: part of the value is replaced, a NULL
        // expression cuts it at the offset, a NULL offset appends; a variable set beside it is set.
        Assert.Equal(
            "id,c: 1,aZZ;2,ham;3,Zbc | v: 1 | t: nvarchar(max)",
            (await Run([], """
                SET NOCOUNT ON;
                CREATE TABLE #t (id int NOT NULL, c nvarchar(max) NULL);
                INSERT #t VALUES (1, N'abcdef');
                UPDATE #t SET c.WRITE(N'ZZ', 1, 2);
                UPDATE #t SET c.WRITE(NULL, 3, 0);
                DECLARE @t TABLE (id int NOT NULL, c nvarchar(max) NULL);
                INSERT @t VALUES (2, N'jam');
                UPDATE @t SET c.WRITE(N'h', 0, 1);
                SELECT 3 AS id, CAST(N'abc' AS nvarchar(max)) AS c INTO #s;
                DECLARE @v int;
                UPDATE #s SET c.WRITE(N'Z', 0, 1), @v = 1;
                SELECT id, c FROM #t UNION ALL SELECT id, c FROM @t UNION ALL SELECT id, c FROM #s ORDER BY id;
                SELECT @v AS v;
                SELECT type_name(c.system_type_id) + '(' + IIF(c.max_length = -1, 'max', '?') + ')' AS t FROM tempdb.sys.columns AS c WHERE c.object_id = object_id('tempdb..#s') AND c.name = 'c';
                """)).ToString());

        // A NULL expression and a NULL length cut the value to its first offset characters: '' from 0, whatever it held.
        Assert.Equal("n: 0", (await Run([], "SELECT CAST('abcdef' AS varchar(max)) AS c INTO #w; UPDATE #w SET c.WRITE(NULL, 0, NULL); SELECT LEN(c) AS n FROM #w;")).ToString());
        Assert.Equal("n: 0", (await Run([], "SELECT CAST('abcdef' AS varchar(max)) AS c INTO #w; UPDATE #w SET c.WRITE('', 0, NULL); SELECT LEN(c) AS n FROM #w;")).ToString());

        // On a NULL it is an error (5302), so a column NULL before is still NULL after, never set; on a type that is not max, an
        // error when the procedure is created (258).
        Assert.Equal(5302, (await Run([], "CREATE TABLE #t (c nvarchar(max) NULL); INSERT #t VALUES (NULL); UPDATE #t SET c.WRITE(N'x', 0, 1);")).Error);
        Assert.Equal(258, (await Run([], "CREATE TABLE #u (c nvarchar(10) NULL); INSERT #u VALUES (N'a'); UPDATE #u SET c.WRITE(N'x', 0, 1);")).Error);
    }
}

using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// The keys of a body's own tables: a temporary table's, from its CREATE TABLE and from what later statements add and remove,
// and a table variable's, from its declaration. Each is observed in RefinementObservationTests.TemporaryKeys.cs.
public partial class RefinementTests
{
    /// <summary>The last result's cardinality: of a query that fixes <c>t.id</c>, or what <paramref name="query"/> reads.</summary>
    private static IrCardinality LastRows(string body, string query = "SELECT t.n FROM #t AS t WHERE t.id = @x;") =>
        Results(Procedure($"{body}\n{query}", "@x int, @y int, @c bit"))[^1].Cardinality;

    [Theory]
    [InlineData("id int PRIMARY KEY, n int", IrCardinality.AtMostOne)]
    [InlineData("id int NOT NULL, n int, PRIMARY KEY (id)", IrCardinality.AtMostOne)]
    [InlineData("id int NOT NULL, n int, CONSTRAINT pk_t PRIMARY KEY NONCLUSTERED (id)", IrCardinality.AtMostOne)]
    [InlineData("id int UNIQUE, n int", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, CONSTRAINT uq_t UNIQUE (id)", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, INDEX ux_t UNIQUE (id)", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, INDEX ux_t UNIQUE CLUSTERED (id)", IrCardinality.AtMostOne)]
    [InlineData("id int INDEX ux_t UNIQUE NONCLUSTERED, n int", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, INDEX ux_t UNIQUE (id) WITH (IGNORE_DUP_KEY = ON)", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, INDEX ux_t UNIQUE (id) WHERE id > 0", IrCardinality.Many)]
    [InlineData("id int, n int, INDEX ix_t (id)", IrCardinality.Many)]
    [InlineData("id int IDENTITY, n int", IrCardinality.Many)]
    [InlineData("id int, n int, PRIMARY KEY (id, n)", IrCardinality.Many)]
    [InlineData("id int, n int", IrCardinality.Many)]
    public void A_temporary_table_s_declared_key_fixes_a_query_over_it(string columns, IrCardinality expected) =>
        // A_temporary_table_enforces_the_keys_its_create_table_declares, and a filtered or plain index, an identity or part of a
        // key fixes nothing (A_temporary_table_s_identity_is_no_key).
        Assert.Equal(expected, LastRows($"CREATE TABLE #t ({columns});"));

    [Theory]
    [InlineData("id int PRIMARY KEY, n int", IrCardinality.AtMostOne)]
    [InlineData("id int UNIQUE, n int", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, UNIQUE (id)", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, INDEX ux_t UNIQUE (id)", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, INDEX ux_t UNIQUE NONCLUSTERED (id)", IrCardinality.AtMostOne)]
    [InlineData("id int, n int, INDEX ux_t UNIQUE (id) WHERE id > 0", IrCardinality.Many)]
    [InlineData("id int, n int, PRIMARY KEY (id, n)", IrCardinality.Many)]
    [InlineData("id int, n int", IrCardinality.Many)]
    public void A_table_variable_s_declared_key_fixes_a_query_over_it(string columns, IrCardinality expected) =>
        // A_table_variable_enforces_the_keys_it_declares.
        Assert.Equal(expected, LastRows($"DECLARE @t TABLE ({columns});", "SELECT t.n FROM @t AS t WHERE t.id = @x;"));

    [Theory]
    [InlineData("SELECT t.n FROM #t AS t WHERE t.id = @x AND t.n = @y;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.n FROM #t AS t WHERE t.id = @x OR t.n = @y;", IrCardinality.Many)]
    [InlineData("SELECT t.id FROM #t AS t WHERE t.n IS NULL AND t.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.id FROM #t AS t WHERE t.id = @x AND t.n IS NULL;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.label FROM dbo.u AS u JOIN #t AS t ON t.id = u.t_id AND t.n = 1 WHERE u.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.n FROM #t AS t WITH (NOLOCK) WHERE t.id = @x AND t.n = @y;", IrCardinality.Many)]
    public void A_temporary_table_s_key_is_fixed_by_the_rules_of_a_table_s(string query, IrCardinality expected) =>
        // As a table's: every column of the key, by = or IS NULL (A_unique_constraint_allows_one_null_only), never under OR, and no
        // key under NOLOCK (decision 0016).
        Assert.Equal(expected, LastRows("CREATE TABLE #t (id int NOT NULL, n int NULL, UNIQUE (id, n));", query));

    [Theory]
    [InlineData("SELECT t.n FROM @v AS v JOIN #t AS t ON t.id = v.id WHERE v.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.n FROM @v AS v LEFT JOIN #t AS t ON t.id = v.id WHERE v.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.n FROM #t AS t JOIN #u AS u ON u.code = t.code WHERE t.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.n FROM (SELECT TOP (1) v.id FROM @v AS v) AS d JOIN #t AS t ON t.id = d.id;", IrCardinality.AtMostOne)]
    [InlineData("SELECT t.n FROM @v AS v JOIN #t AS t ON t.id = v.id;", IrCardinality.Many)]
    [InlineData("SELECT t.n FROM @v AS v JOIN #t AS t ON t.id = v.id AND t.id = t.n WHERE v.id = @x;", IrCardinality.AtMostOne)]
    [InlineData("SELECT u.n FROM #t AS t JOIN #u AS u ON u.code = t.ncode WHERE t.id = @x;", IrCardinality.Many)]
    [InlineData("SELECT t.n FROM @v AS v JOIN #t AS t ON t.id = v.id WHERE v.n = @x;", IrCardinality.Many)]
    [InlineData("SELECT u.n FROM (SELECT t.code COLLATE Latin1_General_CI_AS AS v FROM #t AS t WHERE t.id = @x) AS d JOIN #u AS u ON u.code = d.v;", IrCardinality.Many)]
    [InlineData("SELECT u.n FROM (SELECT UPPER(t.code COLLATE Latin1_General_CI_AS) AS v FROM #t AS t WHERE t.id = @x) AS d JOIN #u AS u ON u.code = d.v;", IrCardinality.Many)]
    [InlineData("WITH c AS (SELECT t.code COLLATE Latin1_General_CI_AS AS v FROM #t AS t WHERE t.id = @x) SELECT u.n FROM c JOIN #u AS u ON u.code = c.v;", IrCardinality.Many)]
    [InlineData("SELECT u.n FROM (SELECT p.code COLLATE Latin1_General_CI_AS AS v FROM dbo.t AS p WHERE p.id = @x) AS d JOIN #u AS u ON u.code = d.v;", IrCardinality.Many)]
    [InlineData("SELECT u.n FROM (SELECT t.code AS v FROM #t AS t WHERE t.id = @x) AS d JOIN #u AS u ON u.code = d.v;", IrCardinality.Many)]
    public void A_key_is_fixed_by_a_column_of_a_body_s_table_already_fixed(string query, IrCardinality expected) =>
        // As by a table's column: a column of a table variable, a temporary table or a derived table whose row is fixed is one
        // value. Its type must keep the key's equality: an nvarchar column compared with a varchar key converts the key. A string
        // column of a CTE or derived table may carry a COLLATE that stays explicit outside it, and the comparison is made under
        // it (A_derived_table_s_explicit_collation_compares_a_key_under_it), so only a table's string column fixes a string key.
        Assert.Equal(expected, LastRows("""
            DECLARE @v TABLE (id int PRIMARY KEY, n int);
            CREATE TABLE #t (id int PRIMARY KEY, n int, code varchar(10), ncode nvarchar(10));
            CREATE TABLE #u (code varchar(10) PRIMARY KEY, n int);
            """, query));

    [Theory]
    [InlineData("varchar(10)", "@s varchar(10)", IrCardinality.AtMostOne)]
    [InlineData("varchar(10)", "@s varchar(20)", IrCardinality.AtMostOne)]
    [InlineData("nvarchar(10)", "@s varchar(10)", IrCardinality.AtMostOne)]
    [InlineData("varchar(10)", "@s nvarchar(10)", IrCardinality.Many)]
    [InlineData("varchar(10) COLLATE Latin1_General_BIN2", "@s varchar(10)", IrCardinality.AtMostOne)]
    public void A_temporary_table_s_string_key_is_fixed_whatever_tempdb_s_collation(string type, string parameter, IrCardinality expected)
    {
        // A comparison with a variable or literal is made under the column's own collation, whatever tempdb's is, which is the
        // key's equality, and one with a column of another collation is refused (468): observed in
        // A_temporary_table_s_string_key_compares_under_its_own_collation. A varchar key compared with nvarchar is converted
        // first, as for a table (A_varchar_key_under_a_sql_collation_compared_with_nvarchar_can_match_several_rows).
        var doc = Procedure($"CREATE TABLE #t (code {type} PRIMARY KEY, n int);\nSELECT t.n FROM #t AS t WHERE t.code = @s;", parameter);
        Assert.Equal(expected, Assert.Single(Results(doc)).Cardinality);
    }

    [Theory]
    [InlineData("", IrCardinality.Many)]
    [InlineData("ALTER TABLE #t ADD PRIMARY KEY (id);", IrCardinality.AtMostOne)]
    [InlineData("ALTER TABLE #t ADD CONSTRAINT pk_t PRIMARY KEY CLUSTERED (id);", IrCardinality.AtMostOne)]
    [InlineData("ALTER TABLE #t ADD CONSTRAINT uq_t UNIQUE (id);", IrCardinality.AtMostOne)]
    [InlineData("ALTER TABLE #t WITH NOCHECK ADD UNIQUE (id);", IrCardinality.AtMostOne)]
    [InlineData("ALTER TABLE #t ADD UNIQUE (id, n);", IrCardinality.Many)]
    [InlineData("CREATE UNIQUE INDEX ux_t ON #t (id);", IrCardinality.AtMostOne)]
    [InlineData("CREATE UNIQUE CLUSTERED INDEX ux_t ON #t (id DESC) INCLUDE (n);", IrCardinality.AtMostOne)]
    [InlineData("CREATE UNIQUE NONCLUSTERED INDEX ux_t ON #t (id) WITH (IGNORE_DUP_KEY = ON);", IrCardinality.AtMostOne)]
    [InlineData("CREATE UNIQUE INDEX ux_t ON #t (id) WHERE id > 0;", IrCardinality.Many)]
    [InlineData("CREATE INDEX ix_t ON #t (id);", IrCardinality.Many)]
    [InlineData("CREATE UNIQUE INDEX ux_t ON #other (id);", IrCardinality.Many)]
    [InlineData("ALTER TABLE #other ADD PRIMARY KEY (id);", IrCardinality.Many)]
    [InlineData("ALTER TABLE #t ADD UNIQUE (missing);", IrCardinality.Many)]
    public void A_key_added_to_a_table_select_into_made_holds_from_that_statement_on(string added, IrCardinality expected) =>
        // SELECT … INTO makes no key (A_table_select_into_makes_has_no_key_even_with_an_identity); one added after holds
        // (A_key_added_to_a_temporary_table_holds_from_that_statement_on), and one that can't be added fails the call.
        Assert.Equal(expected, LastRows($"SELECT k.id, k.n INTO #t FROM dbo.u AS x CROSS APPLY (SELECT x.id, x.t_id AS n) AS k;\nCREATE TABLE #other (id int NOT NULL);\n{added}"));

    [Fact]
    public void An_identity_select_into_makes_is_no_key() =>
        // A_table_select_into_makes_has_no_key_even_with_an_identity: SET IDENTITY_INSERT or a reseed writes a value twice.
        Assert.Equal(IrCardinality.Many, LastRows("SELECT IDENTITY(int, 1, 1) AS id, u.t_id AS n INTO #t FROM dbo.u AS u;"));

    [Fact]
    public void A_key_added_after_a_query_does_not_fix_it() =>
        Assert.Equal(
            [IrCardinality.Many, IrCardinality.AtMostOne],
            Results(Procedure("""
                CREATE TABLE #t (id int NOT NULL, n int);
                SELECT t.n FROM #t AS t WHERE t.id = @x;
                ALTER TABLE #t ADD PRIMARY KEY (id);
                SELECT t.n FROM #t AS t WHERE t.id = @x;
                """, "@x int")).Select(r => r.Cardinality));

    [Theory]
    [InlineData("DROP INDEX ux_t ON #t;", IrCardinality.Many)]
    [InlineData("DROP INDEX #t.ux_t;", IrCardinality.Many)]
    [InlineData("DROP INDEX IF EXISTS ux_t ON #t;", IrCardinality.Many)]
    [InlineData("DROP INDEX ix_other ON #t;", IrCardinality.AtMostOne)]
    [InlineData("ALTER INDEX ux_t ON #t DISABLE;", IrCardinality.Many)]
    [InlineData("ALTER INDEX ALL ON #t DISABLE;", IrCardinality.Many)]
    [InlineData("ALTER INDEX ix_other ON #t DISABLE;", IrCardinality.AtMostOne)]
    [InlineData("ALTER INDEX ux_t ON #t DISABLE; ALTER INDEX ux_t ON #t REBUILD;", IrCardinality.AtMostOne)]
    [InlineData("ALTER INDEX ux_t ON #t DISABLE; ALTER INDEX ALL ON #t REBUILD;", IrCardinality.AtMostOne)]
    [InlineData("ALTER INDEX ux_t ON #t DISABLE; ALTER INDEX ix_other ON #t REBUILD;", IrCardinality.Many)]
    [InlineData("ALTER INDEX ux_t ON #t REORGANIZE;", IrCardinality.AtMostOne)]
    [InlineData("ALTER INDEX ux_t ON #t SET (IGNORE_DUP_KEY = ON);", IrCardinality.AtMostOne)]
    [InlineData("CREATE INDEX ux_t ON #t (id) WITH (DROP_EXISTING = ON);", IrCardinality.Many)]
    [InlineData("CREATE UNIQUE INDEX ux_t ON #t (id) WITH (DROP_EXISTING = ON);", IrCardinality.AtMostOne)]
    [InlineData("ALTER TABLE #t DROP COLUMN n;", IrCardinality.AtMostOne)]
    [InlineData("ALTER TABLE #t ALTER COLUMN id bigint NOT NULL;", IrCardinality.Many)]
    [InlineData("TRUNCATE TABLE #t; DELETE FROM #t;", IrCardinality.AtMostOne)]
    [InlineData("DROP TABLE #t; CREATE TABLE #t (id int NOT NULL, n int);", IrCardinality.Many)]
    public void A_unique_index_on_a_temporary_table_holds_until_a_statement_drops_or_disables_it(string change, IrCardinality expected) =>
        // A_temporary_table_s_key_holds_until_it_is_dropped_or_disabled: a disabled index is enabled again by REBUILD, of its
        // name or ALL, and an index of another name changes nothing. A dropped table's keys go with it.
        Assert.Equal(expected, LastRows($"CREATE TABLE #t (id int NOT NULL, n int, INDEX ux_t UNIQUE (id), INDEX ix_other (n));\n{change}"));

    [Theory]
    [InlineData("pk_t", "ALTER TABLE #t DROP CONSTRAINT pk_t;", IrCardinality.Many)]
    [InlineData("pk_t", "ALTER TABLE #t DROP pk_t;", IrCardinality.Many)]
    [InlineData("pk_t", "ALTER TABLE #t DROP CONSTRAINT IF EXISTS pk_t;", IrCardinality.Many)]
    [InlineData("pk_t", "ALTER TABLE #t DROP CONSTRAINT ck_other;", IrCardinality.AtMostOne)]
    [InlineData("pk_t", "ALTER INDEX pk_t ON #t DISABLE;", IrCardinality.Many)]
    [InlineData(null, "ALTER TABLE #t DROP CONSTRAINT ck_other;", IrCardinality.Many)]
    [InlineData(null, "ALTER INDEX ix_other ON #t DISABLE;", IrCardinality.Many)]
    [InlineData(null, "DROP INDEX ix_other ON #t;", IrCardinality.Many)]
    [InlineData(null, "ALTER INDEX ALL ON #t DISABLE; ALTER INDEX ix_other ON #t REBUILD;", IrCardinality.Many)]
    [InlineData(null, "ALTER INDEX ALL ON #t DISABLE; ALTER INDEX ALL ON #t REBUILD;", IrCardinality.AtMostOne)]
    [InlineData("pk_t", "DROP INDEX ix_other ON #t;", IrCardinality.AtMostOne)]
    public void A_key_constraint_on_a_temporary_table_holds_until_a_statement_that_may_name_it_drops_it(string? name, string change, IrCardinality expected)
    {
        // An unnamed key is named by SQL Server, so a constraint or index of a name the body doesn't know may be it. (DROP INDEX
        // can't drop the index of a constraint, 3723, but a key isn't told apart from an index by what it came from.)
        var key = name is null ? "PRIMARY KEY (id)" : $"CONSTRAINT {name} PRIMARY KEY (id)";
        Assert.Equal(expected, LastRows($"CREATE TABLE #t (id int NOT NULL, n int CONSTRAINT ck_other CHECK (n > 0), {key});\n{change}"));
    }

    [Theory]
    [InlineData("IF @c = 1 ALTER TABLE #t ADD PRIMARY KEY (id);", IrCardinality.Many)]
    [InlineData("IF @c = 1 ALTER TABLE #t ADD PRIMARY KEY (id); ELSE ALTER TABLE #t ADD CONSTRAINT uq_t UNIQUE (id);", IrCardinality.AtMostOne)]
    [InlineData("IF @c = 1 RETURN; ALTER TABLE #t ADD PRIMARY KEY (id);", IrCardinality.AtMostOne)]
    [InlineData("ALTER TABLE #t ADD CONSTRAINT pk_t PRIMARY KEY (id); IF @c = 1 ALTER TABLE #t DROP CONSTRAINT pk_t;", IrCardinality.Many)]
    [InlineData("ALTER TABLE #t ADD CONSTRAINT pk_t PRIMARY KEY (id); WHILE @c = 1 BEGIN ALTER INDEX pk_t ON #t DISABLE; SET @c = 0; END", IrCardinality.Many)]
    [InlineData("WHILE @c = 1 BEGIN SET @c = 0; END ALTER TABLE #t ADD PRIMARY KEY (id);", IrCardinality.AtMostOne)]
    [InlineData("BEGIN TRY ALTER TABLE #t ADD PRIMARY KEY (id); END TRY BEGIN CATCH SELECT 1 AS failed; END CATCH", IrCardinality.Many)]
    public void A_key_on_a_temporary_table_holds_after_paths_meet_only_when_every_path_has_it(string paths, IrCardinality expected) =>
        // As for the columns: the join of the paths. An error inside TRY reaches CATCH from the state before the ALTER, so a key
        // that failed to be added isn't there (A_key_that_fails_to_be_added_fails_the_call_or_reaches_catch_without_it).
        Assert.Equal(expected, LastRows($"CREATE TABLE #t (id int NOT NULL, n int);\n{paths}"));

    [Theory]
    [InlineData("INSERT INTO #t (id, n) EXEC dbo.rows;")]
    [InlineData("MERGE #t AS t USING dbo.u AS u ON t.id = u.id WHEN MATCHED THEN UPDATE SET n = u.t_id WHEN NOT MATCHED THEN INSERT (id, n) VALUES (u.id, u.t_id);")]
    [InlineData("INSERT INTO #t (id, n) SELECT u.id, u.t_id FROM dbo.u AS u; UPDATE #t SET id = id + 1;")]
    public void A_write_to_a_keyed_temporary_table_keeps_its_key(string write)
    {
        // A_write_that_would_break_a_temporary_table_s_key_fails: INSERT … EXEC and MERGE included, the whole statement fails.
        var doc = Infer(
            ("tables.sql", Tables),
            ("rows.sql", "CREATE PROCEDURE dbo.rows AS SELECT u.id, u.t_id FROM dbo.u AS u;"),
            ("p.sql", $"CREATE PROCEDURE dbo.p @x int AS\nCREATE TABLE #t (id int PRIMARY KEY, n int);\n{write}\nSELECT t.n FROM #t AS t WHERE t.id = @x;"));
        Assert.Equal(IrCardinality.AtMostOne, Object(doc, "dbo.p").Results![^1].Cardinality);
    }

    [Theory]
    [InlineData("CREATE TABLE #t (id int PRIMARY KEY, n int);", "", IrCardinality.AtMostOne)]
    [InlineData("CREATE TABLE #t (id int NOT NULL, n int);", "", IrCardinality.Many)]
    [InlineData("CREATE TABLE #t (id int NOT NULL, n int);", "ALTER TABLE #t ADD PRIMARY KEY (id);", IrCardinality.AtMostOne)]
    [InlineData("CREATE TABLE #t (id int NOT NULL, n int, INDEX ux_t UNIQUE (id));", "DROP INDEX ux_t ON #t;", IrCardinality.Many)]
    public void A_key_crosses_a_call_as_a_temporary_table_s_columns_do(string created, string inCallee, IrCardinality expected)
    {
        // A callee reads its caller's table with the caller's keys, and a key it adds or drops stays when it returns
        // (A_callee_s_key_on_its_caller_s_temporary_table_stays_when_it_returns). Read alone, the callee knows nothing of it.
        var doc = Infer(
            ("tables.sql", Tables),
            ("callee.sql", $"CREATE PROCEDURE dbo.callee @x int AS\n{inCallee}\nSELECT t.n FROM #t AS t WHERE t.id = @x;"),
            ("p.sql", $"CREATE PROCEDURE dbo.p @x int AS\n{created}\nEXEC dbo.callee @x = @x;\nSELECT t.n AS m FROM #t AS t WHERE t.id = @x;"));
        Assert.Equal([expected, expected], Object(doc, "dbo.p").Results!.Select(r => r.Cardinality));
        Assert.Equal(IrCardinality.Many, Assert.Single(Object(doc, "dbo.callee").Results!).Cardinality);
    }

    [Theory]
    [InlineData("ALTER INDEX ALL ON #t REBUILD;", IrCardinality.Many)]
    [InlineData("DROP INDEX ux_t ON #t; CREATE UNIQUE INDEX ux_t ON #t (id);", IrCardinality.Many)]
    [InlineData("CREATE UNIQUE INDEX ux_t ON #t (id) WITH (DROP_EXISTING = ON);", IrCardinality.Many)]
    [InlineData("DECLARE @k int = 0;", IrCardinality.AtMostOne)]
    public void A_recursive_call_that_may_change_an_index_makes_its_caller_s_table_unknown(string inRecursion, IrCardinality expected)
    {
        // Each statement here leaves the key as it was, so the walk of the body keeps it. But the recursive call isn't followed:
        // what it may write is read from its body, an index statement included, and a table it may change is unknown after it,
        // as a callee's drop stays when it returns (A_callee_s_key_on_its_caller_s_temporary_table_stays_when_it_returns).
        var doc = Infer(
            ("rec.sql", $"""
                CREATE PROCEDURE dbo.rec @n int AS
                DECLARE @m int = @n - 1;
                {inRecursion}
                IF @n > 0 EXEC dbo.rec @n = @m;
                """),
            ("p.sql", "CREATE PROCEDURE dbo.p @x int AS\nCREATE TABLE #t (id int NOT NULL, n int, INDEX ux_t UNIQUE (id));\nEXEC dbo.rec @n = 1;\nSELECT t.n FROM #t AS t WHERE t.id = @x;"));
        Assert.Equal(expected, Assert.Single(Object(doc, "dbo.p").Results!).Cardinality);
    }

    [Theory]
    [InlineData("ALTER TABLE #t DROP CONSTRAINT pk_t;", "INSERT INTO dbo.u (id, label) VALUES (@x, N'a');", IrCardinality.Many)]
    [InlineData("EXEC dbo.drop_its_key;", "UPDATE dbo.u SET label = N'b';", IrCardinality.Many)]
    [InlineData("EXEC dbo.drop_its_key;", "INSERT INTO dbo.u (id, label) VALUES (@x, N'a');", IrCardinality.Many)]
    [InlineData("DROP INDEX ux_t ON #t;", "UPDATE dbo.u SET label = N'b';", IrCardinality.Many)]
    [InlineData("ALTER TABLE #t DROP CONSTRAINT pk_t;", "INSERT INTO dbo.u (id, label) EXEC dbo.rows;", IrCardinality.Many)]
    [InlineData("ALTER TABLE #t DROP CONSTRAINT pk_t;", "DELETE FROM dbo.u;", IrCardinality.AtMostOne)]
    [InlineData("DECLARE @n int = 0;", "INSERT INTO dbo.u (id, label) VALUES (@x, N'a');", IrCardinality.AtMostOne)]
    public void A_trigger_a_write_fires_may_change_the_procedure_s_temporary_table(string inTrigger, string write, IrCardinality expected)
    {
        // A_trigger_changes_the_temporary_table_of_the_procedure_whose_write_fires_it: a trigger runs in the session that fired it,
        // so one that may change #t makes it unknown after the write; one for another statement, or that touches no #t, doesn't.
        var doc = Infer(
            ("tables.sql", Tables),
            ("trigger.sql", $"CREATE TRIGGER dbo.u_changed ON dbo.u AFTER INSERT, UPDATE AS\n{inTrigger}"),
            ("drop_its_key.sql", "CREATE PROCEDURE dbo.drop_its_key AS DROP INDEX ux_t ON #t;"),
            ("rows.sql", "CREATE PROCEDURE dbo.rows AS SELECT 1 AS id, N'a' AS label;"),
            ("p.sql", $"CREATE PROCEDURE dbo.p @x int AS\nCREATE TABLE #t (id int NOT NULL, n int, CONSTRAINT pk_t PRIMARY KEY (id));\n{write}\nSELECT t.n FROM #t AS t WHERE t.id = @x;"));
        Assert.Equal(expected, Object(doc, "dbo.p").Results![^1].Cardinality);
    }

    [Fact]
    public void A_global_temporary_table_claims_no_key() =>
        // Another session may drop and recreate it between two statements (A_global_temporary_table_may_be_replaced_by_another_session),
        // so nothing about it is claimed, its keys included.
        Assert.Equal(IrCardinality.Many, LastRows("", "SELECT t.n FROM ##t AS t WHERE t.id = @x;"));

    [Theory]
    [InlineData("ALTER TABLE #t ADD PRIMARY KEY (id);", false)]
    [InlineData("ALTER TABLE #t ADD CONSTRAINT uq_t UNIQUE (id);", true)]
    [InlineData("CREATE UNIQUE INDEX ux_t ON #t (id);", true)]
    [InlineData("", true)]
    public void A_primary_key_added_to_a_table_select_into_made_makes_its_column_never_null(string added, bool nullable)
    {
        // A primary key can't be added over a column that allows NULL (8111), so once added its column refuses NULL (515), what
        // ever later writes try: A_primary_key_added_to_a_temporary_table_makes_its_column_refuse_null.
        var doc = Procedure($"""
            SELECT u.id, u.t_id AS n INTO #t FROM dbo.u AS u;
            {added}
            UPDATE #t SET id = (SELECT MAX(x.t_id) FROM dbo.u AS x);
            SELECT t.id FROM #t AS t;
            """);
        Assert.Equal(nullable, Field(doc, "id").Nullable);
    }
}

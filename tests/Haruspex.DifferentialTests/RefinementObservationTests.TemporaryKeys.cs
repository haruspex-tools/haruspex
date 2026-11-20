using System.Data.Common;
using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

// The keys SQL Server enforces on a body's own tables: a temporary table's, through every way of making one and every statement
// that adds or removes one, and a table variable's. The rules are KnownTable.Keys and Typer.TemporaryKeys in Inference; the
// Inference README's "A temporary table's keys" gives them.
public partial class RefinementObservationTests
{
    private Task<CallOutcome> Run(string[] setup, string call, bool errorsAsMessages) => ObservedCall.Run(server, setup, call, errorsAsMessages);

    /// <summary>How many rows <c>#t</c> holds after <paramref name="writes"/>, or the error that refused them.</summary>
    private async Task<string> RowsAfter(string table, string writes) =>
        (await Run([], $"{table} {writes} SELECT COUNT(*) AS n FROM #t;")).ToString();

    [Fact]
    public async Task A_temporary_table_enforces_the_keys_its_create_table_declares()
    {
        // The primary key, a unique constraint on a column or the table, and a unique index in the table or on a column, clustered or
        // not, each refuse a second row with the same key; a unique key over a nullable column takes one NULL only.
        Assert.Equal(" ! 2627", await RowsAfter("CREATE TABLE #t (id int PRIMARY KEY, n int);", "INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2627", await RowsAfter("CREATE TABLE #t (id int NOT NULL, n int, CONSTRAINT pk_t PRIMARY KEY NONCLUSTERED (id));", "INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2627", await RowsAfter("CREATE TABLE #t (id int UNIQUE, n int);", "INSERT INTO #t VALUES (NULL, 1), (NULL, 2);"));
        Assert.Equal(" ! 2627", await RowsAfter("CREATE TABLE #t (id int, n int, UNIQUE (id, n));", "INSERT INTO #t VALUES (1, NULL), (1, NULL);"));
        Assert.Equal(" ! 2601", await RowsAfter("CREATE TABLE #t (id int, n int, INDEX ux_t UNIQUE (id));", "INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2601", await RowsAfter("CREATE TABLE #t (id int, n int, INDEX ux_t UNIQUE CLUSTERED (id));", "INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2601", await RowsAfter("CREATE TABLE #t (id int INDEX ux_t UNIQUE NONCLUSTERED, n int);", "INSERT INTO #t VALUES (1, 1), (1, 2);"));

        // IGNORE_DUP_KEY drops the second row and keeps the key; a filtered index, a plain one and part of a key take duplicates.
        Assert.Equal("n: 1", await RowsAfter("CREATE TABLE #t (id int, n int, INDEX ux_t UNIQUE (id) WITH (IGNORE_DUP_KEY = ON));", "INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal("n: 2", await RowsAfter("CREATE TABLE #t (id int, n int, INDEX ux_t UNIQUE (id) WHERE id > 0);", "INSERT INTO #t VALUES (-1, 1), (-1, 2);"));
        Assert.Equal("n: 2", await RowsAfter("CREATE TABLE #t (id int, n int, INDEX ix_t (id));", "INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal("n: 2", await RowsAfter("CREATE TABLE #t (id int, n int, PRIMARY KEY (id, n));", "INSERT INTO #t VALUES (1, 1), (1, 2);"));
    }

    [Fact]
    public async Task A_temporary_table_s_identity_is_no_key()
    {
        // IDENTITY_INSERT, or a reseed, writes a value the column already holds.
        Assert.Equal("n: 2", await RowsAfter("CREATE TABLE #t (id int IDENTITY, n int);", "SET IDENTITY_INSERT #t ON; INSERT INTO #t (id, n) VALUES (1, 1), (1, 2); SET IDENTITY_INSERT #t OFF;"));
        Assert.Equal("n: 2", await RowsAfter("CREATE TABLE #t (id int IDENTITY, n int);", "INSERT INTO #t (n) VALUES (1); DBCC CHECKIDENT ('#t', RESEED, 0) WITH NO_INFOMSGS; INSERT INTO #t (n) VALUES (2);"));
    }

    [Fact]
    public async Task A_table_variable_enforces_the_keys_it_declares()
    {
        static string Declared(string columns) => $"DECLARE @t TABLE ({columns});";
        async Task<string> RowsOf(string columns, string values) =>
            (await Run([], $"{Declared(columns)} INSERT INTO @t VALUES {values}; SELECT COUNT(*) AS n FROM @t;")).ToString();

        Assert.Equal(" ! 2627", await RowsOf("id int PRIMARY KEY, n int", "(1, 1), (1, 2)"));
        Assert.Equal(" ! 2627", await RowsOf("id int UNIQUE, n int", "(NULL, 1), (NULL, 2)"));
        Assert.Equal(" ! 2627", await RowsOf("id int, n int, UNIQUE (id)", "(1, 1), (1, 2)"));
        Assert.Equal(" ! 2601", await RowsOf("id int, n int, INDEX ux_t UNIQUE (id)", "(1, 1), (1, 2)"));
        Assert.Equal(" ! 2601", await RowsOf("id int, n int, INDEX ux_t UNIQUE NONCLUSTERED (id)", "(1, 1), (1, 2)"));
        Assert.Equal("n: 2", await RowsOf("id int, n int, INDEX ux_t UNIQUE (id) WHERE id > 0", "(-1, 1), (-1, 2)"));
        Assert.Equal("n: 2", await RowsOf("id int, n int, PRIMARY KEY (id, n)", "(1, 1), (1, 2)"));
    }

    [Fact]
    public async Task A_table_select_into_makes_has_no_key_even_with_an_identity()
    {
        // SELECT … INTO copies no key, nor does CREATE TABLE … AS SELECT, which SQL Server 2025 takes only with a WITH naming a
        // distribution (156 without one); an identity it copies or makes takes a value twice by IDENTITY_INSERT.
        string[] keyed = ["CREATE TABLE dbo.obs_source (id int PRIMARY KEY, n int); INSERT INTO dbo.obs_source VALUES (1, 1);"];
        Assert.Equal("n: 2", (await Run(keyed, "SELECT id, n INTO #t FROM dbo.obs_source; INSERT INTO #t SELECT id, n FROM dbo.obs_source; SELECT COUNT(*) AS n FROM #t;")).ToString());
        Assert.Equal("n: 2", (await Run(keyed, "CREATE TABLE #t WITH (DISTRIBUTION = ROUND_ROBIN) AS SELECT id, n FROM dbo.obs_source; INSERT INTO #t SELECT id, n FROM dbo.obs_source; SELECT COUNT(*) AS n FROM #t;")).ToString());
        Assert.Equal(156, (await Run(keyed, "CREATE TABLE #t AS SELECT id, n FROM dbo.obs_source;")).Error);
        Assert.Equal(
            "n: 2",
            (await Run([], "SELECT IDENTITY(int, 1, 1) AS id, 5 AS n INTO #t; SET IDENTITY_INSERT #t ON; INSERT INTO #t (id, n) VALUES (1, 6); SELECT COUNT(*) AS n FROM #t;")).ToString());
    }

    [Fact]
    public async Task A_key_added_to_a_temporary_table_holds_from_that_statement_on()
    {
        // ALTER TABLE … ADD a primary key or unique constraint, WITH NOCHECK too, and CREATE UNIQUE [CLUSTERED | NONCLUSTERED] INDEX,
        // each refuse a duplicate from then on; a filtered one and a plain one don't.
        const string Made = "SELECT 1 AS id, 1 AS n INTO #t WHERE 1 = 0;";
        Assert.Equal(" ! 2627", await RowsAfter(Made, "ALTER TABLE #t ADD PRIMARY KEY (id); INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2627", await RowsAfter(Made, "ALTER TABLE #t ADD CONSTRAINT uq_t UNIQUE (id); INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2627", await RowsAfter(Made, "ALTER TABLE #t WITH NOCHECK ADD UNIQUE (id); INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2601", await RowsAfter(Made, "CREATE UNIQUE INDEX ux_t ON #t (id); INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2601", await RowsAfter(Made, "CREATE UNIQUE CLUSTERED INDEX ux_t ON #t (id DESC) WITH (IGNORE_DUP_KEY = OFF); INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal(" ! 2601", await RowsAfter(Made, "CREATE UNIQUE NONCLUSTERED INDEX ux_t ON #t (id) INCLUDE (n); INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal("n: 2", await RowsAfter(Made, "CREATE UNIQUE INDEX ux_t ON #t (id) WHERE id > 1; INSERT INTO #t VALUES (1, 1), (1, 2);"));
        Assert.Equal("n: 2", await RowsAfter(Made, "CREATE INDEX ix_t ON #t (id); INSERT INTO #t VALUES (1, 1), (1, 2);"));
    }

    [Fact]
    public async Task A_key_that_fails_to_be_added_fails_the_call_or_reaches_catch_without_it()
    {
        // Rows already sharing the key refuse it (1505, then 1750), and the error ends the call: what follows in the procedure doesn't
        // run. Inside TRY it reaches CATCH, and the table goes on without the key. A primary key over a column that allows NULL is
        // refused whatever the rows (8111).
        string[] procedures = [
            """
            CREATE PROCEDURE dbo.obs_add_key AS
            BEGIN
                CREATE TABLE #t (id int NOT NULL);
                INSERT INTO #t VALUES (1), (1);
                ALTER TABLE #t ADD PRIMARY KEY (id);
                SELECT COUNT(*) AS n FROM #t;
            END
            """,
            """
            CREATE PROCEDURE dbo.obs_add_index AS
            BEGIN
                CREATE TABLE #t (id int NOT NULL);
                INSERT INTO #t VALUES (1), (1);
                CREATE UNIQUE INDEX ux_t ON #t (id);
                SELECT COUNT(*) AS n FROM #t;
            END
            """,
        ];
        Assert.Equal(" ! 1505", (await Run(procedures, "EXEC dbo.obs_add_key; SELECT 'after' AS step;")).ToString());
        Assert.Equal(" ~ 1505,1750", (await Run(procedures, "EXEC dbo.obs_add_key; SELECT 'after' AS step;", true)).ToString());
        Assert.Equal(" ~ 1505", (await Run(procedures, "EXEC dbo.obs_add_index; SELECT 'after' AS step;", true)).ToString());

        // Caught, outside a transaction: in one, such as the write session's, the error leaves it unable to commit (3930).
        await using var scratch = await server.ScratchAsync();
        await scratch.ExecuteAsync("""
            CREATE PROCEDURE dbo.obs_add_key_caught AS
            BEGIN
                DECLARE @caught int;
                CREATE TABLE #t (id int NOT NULL);
                INSERT INTO #t VALUES (1), (1);
                BEGIN TRY
                    ALTER TABLE #t ADD PRIMARY KEY (id);
                END TRY
                BEGIN CATCH
                    SET @caught = ERROR_NUMBER();
                END CATCH
                INSERT INTO #t VALUES (1);
                SELECT CONCAT(@caught, '|', COUNT(*)) FROM #t;
            END
            """, TestContext.Current.CancellationToken);
        Assert.Equal("1750|3", await Scalar(scratch, "EXEC dbo.obs_add_key_caught;"));
        Assert.Equal(8111, (await Run([], "SELECT CAST(NULL AS int) AS id INTO #t WHERE 1 = 0; ALTER TABLE #t ADD PRIMARY KEY (id);")).Error);
    }

    [Fact]
    public async Task A_primary_key_added_to_a_temporary_table_makes_its_column_refuse_null()
    {
        // The column SELECT … INTO made from a NOT NULL column doesn't allow NULL, so the key goes on, and the column refuses NULL
        // after it (515) as before; a unique key leaves a column that allows NULL as it was.
        string[] source = ["CREATE TABLE dbo.obs_source (id int NOT NULL, n int NULL); INSERT INTO dbo.obs_source VALUES (1, 1);"];
        Assert.Equal(
            "id_nullable: 0 ! 515",
            (await Run(source, """
                SELECT id, n INTO #t FROM dbo.obs_source;
                ALTER TABLE #t ADD PRIMARY KEY (id);
                SELECT CAST(c.is_nullable AS int) AS id_nullable FROM tempdb.sys.columns AS c WHERE c.object_id = OBJECT_ID('tempdb..#t') AND c.name = 'id';
                UPDATE #t SET id = NULL;
                """)).ToString());
        Assert.Equal(
            "n: 1",
            (await Run(source, "SELECT id, n INTO #t FROM dbo.obs_source; ALTER TABLE #t ADD UNIQUE (n); UPDATE #t SET n = NULL; SELECT COUNT(*) AS n FROM #t WHERE n IS NULL;")).ToString());
    }

    [Fact]
    public async Task A_temporary_table_s_key_holds_until_it_is_dropped_or_disabled()
    {
        // DROP INDEX (either form, and IF EXISTS), ALTER TABLE … DROP CONSTRAINT (or DROP without the keyword), a disabled index and
        // an index that replaces it by DROP_EXISTING take duplicates; REBUILD, of the index or ALL, enables it again, and refuses
        // rows written meanwhile that break it (1505).
        const string Indexed = "CREATE TABLE #t (id int NOT NULL, n int, INDEX ux_t UNIQUE NONCLUSTERED (id), CONSTRAINT pk_t PRIMARY KEY (n));";
        const string Twice = "INSERT INTO #t VALUES (1, 1), (1, 2);";
        Assert.Equal("n: 2", await RowsAfter(Indexed, $"DROP INDEX ux_t ON #t; {Twice}"));
        Assert.Equal("n: 2", await RowsAfter(Indexed, $"DROP INDEX #t.ux_t; {Twice}"));
        Assert.Equal("n: 2", await RowsAfter(Indexed, $"DROP INDEX IF EXISTS ux_t ON #t; {Twice}"));
        Assert.Equal("n: 2", await RowsAfter(Indexed, $"ALTER INDEX ux_t ON #t DISABLE; {Twice}"));
        Assert.Equal("n: 2", await RowsAfter(Indexed, $"CREATE INDEX ux_t ON #t (id) WITH (DROP_EXISTING = ON); {Twice}"));
        Assert.Equal(" ! 2601", await RowsAfter(Indexed, $"ALTER INDEX ux_t ON #t DISABLE; ALTER INDEX ux_t ON #t REBUILD; {Twice}"));
        Assert.Equal(" ! 2601", await RowsAfter(Indexed, $"ALTER INDEX ux_t ON #t DISABLE; ALTER INDEX ALL ON #t REBUILD; {Twice}"));
        Assert.Equal(" ! 1505", await RowsAfter(Indexed, $"ALTER INDEX ux_t ON #t DISABLE; {Twice} ALTER INDEX ux_t ON #t REBUILD;"));
        Assert.Equal(" ! 2601", await RowsAfter(Indexed, $"ALTER INDEX ux_t ON #t REORGANIZE; {Twice}"));
        Assert.Equal("n: 2", await RowsAfter(Indexed, "ALTER TABLE #t DROP CONSTRAINT pk_t; INSERT INTO #t VALUES (1, 1), (2, 1);"));
        Assert.Equal("n: 2", await RowsAfter(Indexed, "ALTER TABLE #t DROP pk_t; INSERT INTO #t VALUES (1, 1), (2, 1);"));

        // An index that enforces a constraint can't be dropped by DROP INDEX (3723), and a key's column can't be altered (5074).
        Assert.Equal(3723, (await Run([], $"{Indexed} DROP INDEX pk_t ON #t;")).Error);
        Assert.Equal(5074, (await Run([], $"{Indexed} ALTER TABLE #t ALTER COLUMN id bigint NOT NULL;")).Error);
    }

    [Fact]
    public async Task A_write_that_would_break_a_temporary_table_s_key_fails()
    {
        // The whole statement fails, INSERT … EXEC and MERGE as an INSERT does: no row of it stays.
        string[] setup = [
            "CREATE TABLE dbo.obs_source (id int NOT NULL, n int NULL); INSERT INTO dbo.obs_source VALUES (1, 1), (1, 2);",
            "CREATE PROCEDURE dbo.obs_rows AS SELECT id, n FROM dbo.obs_source;",
        ];
        const string Keyed = "CREATE TABLE #t (id int PRIMARY KEY, n int);";
        Assert.Equal("n: 0", (await Run(setup, $"{Keyed} BEGIN TRY INSERT INTO #t EXEC dbo.obs_rows; END TRY BEGIN CATCH END CATCH SELECT COUNT(*) AS n FROM #t;")).ToString());
        Assert.Equal(2627, (await Run(setup, $"{Keyed} INSERT INTO #t EXEC dbo.obs_rows;")).Error);
        Assert.Equal(
            2627,
            (await Run(setup, $"{Keyed} MERGE #t AS t USING dbo.obs_source AS s ON t.n = s.n WHEN NOT MATCHED THEN INSERT (id, n) VALUES (s.id, s.n);")).Error);
        Assert.Equal(2627, (await Run(setup, "DECLARE @t TABLE (id int PRIMARY KEY, n int); INSERT INTO @t EXEC dbo.obs_rows;")).Error);
    }

    [Fact]
    public async Task A_callee_s_key_on_its_caller_s_temporary_table_stays_when_it_returns()
    {
        // A key a callee adds to its caller's table holds after it returns, and one it drops is gone; the callee reads the table
        // with the keys it has when it is called.
        string[] procedures = [
            "CREATE PROCEDURE dbo.obs_key_it AS ALTER TABLE #t ADD PRIMARY KEY (id);",
            "CREATE PROCEDURE dbo.obs_drop_its_key AS DROP INDEX ux_t ON #t;",
            "CREATE PROCEDURE dbo.obs_write_it AS INSERT INTO #t VALUES (1, 1), (1, 2);",
        ];
        Assert.Equal(" ! 2627", (await Run(procedures, "CREATE TABLE #t (id int NOT NULL, n int); EXEC dbo.obs_key_it; INSERT INTO #t VALUES (1, 1), (1, 2);")).ToString());
        Assert.Equal("n: 2", (await Run(procedures, "CREATE TABLE #t (id int NOT NULL, n int, INDEX ux_t UNIQUE (id)); EXEC dbo.obs_drop_its_key; INSERT INTO #t VALUES (1, 1), (1, 2); SELECT COUNT(*) AS n FROM #t;")).ToString());
        Assert.Equal(" ! 2601", (await Run(procedures, "CREATE TABLE #t (id int NOT NULL, n int, INDEX ux_t UNIQUE (id)); EXEC dbo.obs_write_it;")).ToString());
    }

    [Fact]
    public async Task A_trigger_changes_the_temporary_table_of_the_procedure_whose_write_fires_it()
    {
        // A trigger runs in the session of the statement that fired it, so #t is the procedure's: it can drop its key, and a
        // duplicate then gets in.
        string[] setup = [
            "CREATE TABLE dbo.obs_logged (id int NOT NULL);",
            "CREATE TRIGGER dbo.obs_logged_drops_key ON dbo.obs_logged AFTER INSERT AS IF OBJECT_ID('tempdb..#t') IS NOT NULL ALTER TABLE #t DROP CONSTRAINT pk_t;",
            """
            CREATE PROCEDURE dbo.obs_fires AS
            BEGIN
                CREATE TABLE #t (id int NOT NULL, CONSTRAINT pk_t PRIMARY KEY (id));
                INSERT INTO dbo.obs_logged VALUES (1);
                INSERT INTO #t VALUES (1), (1);
                SELECT COUNT(*) AS n FROM #t;
            END
            """,
        ];
        Assert.Equal("n: 2", (await Run(setup, "EXEC dbo.obs_fires;")).ToString());
    }

    [Fact]
    public async Task A_temporary_table_s_string_column_takes_tempdb_s_collation_and_a_table_variable_s_the_database_s()
    {
        // In a database whose collation isn't the server's: a temporary table's column without COLLATE takes tempdb's, which is the
        // server's, and COLLATE DATABASE_DEFAULT the database's; a table variable's takes the database's.
        await using var scratch = await CaseSensitive();
        Assert.Equal(
            "SQL_Latin1_General_CP1_CI_AS|Latin1_General_100_CS_AS|Latin1_General_100_CS_AS|SQL_Latin1_General_CP1_CI_AS|Latin1_General_100_CS_AS",
            await Scalar(scratch, """
                CREATE TABLE #t (s varchar(10), d varchar(10) COLLATE DATABASE_DEFAULT);
                DECLARE @t TABLE (s varchar(10));
                INSERT INTO @t VALUES ('a');
                SELECT CONCAT_WS('|',
                    (SELECT c.collation_name FROM tempdb.sys.columns AS c WHERE c.object_id = OBJECT_ID('tempdb..#t') AND c.name = 's'),
                    (SELECT c.collation_name FROM tempdb.sys.columns AS c WHERE c.object_id = OBJECT_ID('tempdb..#t') AND c.name = 'd'),
                    (SELECT CAST(SQL_VARIANT_PROPERTY(CAST(s AS sql_variant), 'Collation') AS sysname) FROM @t),
                    CAST(SERVERPROPERTY('Collation') AS sysname),
                    CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS sysname));
                """));
    }

    [Fact]
    public async Task A_temporary_table_s_string_key_compares_under_its_own_collation()
    {
        // Under tempdb's case-insensitive collation 'a' and 'A' are one key value. A variable or literal of the case-sensitive
        // database's collation is compared under the key's, so it matches that one row; a column of the database's collation can't
        // be compared with it at all (468). A table variable's key is the database's, so there 'a' and 'A' are two.
        await using var scratch = await CaseSensitive();
        Assert.Equal(
            "1|1|a",
            await Scalar(scratch, """
                CREATE TABLE #t (code varchar(10) PRIMARY KEY);
                INSERT INTO #t VALUES ('a');
                DECLARE @v varchar(10) = 'A';
                SELECT CONCAT_WS('|', (SELECT COUNT(*) FROM #t WHERE code = @v), (SELECT COUNT(*) FROM #t WHERE code = 'A'), (SELECT code FROM #t WHERE code = @v));
                """));
        Assert.Equal("2627", await Error(scratch, "CREATE TABLE #t (code varchar(10) PRIMARY KEY); INSERT INTO #t VALUES ('a'), ('A');"));
        Assert.Equal("468", await Error(scratch, "CREATE TABLE dbo.box (code varchar(10)); CREATE TABLE #t (code varchar(10) PRIMARY KEY); SELECT t.code FROM #t AS t JOIN dbo.box AS b ON t.code = b.code;"));
        Assert.Equal("2", await Scalar(scratch, "DECLARE @t TABLE (code varchar(10) PRIMARY KEY); INSERT INTO @t VALUES ('a'), ('A'); SELECT COUNT(*) FROM @t;"));
    }

    [Fact]
    public async Task A_derived_table_s_explicit_collation_compares_a_key_under_it()
    {
        // A COLLATE inside a derived table or CTE stays explicit outside it, so key = d.v compares under d.v's collation: a binary
        // key holding 'a' and 'A', compared with a fixed case-insensitive d.v, matches both, from a temporary table or a table. A
        // derived column without COLLATE compares under the key's (one row); a table's column of another collation is refused.
        await using var scratch = await server.ScratchAsync();
        const string Tables = """
            CREATE TABLE #s (v varchar(10) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY);
            CREATE TABLE #k (code varchar(10) COLLATE Latin1_General_BIN NOT NULL PRIMARY KEY, n int NOT NULL);
            INSERT INTO #s VALUES ('a');
            INSERT INTO #k VALUES ('a', 1), ('A', 2);
            """;
        Assert.Equal("2", await Scalar(scratch, $"""
            {Tables}
            SELECT COUNT(*) FROM (SELECT s.v COLLATE Latin1_General_CI_AS AS v FROM #s AS s WHERE s.v = 'a') AS d JOIN #k AS k ON k.code = d.v;
            """));
        Assert.Equal("2", await Scalar(scratch, $"""
            {Tables}
            WITH c AS (SELECT UPPER(s.v COLLATE Latin1_General_CI_AS) AS v FROM #s AS s WHERE s.v = 'a')
            SELECT COUNT(*) FROM c JOIN #k AS k ON k.code = c.v;
            """));
        Assert.Equal("2", await Scalar(scratch, """
            CREATE TABLE dbo.s (v varchar(10) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.k (code varchar(10) COLLATE Latin1_General_BIN NOT NULL PRIMARY KEY, n int NOT NULL);
            INSERT INTO dbo.s VALUES ('a');
            INSERT INTO dbo.k VALUES ('a', 1), ('A', 2);
            SELECT COUNT(*) FROM (SELECT s.v COLLATE Latin1_General_CI_AS AS v FROM dbo.s AS s WHERE s.v = 'a') AS d JOIN dbo.k AS k ON k.code = d.v;
            """));
        Assert.Equal("1", await Scalar(scratch, $"""
            {Tables}
            SELECT COUNT(*) FROM (SELECT 'a' AS v) AS d JOIN #k AS k ON k.code = d.v;
            """));
        Assert.Equal("468", await Error(scratch, $"""
            {Tables}
            SELECT COUNT(*) FROM #s AS s JOIN #k AS k ON k.code = s.v WHERE s.v = 'a';
            """));
    }

    [Fact]
    public async Task A_global_temporary_table_may_be_replaced_by_another_session()
    {
        // Its key holds whoever writes, but between two statements of one session another may drop it and create another of that
        // name, without the key, which the first then reads.
        await using var scratch = await server.ScratchAsync();
        var token = TestContext.Current.CancellationToken;
        var name = "##obs_" + Guid.NewGuid().ToString("N");
        await using var mine = await scratch.OpenAsync(token);
        await using var theirs = await scratch.OpenAsync(token);
        await Execute(mine, $"CREATE TABLE {name} (id int PRIMARY KEY);", token);
        Assert.Equal(2627, (await Assert.ThrowsAsync<SqlException>(() => Execute(theirs, $"INSERT INTO {name} VALUES (1), (1);", token))).Number);
        await Execute(theirs, $"DROP TABLE {name}; CREATE TABLE {name} (id int); INSERT INTO {name} VALUES (1), (1);", token);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var count = new SqlCommand($"SELECT COUNT(*) FROM {name} WHERE id = 1;", mine);
#pragma warning restore CA2100
        Assert.Equal(2, (int)await count.ExecuteScalarAsync(token));
        await Execute(theirs, $"DROP TABLE {name};", token);
    }

    /// <summary>A database of the test's own in a case-sensitive collation, which the test server's isn't.</summary>
    private async Task<ScratchDatabase> CaseSensitive()
    {
        var scratch = await server.ScratchAsync();
        await scratch.ExecuteAsync("ALTER DATABASE CURRENT COLLATE Latin1_General_100_CS_AS;", TestContext.Current.CancellationToken);
        return scratch;
    }

    private static async Task<string> Scalar(ScratchDatabase database, string batch)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await database.OpenAsync(token);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var command = new SqlCommand(batch, connection);
#pragma warning restore CA2100
        return Convert.ToString(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) ?? "null";
    }

    private static async Task<string> Error(ScratchDatabase database, string batch)
    {
        try
        {
            await database.ExecuteAsync(batch, TestContext.Current.CancellationToken);
            return "none";
        }
        catch (SqlException e)
        {
            return e.Number.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static async Task Execute(DbConnection connection, string batch, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Observation tests' own batches.
        command.CommandText = batch;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(token);
    }
}

using Haruspex.DifferentialTests.Server;
using Haruspex.SqlServer.Model;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server enforces for types, alias types and CHECK constraints, observed and kept as tests. Semantics'
/// reading of the schema (SchemaRules, CheckReader) and the Zod emitter's mappings follow what these say; several
/// are why a fact is weaker than it first looks. Each runs in a write session, alone in the test database and rolled
/// back (whose collation is the server's default, SQL_Latin1_General_CP1_CI_AS on the pinned image).
/// </summary>
public class CheckConstraintObservationTests(SqlServerFixture server)
{
    /// <summary>Runs <paramref name="setup"/>, then returns the first column of the first row of <paramref name="query"/>; rolled back.</summary>
    private async Task<object?> Query(string setup, string query)
    {
        server.Require();
        // It creates a table and writes to it, so it is alone in the database while it does (README.md).
        await using var session = await server.WriteAsync(TestContext.Current.CancellationToken);
        await using (var command = session.Command(setup))
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await using var select = session.Command(query);
        var value = await select.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull ? null : value;
    }

    /// <summary>Whether <paramref name="statement"/> succeeds after <paramref name="setup"/>; rolled back.</summary>
    private async Task<bool> Succeeds(string setup, string statement)
    {
        try
        {
            await Query(setup, statement + "; SELECT 1;");
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    // ---- strings ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_string_check_admits_another_case_and_trailing_spaces()
    {
        // So a CHECK's strings are not an exact set: the IR says "collation", and the Zod is not an enum.
        var json = await Query(
            "CREATE TABLE dbo.obs (s varchar(10) NULL CHECK (s IN ('open', 'closed'))); INSERT INTO dbo.obs VALUES ('OPEN'), ('open   '), ('Closed');",
            "SELECT s FROM dbo.obs FOR JSON PATH;");

        Assert.Equal("""[{"s":"OPEN"},{"s":"open   "},{"s":"Closed"}]""", json);
    }

    [Theory]
    // A COLLATE in the condition decides the comparison, whatever the column's collation: so CheckReader gives a set compared
    // so no exact, padded or canonical claim. A case-insensitive COLLATE on a literal, on the column, and in an OR of
    // comparisons, each under a case-sensitive column, stores 'OPEN'; so does one under a binary column.
    [InlineData("Latin1_General_100_CS_AS", "s IN ('open' COLLATE Latin1_General_100_CI_AS, 'shut')", "OPEN", true)]
    [InlineData("Latin1_General_100_CS_AS", "s COLLATE Latin1_General_100_CI_AS IN ('open', 'shut')", "OPEN", true)]
    [InlineData("Latin1_General_100_CS_AS", "s = 'open' COLLATE Latin1_General_100_CI_AS OR s = 'shut'", "OPEN", true)]
    [InlineData("Latin1_General_100_BIN2", "s IN ('open' COLLATE Latin1_General_100_CI_AS, 'shut')", "OPEN", true)]
    // And the reverse: a case-sensitive COLLATE under a case-insensitive column refuses what the column alone would store.
    [InlineData("Latin1_General_100_CI_AS", "s IN ('open' COLLATE Latin1_General_100_CS_AS, 'shut')", "OPEN", false)]
    public async Task A_collate_in_a_check_decides_how_it_compares(string column, string condition, string value, bool stored) =>
        Assert.Equal(stored, await Succeeds($"CREATE TABLE dbo.obs (s varchar(10) COLLATE {column} NULL CHECK ({condition}));", $"INSERT INTO dbo.obs VALUES ('{value}')"));

    [Fact]
    public async Task The_collations_haruspex_lists_are_exactly_the_servers_with_their_code_pages()
    {
        // Collations is a closed list, so that a name off it is unknown and never read as single-byte: every name the server has
        // must be on it with the server's code page, and nothing else.
        server.Require();
        await using var session = await server.ReadAsync(TestContext.Current.CancellationToken);
        await using var command = session.Command("SELECT name, CAST(COLLATIONPROPERTY(name, 'CodePage') AS int) FROM sys.fn_helpcollations();");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var observed = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            observed.Add(reader.GetString(0), reader.GetInt32(1));

        Assert.Equal(
            observed.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key} {c.Value}"),
            CollationCatalogue.Names.Order(StringComparer.Ordinal).Select(n => $"{n} {CollationCatalogue.CodePage(n)}"));
    }

    [Fact]
    public async Task A_unicode_only_collation_is_no_databases_collation()
    {
        // A collation of no code page (Indic_General_100_CI_AS, Assamese_100_CI_AS) holds only nvarchar: CREATE DATABASE refuses
        // each with Msg 453, so the config refuses one as the database's collation. Each is tried, and none is ever made.
        server.Require();
        var token = TestContext.Current.CancellationToken;
        await using var scratch = await server.ScratchAsync();
        await using var connection = await scratch.OpenAsync(token);
        await using var command = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @tried TABLE (name sysname, error int);
            DECLARE @name sysname, @error int;
            DECLARE each CURSOR LOCAL FAST_FORWARD FOR
                SELECT name FROM sys.fn_helpcollations() WHERE COLLATIONPROPERTY(name, 'CodePage') = 0;
            OPEN each;
            FETCH each INTO @name;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @error = 0;
                BEGIN TRY
                    EXEC (N'CREATE DATABASE haruspex_test_unicode_only COLLATE ' + @name);
                    EXEC (N'DROP DATABASE haruspex_test_unicode_only');
                END TRY
                BEGIN CATCH
                    SET @error = ERROR_NUMBER();
                END CATCH;
                INSERT @tried VALUES (@name, @error);
                FETCH each INTO @name;
            END;
            SELECT name, error FROM @tried;
            """, connection) { CommandTimeout = 300 };
        await using var reader = await command.ExecuteReaderAsync(token);
        var tried = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
            tried.Add(reader.GetString(0), reader.GetInt32(1));

        Assert.Equal(
            CollationCatalogue.Names.Where(n => CollationCatalogue.CodePage(n) == CollationCatalogue.UnicodeOnly).Order(StringComparer.Ordinal),
            tried.Keys.Order(StringComparer.Ordinal));
        Assert.All(tried, t => Assert.Equal(453, t.Value));
    }

    [Theory]
    // An N'…' literal outranks varchar, so a code-page column is converted to Unicode and compared by the collation's Unicode
    // rules, which are not its code-page rules: under SQL_Latin1_General_CP1_CI_AS 'v²' equals N'v2', a NUL is ignored, and 'ß'
    // equals N'ss'. The same strings without N refuse each. So CheckReader gives such a set no canonical fold. Under a binary
    // collation the code points are compared, and 'v²' is refused either way.
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "N'v1', N'v2'", "'v' + CHAR(178)", true)]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "N'v1', N'v2'", "'v' + CHAR(0) + '1'", true)]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "N'pass', N'fail'", "'pa' + CHAR(223)", true)]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "'v1', 'v2'", "'v' + CHAR(178)", false)]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "'v1', 'v2'", "'v' + CHAR(0) + '1'", false)]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "'pass', 'fail'", "'pa' + CHAR(223)", false)]
    [InlineData("Latin1_General_100_BIN2", "N'v1', N'v2'", "'v' + CHAR(178)", false)]
    public async Task A_national_literal_compares_a_code_page_column_by_unicode_rules(string collation, string items, string value, bool stored) =>
        Assert.Equal(stored, await Succeeds($"CREATE TABLE dbo.obs (s varchar(10) COLLATE {collation} NULL CHECK (s IN ({items})));", $"INSERT INTO dbo.obs VALUES ({value})"));

    [Theory]
    // A literal without N is varchar in the database's code page (1252 here) before it is compared, whatever the column: 'Ω' and
    // 'Ā' become 'O' and 'A', so a Unicode or UTF-8 column under a binary collation refuses the character as written and stores
    // the one it became (sys.check_constraints keeps [s]='O'). With N it is compared as written.
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN2", "'Ω'", "N'Ω'", false)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN2", "'Ω'", "N'O'", true)]
    [InlineData("varchar(20) COLLATE Latin1_General_100_BIN2_UTF8", "'Ā'", "N'Ā'", false)]
    [InlineData("varchar(20) COLLATE Latin1_General_100_BIN2_UTF8", "'Ā'", "'A'", true)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN2", "N'Ω'", "N'Ω'", true)]
    public async Task A_literal_without_n_is_converted_to_the_databases_code_page_before_it_is_compared(string column, string item, string value, bool stored) =>
        Assert.Equal(stored, await Succeeds($"CREATE TABLE dbo.obs (s {column} NULL CHECK (s IN ({item}, 'ok')));", $"INSERT INTO dbo.obs VALUES ({value})"));

    [Theory]
    // LEN counts a surrogate pair as one character of nvarchar under _SC or a version 140 collation that isn't binary, and of
    // varchar under _UTF8, binary too; as two otherwise (a code page has no such character, so varchar stores it as two). So a
    // CHECK on LEN bounds code points only there.
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_CI_AS_SC", true)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_CI_AS_SC_UTF8", true)]
    [InlineData("nvarchar(10) COLLATE Japanese_XJIS_140_CI_AS", true)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CI_AS_SC_UTF8", true)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2_UTF8", true)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN2_UTF8", false)]
    [InlineData("nvarchar(10) COLLATE Japanese_XJIS_140_BIN", false)]
    [InlineData("nvarchar(10) COLLATE Japanese_XJIS_140_BIN2", false)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CI_AS_SC", false)]
    [InlineData("varchar(10) COLLATE Japanese_XJIS_140_CI_AS", false)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_CI_AS", false)]
    public async Task Len_counts_a_surrogate_pair_as_one_only_under_a_supplementary_character_collation(string column, bool asOne)
    {
        var setup = $"CREATE TABLE dbo.obs_len (c {column} NULL CHECK (LEN(c) >= 2));";

        // Two emoji are two characters under either; one emoji is one only under a supplementary-character collation.
        Assert.True(await Succeeds(setup, "INSERT INTO dbo.obs_len (c) VALUES (NCHAR(55357) + NCHAR(56832) + NCHAR(55357) + NCHAR(56832))"));
        Assert.Equal(!asOne, await Succeeds(setup, "INSERT INTO dbo.obs_len (c) VALUES (NCHAR(55357) + NCHAR(56832))"));
        var one = Convert.ToInt32(await Query($"CREATE TABLE dbo.obs_len (c {column} NULL); INSERT INTO dbo.obs_len (c) VALUES (NCHAR(55357) + NCHAR(56832));", "SELECT LEN(c) FROM dbo.obs_len;"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(asOne ? 1 : 2, one);
    }

    [Fact]
    public async Task A_binary_collation_still_ignores_trailing_spaces()
    {
        const string table = "CREATE TABLE dbo.obs (s varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (s IN ('open')));";

        Assert.True(await Succeeds(table, "INSERT INTO dbo.obs VALUES ('open  ')"));
        Assert.False(await Succeeds(table, "INSERT INTO dbo.obs VALUES ('OPEN')"));
    }

    [Fact]
    public async Task Len_and_not_empty_ignore_trailing_spaces_so_they_refuse_a_value_of_spaces()
    {
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (s nvarchar(10) NULL CHECK (len(s) > 0));", "INSERT INTO dbo.obs VALUES (N'   ')"));
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (s nvarchar(10) NULL CHECK (s <> ''));", "INSERT INTO dbo.obs VALUES (N'   ')"));
        Assert.True(await Succeeds("CREATE TABLE dbo.obs (s nvarchar(10) NULL CHECK (s <> ''));", "INSERT INTO dbo.obs VALUES (N' x')"));
    }

    [Fact]
    public async Task Char_in_a_utf8_collation_pads_to_n_bytes_not_n_characters()
    {
        // So char(n)'s .length(n) holds only in a single-byte collation.
        var json = await Query(
            "CREATE TABLE dbo.obs (c char(3) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL); INSERT INTO dbo.obs VALUES (N'é');",
            "SELECT c FROM dbo.obs FOR JSON PATH;");

        Assert.Equal("""[{"c":"é "}]""", json);
    }

    [Fact]
    public async Task A_nullable_char_column_created_with_ansi_padding_off_cuts_trailing_spaces_but_nchar_and_not_null_pad()
    {
        // The setting is the one in force when a table is created: a script's own SET before its CREATE TABLE. Under it a
        // nullable char(n) column keeps its values with their trailing spaces cut, in a single-byte, a UTF-8 and a double-byte
        // collation alike, and an empty string or one of spaces keeps one space; nchar(n), and a column that is never null, pad.
        var outcome = await ObservedCall.Run(
            server,
            [
                """
                SET ANSI_PADDING OFF;
                CREATE TABLE dbo.obs_pad (
                    c char(4) COLLATE Latin1_General_100_CI_AS NULL, kept char(4) COLLATE Latin1_General_100_CI_AS NOT NULL,
                    n nchar(4) NULL, u char(4) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL, d char(4) COLLATE Chinese_PRC_CI_AS NULL);
                SET ANSI_PADDING ON;
                """,
            ],
            """
            INSERT INTO dbo.obs_pad VALUES ('a', 'a', N'a', 'a', 'a'), ('', '', N'', '', '');
            SELECT DATALENGTH(c) AS c, DATALENGTH(kept) AS kept, DATALENGTH(n) AS n, DATALENGTH(u) AS u, DATALENGTH(d) AS d FROM dbo.obs_pad;
            """);

        Assert.Equal("c,kept,n,u,d: 1,4,8,1,1;1,4,8,1,1", outcome.ToString());
    }

    [Fact]
    public async Task A_table_type_created_with_ansi_padding_off_pads_its_nullable_char_and_binary_columns()
    {
        // Its columns report is_ansi_padded whatever the setting. Created and committed first, in a database of its own: a type
        // created in the transaction that declares it deadlocks (1205, observed).
        var token = TestContext.Current.CancellationToken;
        await using var database = await server.ScratchAsync();
        await database.ExecuteAsync("SET ANSI_PADDING OFF; EXEC (N'CREATE TYPE dbo.obs_pad_list AS TABLE (c char(4) NULL, b binary(4) NULL)'); SET ANSI_PADDING ON;", token);
        await using var connection = await database.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SET ANSI_PADDING OFF; DECLARE @listed dbo.obs_pad_list; INSERT INTO @listed VALUES ('a', 0x01); SELECT CONCAT(DATALENGTH(c), ',', DATALENGTH(b)) FROM @listed;";

        Assert.Equal("4,4", await command.ExecuteScalarAsync(token));
    }

    [Fact]
    public async Task A_nullable_char_column_a_body_creates_is_cut_under_its_callers_ansi_padding_off_but_a_table_variable_s_pads()
    {
        // A temporary table, made by CREATE TABLE or by SELECT … INTO, takes the caller's session's setting; a table variable's
        // column pads whatever it is, and so does the table a multi-statement function returns, created under it too.
        var outcome = await ObservedCall.Run(
            server,
            [
                "SET ANSI_PADDING OFF; EXEC (N'CREATE FUNCTION dbo.obs_returned() RETURNS @r TABLE (c char(4) NULL) AS BEGIN INSERT INTO @r VALUES (''a''); RETURN; END'); SET ANSI_PADDING ON;",
                """
                CREATE PROCEDURE dbo.obs_padded AS
                CREATE TABLE #c (c char(4) NULL);
                INSERT INTO #c VALUES ('a');
                SELECT CAST(CASE WHEN 1 = 1 THEN 'a' END AS char(4)) AS c INTO #t;
                DECLARE @v TABLE (c char(4) NULL);
                INSERT INTO @v VALUES ('a');
                SELECT (SELECT DATALENGTH(c) FROM #c) AS created, (SELECT DATALENGTH(c) FROM #t) AS made, (SELECT DATALENGTH(c) FROM @v) AS variable,
                       (SELECT DATALENGTH(c) FROM dbo.obs_returned()) AS returned;
                """,
            ],
            "SET ANSI_PADDING OFF; EXEC dbo.obs_padded; SET ANSI_PADDING ON; EXEC dbo.obs_padded;");

        Assert.Equal("created,made,variable,returned: 1,1,4,4 | created,made,variable,returned: 4,4,4,4", outcome.ToString());
    }

    [Fact]
    public async Task Varchar_in_a_utf8_collation_counts_bytes()
    {
        const string table = "CREATE TABLE dbo.obs (c varchar(3) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL);";

        Assert.True(await Succeeds(table, "INSERT INTO dbo.obs VALUES (N'éa')"));
        Assert.False(await Succeeds(table, "INSERT INTO dbo.obs VALUES (N'éé')"));
    }

    // ---- numbers ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_money_column_compares_with_a_literal_of_more_decimal_places_exactly()
    {
        // The literal is not rounded to money first: >= 1.00004 refuses 1.0000, and <= 1.00006 refuses 1.0001.
        // CheckReader still reads only literals that fit the column (see its remarks); this pins what the engine
        // does today, so that the caution there is known to be caution.
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (m money NULL CHECK (m >= 1.00004));", "INSERT INTO dbo.obs VALUES (1.0000)"));
        Assert.False(await Succeeds("CREATE TABLE dbo.obs (m money NULL CHECK (m <= 1.00006));", "INSERT INTO dbo.obs VALUES (1.0001)"));
    }

    // ---- trust --------------------------------------------------------------------------------------

    private const string Trusted = "CREATE TABLE dbo.obs (i int NULL CONSTRAINT ck_obs CHECK (i > 0));";

    private Task<object?> NotTrusted(string setup) =>
        Query(Trusted + setup, "SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'ck_obs';");

    [Fact]
    public async Task A_constraint_disabled_by_nocheck_checks_no_row_and_is_not_trusted()
    {
        Assert.True(await Succeeds(Trusted + " ALTER TABLE dbo.obs NOCHECK CONSTRAINT ck_obs;", "INSERT INTO dbo.obs VALUES (-5)"));
        Assert.Equal(true, await NotTrusted(" ALTER TABLE dbo.obs NOCHECK CONSTRAINT ck_obs;"));
    }

    [Fact]
    public async Task A_plain_check_constraint_re_enables_without_validating_so_the_constraint_stays_untrusted() =>
        Assert.Equal(true, await NotTrusted(" ALTER TABLE dbo.obs NOCHECK CONSTRAINT ck_obs; INSERT INTO dbo.obs VALUES (-5); ALTER TABLE dbo.obs CHECK CONSTRAINT ck_obs;"));

    [Fact]
    public async Task With_check_check_constraint_validates_every_row_so_afterwards_it_holds()
    {
        Assert.Equal(false, await NotTrusted(" ALTER TABLE dbo.obs NOCHECK CONSTRAINT ck_obs; ALTER TABLE dbo.obs WITH CHECK CHECK CONSTRAINT ck_obs;"));
        Assert.False(await Succeeds(Trusted + " ALTER TABLE dbo.obs NOCHECK CONSTRAINT ck_obs; INSERT INTO dbo.obs VALUES (-5);", "ALTER TABLE dbo.obs WITH CHECK CHECK CONSTRAINT ck_obs"));
    }

    [Fact]
    public async Task A_constraint_added_with_nocheck_leaves_existing_rows_that_break_it()
    {
        const string late = "CREATE TABLE dbo.obs (i int NULL); INSERT INTO dbo.obs VALUES (-5); ALTER TABLE dbo.obs WITH NOCHECK ADD CONSTRAINT ck_obs CHECK (i > 0);";

        Assert.Equal(-5, await Query(late, "SELECT MIN(i) FROM dbo.obs;"));
        Assert.Equal(true, await Query(late, "SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'ck_obs';"));
    }

    // ---- bulk loads ---------------------------------------------------------------------------------

    /// <summary>A table whose one check refuses any word but two, which a file the server can read (its host name) breaks.</summary>
    private const string Words = "CREATE TABLE dbo.obs (word varchar(200) NULL CONSTRAINT ck_obs CHECK (word IN ('A', 'B')));";

    [Theory]
    // BULK INSERT and an INSERT from OPENROWSET(BULK) WITH (IGNORE_CONSTRAINTS) check no row, store one that breaks the check, and
    // leave it not trusted, as NOCHECK does; so does INSERT BULK, the statement a bulk copy sends (below). So WeakeningStatements
    // reads each as NOCHECK CONSTRAINT ALL on its table.
    [InlineData("BULK INSERT dbo.obs FROM '/etc/hostname';")]
    [InlineData("INSERT INTO dbo.obs WITH (IGNORE_CONSTRAINTS) (word) SELECT BulkColumn FROM OPENROWSET(BULK '/etc/hostname', SINGLE_CLOB) AS f;")]
    public async Task A_bulk_load_without_check_constraints_writes_rows_no_check_has_seen(string load)
    {
        Assert.Equal(1, await Query(Words + load, "SELECT COUNT(*) FROM dbo.obs WHERE word NOT IN ('A', 'B');"));
        Assert.Equal(true, await Query(Words + load, "SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'ck_obs';"));
    }

    [Theory]
    // With CHECK_CONSTRAINTS, or from OPENROWSET(BULK) without the hint, each row is checked as any insert's is, and refused.
    [InlineData("BULK INSERT dbo.obs FROM '/etc/hostname' WITH (CHECK_CONSTRAINTS)")]
    [InlineData("INSERT INTO dbo.obs (word) SELECT BulkColumn FROM OPENROWSET(BULK '/etc/hostname', SINGLE_CLOB) AS f")]
    public async Task A_bulk_load_that_checks_constraints_refuses_a_row_that_breaks_one(string load) =>
        Assert.False(await Succeeds(Words, load));

    [Theory]
    [InlineData(SqlBulkCopyOptions.Default, true)]
    [InlineData(SqlBulkCopyOptions.CheckConstraints, false)]
    public async Task A_bulk_copy_checks_no_row_unless_asked_and_leaves_the_check_not_trusted(SqlBulkCopyOptions options, bool stored)
    {
        server.Require();
        await using var session = await server.WriteAsync(TestContext.Current.CancellationToken);
        await using (var create = session.Command(Words))
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        using var rows = new System.Data.DataTable();
        rows.Columns.Add("word", typeof(string));
        rows.Rows.Add("Z");
        using var copy = session.BulkCopy(options);
        copy.DestinationTableName = "dbo.obs";
        var copied = true;
        try
        {
            await copy.WriteToServerAsync(rows, TestContext.Current.CancellationToken);
        }
        catch (SqlException)
        {
            copied = false;
        }

        Assert.Equal(stored, copied);
        if (stored)
        {
            await using var trusted = session.Command("SELECT is_not_trusted FROM sys.check_constraints WHERE name = 'ck_obs';");
            Assert.Equal(true, await trusted.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
    }

    // ---- statements at any depth --------------------------------------------------------------------

    [Fact]
    public async Task A_column_loosened_under_if_holds_null()
    {
        // The guarded migration: whether the IF holds isn't known from the source, and when it does the column holds NULL, which
        // a claim from its CREATE TABLE would refuse. So WeakeningStatements reads an ALTER TABLE at any depth as a change.
        const string setup = "CREATE TABLE dbo.obs (id int NOT NULL, cut varchar(10) NOT NULL);"
            + " IF COL_LENGTH('dbo.obs', 'cut') IS NOT NULL ALTER TABLE dbo.obs ALTER COLUMN cut varchar(10) NULL;"
            + " INSERT INTO dbo.obs VALUES (1, NULL);";

        Assert.Equal(1, await Query(setup, "SELECT COUNT(*) FROM dbo.obs WHERE cut IS NULL;"));
    }

    // ---- null ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_check_passes_null() => Assert.True(await Succeeds("CREATE TABLE dbo.obs (i int NULL CHECK (i IN (1, 2)), s varchar(5) NULL CHECK (len(s) > 0));", "INSERT INTO dbo.obs VALUES (NULL, NULL)"));

    [Fact]
    public async Task A_null_in_an_in_list_makes_the_check_pass_anything() => Assert.True(await Succeeds("CREATE TABLE dbo.obs (i int NULL CHECK (i IN (1, NULL)));", "INSERT INTO dbo.obs VALUES (5)"));

    [Theory]
    [InlineData("CREATE TABLE dbo.obs (a int NULL NOT NULL);")]
    [InlineData("CREATE TABLE dbo.obs (a int NOT NULL NULL);")]
    [InlineData("CREATE TABLE dbo.obs (b int NULL); ALTER TABLE dbo.obs ADD a int NULL NOT NULL;")]
    [InlineData("DECLARE @t TABLE (a int NULL NOT NULL);")]
    [InlineData("CREATE TABLE #obs (a int NOT NULL NULL);")]
    [InlineData("CREATE TYPE dbo.obs_t AS TABLE (a int NULL NOT NULL);")]
    [InlineData("CREATE FUNCTION dbo.obs_f() RETURNS @r TABLE (a int NULL NOT NULL) AS BEGIN RETURN; END;")]
    // A column declared both NULL and NOT NULL, in either order, is refused wherever a table is declared (Msg 8150, "Multiple
    // NULL constraints"), so how SyntaxReader.Table reads one describes no table that exists.
    public async Task A_column_declared_both_null_and_not_null_is_refused(string declaration) =>
        Assert.Equal(8150, (await ObservedCall.Run(server, [], declaration)).Error);

    /// <summary>
    /// Nullable columns whose set, bound and minimum length are each spelt with <c>OR col IS NULL</c>, and one whose
    /// <c>IS NULL</c> is of an expression of it, not of the column.
    /// </summary>
    private const string OrIsNull = """
        CREATE TABLE dbo.obs (
            status varchar(10) NULL CHECK (status IS NULL OR status IN ('open', 'shut')),
            n int NULL CHECK (n > 0 OR n IS NULL),
            s varchar(5) NULL CHECK (len(s) > 0 OR s IS NULL),
            m int NULL CHECK (m IN (1, 2) OR NULLIF(m, 5) IS NULL));
        """;

    [Theory]
    [InlineData("INSERT INTO dbo.obs (status, n, s) VALUES (NULL, NULL, NULL)", true)]
    [InlineData("INSERT INTO dbo.obs (status, n, s) VALUES ('open', 1, 'a')", true)]
    [InlineData("INSERT INTO dbo.obs (status) VALUES ('other')", false)]
    [InlineData("INSERT INTO dbo.obs (n) VALUES (0)", false)]
    [InlineData("INSERT INTO dbo.obs (s) VALUES ('')", false)]
    [InlineData("INSERT INTO dbo.obs (m) VALUES (5)", true)]
    // col IS NULL is FALSE for a non-null value, so the other side alone decides it: CheckReader reads it so. An expression's
    // IS NULL is not: NULLIF(m, 5) is NULL for 5, which is stored.
    public async Task An_in_list_or_is_null_check_refuses_a_value_outside_the_set(string insert, bool stored) =>
        Assert.Equal(stored, await Succeeds(OrIsNull, insert));

    // ---- alias types --------------------------------------------------------------------------------

    [Theory]
    [InlineData("ON", "c chk.short_code", false)]
    [InlineData("OFF", "c chk.short_code", false)]
    [InlineData("ON", "c chk.short_code NULL", true)]
    [InlineData("ON", "c sysname", false)]
    [InlineData("ON", "c sysname NULL", true)]
    [InlineData("ON", "c chk.pct", true)]
    public async Task A_column_that_declares_no_nullability_takes_its_alias_types_whatever_the_session_default(string ansiNullDefault, string column, bool nullable)
    {
        var isNullable = await Query(
            $"SET ANSI_NULL_DFLT_ON {ansiNullDefault}; CREATE TABLE dbo.obs ({column});",
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.obs');");

        Assert.Equal(nullable, isNullable);
    }
}

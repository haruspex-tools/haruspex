using System.Data;
using System.Globalization;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What <c>CONTAINSTABLE</c> and <c>FREETEXTTABLE</c> return, observed on a server with full-text search
/// (<see cref="FullTextServer"/>), each test in a database of its own holding the <c>glossary</c> fixture, seeded. The rules
/// are applied in <c>Typer.FullText.cs</c> and tested in <c>FullTextTests</c>.
/// </summary>
public class FullTextObservationTests
{
    /// <summary>
    /// The glossary's rows: entries a search finds and one it doesn't, a note with no entry, labels, and an archived entry whose
    /// key is two accented letters, four bytes of UTF-8 in its char(8), so 6 characters with its padding.
    /// </summary>
    private const string Seed = """
        INSERT lex.entry (id, headword, definition) VALUES (1, N'oak', N'a wooden tree'), (2, N'ash', N'a wooden tree too'), (3, N'iron', NULL);
        INSERT lex.note (code, entry_id, body) VALUES (N'N1', 1, N'wooden furniture'), (N'N2', NULL, N'wooden boxes'), (N'N3', 2, N'metal');
        INSERT lex.label (ref, words) VALUES ('L1', N'wooden label'), ('L2', N'paper label');
        INSERT archive.entry (code, headword) VALUES (N'éé', N'wooden chest'), ('A2', N'iron chest');
        """;

    /// <summary>The glossary, deployed and seeded in a database of the test's own, once its full-text indexes hold every row.</summary>
    private static async Task<ScratchDatabase> GlossaryAsync(CancellationToken token)
    {
        var database = await FullTextServer.ScratchAsync();
        try
        {
            await database.DeployAsync(Fixture.Named("glossary").Database);
            await database.ExecuteAsync(Seed, token);
            await PopulatedAsync(database, token);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Waits until every full-text index has taken in every change: a full-text index is filled in the background, after the
    /// rows are committed, so a search straight after an insert may not find them.
    /// </summary>
    private static async Task PopulatedAsync(ScratchDatabase database, CancellationToken token)
    {
        const string Pending = """
            SELECT COUNT(*) FROM sys.fulltext_indexes AS i
            WHERE OBJECTPROPERTYEX(i.object_id, 'TableFulltextPopulateStatus') <> 0
               OR OBJECTPROPERTYEX(i.object_id, 'TableFulltextPendingChanges') <> 0
               OR OBJECTPROPERTYEX(i.object_id, 'TableFulltextItemCount') <> (SELECT SUM(p.rows) FROM sys.partitions AS p WHERE p.object_id = i.object_id AND p.index_id IN (0, 1));
            """;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (await ScalarAsync(database, Pending, token) is not 0)
        {
            if (clock.Elapsed > TimeSpan.FromMinutes(2))
                throw new TimeoutException("The full-text indexes didn't take in the seed's rows within two minutes.");
            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
        }
    }

    private static async Task<object?> ScalarAsync(ScratchDatabase database, string sql, CancellationToken token)
    {
        await using var connection = await database.OpenAsync(token);
#pragma warning disable CA2100 // The test's own SQL.
        await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        return await command.ExecuteScalarAsync(token);
    }

    /// <summary>Every row a batch returns, each as its values joined by <c>|</c>, NULL as <c>NULL</c>, rows joined by <c>;</c>.</summary>
    private static async Task<string> RowsAsync(ScratchDatabase database, string sql, CancellationToken token)
    {
        await using var connection = await database.OpenAsync(token);
#pragma warning disable CA2100 // The test's own SQL.
        await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<string>();
        while (await reader.ReadAsync(token))
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        return string.Join("; ", rows);
    }

    /// <summary>The number of the error a batch fails with, or 0 when it doesn't.</summary>
    private static async Task<int> ErrorAsync(ScratchDatabase database, string sql, CancellationToken token)
    {
        try
        {
            await database.ExecuteAsync(sql, token);
            return 0;
        }
        catch (SqlException e)
        {
            return e.Number;
        }
    }

    /// <summary>How the server describes a query's columns: name, type, collation and whether it may be null.</summary>
    private static Task<string> DescribedAsync(ScratchDatabase database, string query, CancellationToken token) => RowsAsync(
        database,
        $"SELECT name, system_type_name, ISNULL(collation_name, '-'), is_nullable FROM sys.dm_exec_describe_first_result_set(N'{query.Replace("'", "''", StringComparison.Ordinal)}', NULL, 0) ORDER BY column_ordinal;",
        token);

    [Fact]
    public async Task A_full_text_search_returns_key_and_rank_whatever_its_columns_language_and_top_n()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await GlossaryAsync(token);

        string[] searches =
        [
            "SELECT * FROM CONTAINSTABLE(lex.entry, headword, N'oak')",
            "SELECT * FROM CONTAINSTABLE(lex.entry, (headword, definition), N'wooden', LANGUAGE 1033, 5)",
            "SELECT * FROM CONTAINSTABLE(lex.entry, *, N'wooden', 1)",
            "SELECT * FROM FREETEXTTABLE(lex.entry, *, N'wooden')",
            "SELECT * FROM FREETEXTTABLE(lex.note, body, N'wooden', LANGUAGE N'English', 2)",
        ];
        foreach (var search in searches)
            Assert.Equal("KEY; RANK", string.Join("; ", (await DescribedAsync(database, search, token)).Split("; ").Select(r => r.Split('|')[0])));
    }

    [Fact]
    public async Task A_full_text_search_declares_key_as_its_key_column_never_null_and_rank_an_int_it_may_make_null()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await GlossaryAsync(token);

        // KEY is the key index's column, of its type and collation (an alias type's base), and never null; RANK an int the
        // server declares nullable, as the column a SELECT INTO makes of it is.
        Assert.Equal("KEY|int|-|False; RANK|int|-|True", await DescribedAsync(database, "SELECT * FROM CONTAINSTABLE(lex.entry, *, N'wooden')", token));
        Assert.Equal(
            "KEY|nvarchar(20)|Latin1_General_100_CI_AS_SC_UTF8|False; RANK|int|-|True",
            await DescribedAsync(database, "SELECT * FROM FREETEXTTABLE(lex.note, *, N'wooden')", token));
        Assert.Equal("KEY|char(8)|SQL_Latin1_General_CP1_CI_AS|False; RANK|int|-|True", await DescribedAsync(database, "SELECT * FROM CONTAINSTABLE(lex.label, words, N'wooden')", token));
        Assert.Equal(
            "KEY|char(8)|Latin1_General_100_CI_AS_SC_UTF8|False; RANK|int|-|True",
            await DescribedAsync(database, "SELECT * FROM CONTAINSTABLE(archive.entry, headword, N'chest')", token));

        // A key of a char(8) in UTF-8 is 8 bytes, which a client reads as fewer characters when any isn't ASCII: KEY keeps the
        // column's collation. (Converted to nvarchar on the server it would be padded to 8 characters, so the value is read as is.)
        Assert.Equal("éé    |8", await RowsAsync(database, "SELECT [KEY], DATALENGTH([KEY]) FROM CONTAINSTABLE(archive.entry, headword, N'wooden');", token));
        Assert.Equal(
            "KEY|False; RANK|True",
            await RowsAsync(database, "SELECT * INTO #found FROM CONTAINSTABLE(lex.entry, *, N'wooden'); SELECT c.name, c.is_nullable FROM tempdb.sys.columns AS c WHERE c.object_id = OBJECT_ID('tempdb..#found') ORDER BY c.column_id;", token));

        // No form of search has been seen to give a NULL RANK, though the server declares it may.
        Assert.Equal(
            "1|32; 2|32",
            await RowsAsync(database, "SELECT [KEY], [RANK] FROM CONTAINSTABLE(lex.entry, *, N'wooden') ORDER BY [KEY];", token));
        string[] searches =
        [
            "CONTAINSTABLE(lex.entry, *, N'wooden OR oak OR tree')",
            "CONTAINSTABLE(lex.entry, *, N'ISABOUT (wooden WEIGHT (0.0), tree WEIGHT (0.0))')",
            "CONTAINSTABLE(lex.entry, *, N'NEAR((wooden, tree), 1)')",
            "CONTAINSTABLE(lex.entry, *, N'FORMSOF(INFLECTIONAL, trees)')",
            "CONTAINSTABLE(lex.entry, *, N'\"wood*\"')",
            "FREETEXTTABLE(lex.entry, *, N'wooden tree too')",
            "FREETEXTTABLE(lex.note, *, N'wooden metal furniture boxes')",
        ];
        foreach (var search in searches)
            Assert.Equal("0", await RowsAsync(database, $"SELECT COUNT(*) - COUNT([RANK]) FROM {search};", token));
    }

    [Fact]
    public async Task A_full_text_search_without_an_alias_is_known_by_its_tables_name()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await GlossaryAsync(token);

        Assert.Equal("N1; N2", await RowsAsync(database, "SELECT note.[KEY] FROM FREETEXTTABLE(lex.note, *, N'wooden') ORDER BY note.[KEY];", token));

        // Not by its schema-qualified name (4104), and a table of the same name beside it is an error (1012).
        Assert.Equal(4104, await ErrorAsync(database, "SELECT lex.note.[KEY] FROM FREETEXTTABLE(lex.note, *, N'wooden');", token));
        Assert.Equal(1012, await ErrorAsync(database, "SELECT lex.note.code FROM lex.note JOIN FREETEXTTABLE(lex.note, *, N'wooden') ON [KEY] = code;", token));
    }

    [Fact]
    public async Task A_full_text_search_returns_the_key_of_a_row_deleted_and_not_committed()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await GlossaryAsync(token);

        // The key comes from the full-text index, not the table: so KEY claims the key column's type, not its CHECK
        // constraints or that its row is there.
        Assert.Equal(
            "1; 2",
            await RowsAsync(database, "BEGIN TRANSACTION; DELETE lex.entry WHERE id = 2; SELECT [KEY] FROM CONTAINSTABLE(lex.entry, *, N'wooden') ORDER BY [KEY]; ROLLBACK;", token));
    }

    [Fact]
    public async Task A_full_text_key_is_a_unique_index_of_one_column_never_null_unfiltered_and_never_masked()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await GlossaryAsync(token);
        await database.ExecuteAsync("""
            CREATE TABLE dbo.nullable (a int NULL, w nvarchar(50));
            CREATE UNIQUE INDEX ux_nullable ON dbo.nullable (a);
            CREATE TABLE dbo.pair (a int NOT NULL, b int NOT NULL, w nvarchar(50));
            CREATE UNIQUE INDEX ux_pair ON dbo.pair (a, b);
            CREATE TABLE dbo.filtered (a int NOT NULL, w nvarchar(50));
            CREATE UNIQUE INDEX ux_filtered ON dbo.filtered (a) WHERE a > 0;
            CREATE TABLE dbo.plain (a int NOT NULL, w nvarchar(50));
            CREATE INDEX ix_plain ON dbo.plain (a);
            CREATE TABLE dbo.masked (a int MASKED WITH (FUNCTION = 'default()') NOT NULL CONSTRAINT pk_masked PRIMARY KEY, w nvarchar(50));
            """, token);

        // Any other index is refused (7653), as is a key column with a mask (16014), a mask added to one (16013), the drop of
        // the key index while the full-text index stands (7613), and a full-text index on a temporary table (208).
        Assert.Equal(7653, await ErrorAsync(database, "CREATE FULLTEXT INDEX ON dbo.nullable (w) KEY INDEX ux_nullable;", token));
        Assert.Equal(7653, await ErrorAsync(database, "CREATE FULLTEXT INDEX ON dbo.pair (w) KEY INDEX ux_pair;", token));
        Assert.Equal(7653, await ErrorAsync(database, "CREATE FULLTEXT INDEX ON dbo.filtered (w) KEY INDEX ux_filtered;", token));
        Assert.Equal(7653, await ErrorAsync(database, "CREATE FULLTEXT INDEX ON dbo.plain (w) KEY INDEX ix_plain;", token));
        Assert.Equal(16014, await ErrorAsync(database, "CREATE FULLTEXT INDEX ON dbo.masked (w) KEY INDEX pk_masked;", token));
        Assert.Equal(16013, await ErrorAsync(database, "ALTER TABLE lex.entry ALTER COLUMN id ADD MASKED WITH (FUNCTION = 'default()');", token));
        Assert.Equal(7613, await ErrorAsync(database, "DROP INDEX ux_note_code ON lex.note;", token));
        Assert.Equal(208, await ErrorAsync(database, "CREATE TABLE #t (a int NOT NULL CONSTRAINT pk_t PRIMARY KEY, w nvarchar(50)); CREATE FULLTEXT INDEX ON #t (w) KEY INDEX pk_t;", token));
    }

    public static TheoryData<string, string> Searches { get; } = new()
    {
        { "lex.search_entries", "@query" },
        { "lex.search_notes", "@words" },
        { "lex.entries_with_notes", "@words" },
        { "lex.search_labels", "@query" },
        { "lex.search_archive", "@query" },
    };

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task Each_glossary_procedure_s_output_keeps_every_claim_of_its_ir(string procedure, string parameter)
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = await GlossaryAsync(token);
        var run = new ProcedureRun($"glossary: {procedure}", "glossary", procedure, new Dictionary<string, RunArgument>(StringComparer.Ordinal)
        {
            [parameter] = new("wooden", SqlDbType.NVarChar, 400),
        });

        await using var session = await database.SessionAsync(token);
        var results = await ProcedureRunner.RunAsync(session, run);
        var report = IrValidator.Validate(Fixture.Named("glossary").Ir, procedure, results);

        Assert.NotEmpty(Assert.Single(results).Rows);
        Assert.True(report.Violations.Count == 0, "The output broke claims of the IR:\n" + string.Join("\n", report.Violations));
    }
}

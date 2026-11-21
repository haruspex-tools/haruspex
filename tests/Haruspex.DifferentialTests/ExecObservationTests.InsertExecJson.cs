namespace Haruspex.DifferentialTests;

public partial class ExecObservationTests
{
    // ---- INSERT … EXEC of JSON -----------------------------------------------------------------------------------------
    //
    // A callee returns JSON as a plain rowset column; INSERT … EXEC puts it in a table variable, read afterwards. What the
    // walker's RowsReceived and the table variable's JSON slots rest on.

    /// <summary>A callee of one row: an id, an object, an array of lines and an array that may be empty, as plain columns.</summary>
    private static readonly string[] Cards = [
        """
        CREATE PROCEDURE dbo.obs_cards @rows int AS
        SELECT TOP (@rows) 1 AS id, json_object('name': N'Ann') AS head,
            json_query(coalesce((SELECT 2 AS q WHERE @rows > 1 FOR JSON PATH), N'[]')) AS lines
        FROM (VALUES (1)) AS v (n);
        """,
    ];

    [Fact]
    public async Task Insert_exec_maps_the_callees_columns_to_the_targets_by_position()
    {
        // The callee's head and lines go into the columns the list names second and third, whatever they are called; a column
        // the list leaves out takes its default (null). Without a list, an identity column and a computed column are skipped.
        Assert.Equal("id,head,lines,other: 1,[],{\"name\":\"Ann\"},null", (await Run(Cards, """
            DECLARE @r TABLE (id int, head nvarchar(max), lines nvarchar(max), other nvarchar(max));
            INSERT INTO @r (id, lines, head) EXEC dbo.obs_cards 1;
            SELECT r.id, r.head, r.lines, ISNULL(r.other, N'null') AS other FROM @r AS r;
            """)).ToString());
        Assert.Equal("n,head: 1,{\"name\":\"Ann\"}", (await Run(Cards, """
            DECLARE @r TABLE (n int IDENTITY(1, 1), id int, head nvarchar(max), lines nvarchar(max));
            INSERT INTO @r EXEC dbo.obs_cards 1;
            SELECT r.n, r.head FROM @r AS r;
            """)).ToString());
        Assert.Equal("twice,head: 2,{\"name\":\"Ann\"}", (await Run(Cards, """
            DECLARE @r TABLE (id int, twice AS id * 2, head nvarchar(max), lines nvarchar(max));
            INSERT INTO @r EXEC dbo.obs_cards 1;
            SELECT r.twice, r.head FROM @r AS r;
            """)).ToString());
        Assert.Equal("twice,head: 2,{\"name\":\"Ann\"}", (await Run(Cards, """
            CREATE TABLE #r (id int, twice AS id * 2, head nvarchar(max), lines nvarchar(max));
            INSERT INTO #r EXEC dbo.obs_cards 1;
            SELECT r.twice, r.head FROM #r AS r;
            """)).ToString());
    }

    [Fact]
    public async Task Insert_exec_never_cuts_a_for_json_result_into_rows_it_fails()
    {
        // 13602 however long the JSON (a FOR JSON result longer than 2,033 characters reaches a client in several rows) and
        // whether the callee is a procedure, a string or sp_executesql. So no JSON result set is ever inserted.
        string[] setup = ["CREATE PROCEDURE dbo.obs_long AS SELECT TOP (500) o.object_id, o.name FROM sys.all_objects AS o ORDER BY o.object_id FOR JSON PATH;"];
        const string Table = "DECLARE @t TABLE (j nvarchar(max));";

        Assert.Equal(13602, (await Run(setup, Table + " INSERT INTO @t EXEC dbo.obs_long;")).Error);
        Assert.Equal(13602, (await Run([], Table + " INSERT INTO @t EXEC (N'SELECT 1 AS a FOR JSON PATH');")).Error);
        Assert.Equal(13602, (await Run([], Table + " INSERT INTO @t EXEC sp_executesql N'SELECT 1 AS a FOR JSON PATH';")).Error);
    }

    [Fact]
    public async Task Insert_exec_converts_json_to_the_columns_type_as_insert_does()
    {
        // Under the driver's settings (ANSI_WARNINGS ON): a column too short fails the insert (8152, or 2628 with verbose
        // truncation messages); varchar keeps only what its code page has (Latin1 here: ā is a, 😀 two question marks); a long
        // enough nvarchar(n) keeps the text. Only nvarchar(max) holds any JSON text unchanged.
        string[] setup = ["CREATE PROCEDURE dbo.obs_uni AS SELECT json_object('name': nchar(257) + nchar(55357) + nchar(56832)) AS j;"];

        Assert.Contains((await Run(setup, "DECLARE @t TABLE (j nvarchar(5)); INSERT INTO @t EXEC dbo.obs_uni;")).Error, new int?[] { 8152, 2628 });
        Assert.Equal("j: {\"name\":\"a??\"}", (await Run(setup, "DECLARE @t TABLE (j varchar(max)); INSERT INTO @t EXEC dbo.obs_uni; SELECT j FROM @t;")).ToString());
        Assert.Equal("j: {\"name\":\"ā😀\"}", (await Run(setup, "DECLARE @t TABLE (j nvarchar(4000)); INSERT INTO @t EXEC dbo.obs_uni; SELECT j FROM @t;")).ToString());
        Assert.Equal("j: {\"name\":\"ā😀\"}", (await Run(setup, "DECLARE @t TABLE (j nvarchar(max)); INSERT INTO @t EXEC dbo.obs_uni; SELECT j FROM @t;")).ToString());
    }

    [Fact]
    public async Task Json_inserted_by_insert_exec_is_text_until_json_query_reads_it()
    {
        // json_query parses it, and FOR JSON nests it as JSON; read plainly, FOR JSON nests it as a quoted string.
        Assert.Equal(
            "j: [{\"head\":{\"name\":\"Ann\"},\"lines\":[{\"q\":2}],\"plain\":\"{\\\"name\\\":\\\"Ann\\\"}\"}]",
            (await Run(Cards, """
                DECLARE @r TABLE (id int, head nvarchar(max), lines nvarchar(max));
                INSERT INTO @r EXEC dbo.obs_cards 2;
                SELECT (SELECT json_query(r.head) AS head, json_query(r.lines) AS lines, r.head AS plain FROM @r AS r FOR JSON PATH) AS j;
                """)).ToString());
    }

    [Fact]
    public async Task Insert_exec_of_no_rows_inserts_none_and_a_null_json_column_stays_null()
    {
        // The coalesce fallback gives [] for a row with no lines; a callee of no rows inserts none; NULL stays NULL, and
        // json_query of it is NULL.
        string[] setup = [.. Cards, "CREATE PROCEDURE dbo.obs_null AS SELECT 3 AS id, CAST(NULL AS nvarchar(max)) AS head, CAST(NULL AS nvarchar(max)) AS lines;"];

        Assert.Equal("n,lines: 1,[]", (await Run(setup, """
            DECLARE @r TABLE (id int, head nvarchar(max), lines nvarchar(max));
            INSERT INTO @r EXEC dbo.obs_cards 0;
            INSERT INTO @r EXEC dbo.obs_cards 1;
            SELECT COUNT(*) AS n, MAX(r.lines) AS lines FROM @r AS r;
            """)).ToString());
        Assert.Equal("j: [{\"head\":null}]", (await Run(setup, """
            DECLARE @r TABLE (id int, head nvarchar(max), lines nvarchar(max));
            INSERT INTO @r EXEC dbo.obs_null;
            SELECT (SELECT json_query(r.head) AS head FROM @r AS r FOR JSON PATH, INCLUDE_NULL_VALUES) AS j;
            """)).ToString());
    }

    [Fact]
    public async Task Insert_exec_whose_callee_fails_inserts_nothing_unless_the_error_lets_it_go_on()
    {
        // Under a CATCH of the caller's, an error after the callee's first result set fails the whole statement: the rows
        // already returned are not kept. Uncaught, a RAISERROR of severity 16 lets the callee go on and every result set is
        // inserted, but the client is handed the error (50000), so the call fails, as it does for a RAISERROR outside INSERT …
        // EXEC; 1 / 0 does the same (8134). A CATCH of the callee's own takes its error and the insert goes on with what its
        // CATCH returns.
        string[] setup = [
            "CREATE PROCEDURE dbo.obs_raise AS SELECT json_object('a': 1) AS j; RAISERROR (N'failed', 16, 1); SELECT json_object('b': 2) AS j;",
            "CREATE PROCEDURE dbo.obs_divide AS SELECT json_object('a': 1) AS j; SELECT CAST(1 / 0 AS nvarchar(max)) AS j; SELECT json_object('b': 2) AS j;",
            "CREATE PROCEDURE dbo.obs_own AS BEGIN TRY SELECT json_object('a': 1) AS j; RAISERROR (N'failed', 16, 1); END TRY BEGIN CATCH SELECT json_object('c': 3) AS j; END CATCH",
        ];
        const string Table = "DECLARE @t TABLE (j nvarchar(max));";
        const string Count = " SELECT COUNT(*) AS n, STRING_AGG(t.j, N',') AS j FROM @t AS t;";

        Assert.Equal("n,j: 0,null", (await Run(setup, Table + " BEGIN TRY INSERT INTO @t EXEC dbo.obs_raise; END TRY BEGIN CATCH END CATCH" + Count)).ToString());
        Assert.Equal("n,j: 0,null", (await Run(setup, Table + " BEGIN TRY INSERT INTO @t EXEC dbo.obs_divide; END TRY BEGIN CATCH END CATCH" + Count)).ToString());
        Assert.Equal("n,j: 2,{\"a\":1},{\"b\":2} ~ 50000", (await Run(setup, Table + " INSERT INTO @t EXEC dbo.obs_raise;" + Count, errorsAsMessages: true)).ToString());
        Assert.Equal("n,j: 2,{\"a\":1},{\"b\":2} ~ 8134", (await Run(setup, Table + " INSERT INTO @t EXEC dbo.obs_divide;" + Count, errorsAsMessages: true)).ToString());
        Assert.Equal("n,j: 2,{\"a\":1},{\"c\":3}", (await Run(setup, Table + " INSERT INTO @t EXEC dbo.obs_own;" + Count)).ToString());
    }
}

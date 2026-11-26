using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The statements that return nothing but change where control goes or how later statements run: <c>GOTO</c> and its labels,
/// <c>SET ROWCOUNT</c>, <c>WAITFOR</c>, <c>EXECUTE AS</c> and <c>REVERT</c>, and <c>SET LANGUAGE</c> with the date settings it
/// sets. The walk's reading of each (<c>Walker</c>, <c>Walker.Jumps.cs</c>) rests on these.
/// </summary>
public partial class StatementFlowObservationTests
{
    [Fact]
    public async Task A_goto_jumps_forward_or_back_out_of_blocks_and_into_them_but_not_into_try()
    {
        string[] setup = [
            """
            CREATE PROCEDURE dbo.obs_goto @a int AS
            IF @a = 1 GOTO Skip
            SELECT 'a' AS a;
            skip:
            SELECT 'b' AS b;
            """,
            """
            CREATE PROCEDURE dbo.obs_goto_back AS
            DECLARE @i int = 0;
            again:
            SET @i += 1;
            IF @i < 3 GOTO again;
            SELECT @i AS i;
            """,
            """
            CREATE PROCEDURE dbo.obs_goto_out @a int AS
            DECLARE @i int = 0;
            IF @a = 1 WHILE 1 = 1 BEGIN SET @i += 1; IF @i = 2 GOTO done; END
            BEGIN TRY SELECT 1 / 0 AS x; END TRY BEGIN CATCH GOTO done; END CATCH
            SELECT 'skipped' AS s;
            done: SELECT @i AS i;
            """,
            """
            CREATE PROCEDURE dbo.obs_goto_in AS
            DECLARE @i int = 5;
            GOTO inside;
            IF @i = 1 BEGIN SELECT 'then' AS t; inside: SELECT 'in' AS i; END ELSE SELECT 'else' AS e;
            GOTO body;
            WHILE @i < 3 BEGIN body: SELECT @i AS w; SET @i += 1; END
            """,
            """
            CREATE PROCEDURE dbo.obs_goto_try AS
            GOTO t;
            BEGIN TRY t: SELECT 1 AS x; END TRY BEGIN CATCH SELECT 2 AS y; END CATCH
            """,
        ];

        // A label is matched ignoring case; what a GOTO skips doesn't run at all, so a later result set moves up.
        Assert.Equal("b: b", (await Run(setup, "EXEC dbo.obs_goto 1")).ToString());
        Assert.Equal("a: a | b: b", (await Run(setup, "EXEC dbo.obs_goto 0")).ToString());

        // A jump back is a loop.
        Assert.Equal("i: 3", (await Run(setup, "EXEC dbo.obs_goto_back")).ToString());

        // Out of a loop, at its first pass that takes it; out of a CATCH, after the result set the error cut short.
        Assert.Equal("i: 2", (await Run(setup, "EXEC dbo.obs_goto_out 1")).ToString());
        Assert.Equal("x:  | i: 0", (await Run(setup, "EXEC dbo.obs_goto_out 0")).ToString());

        // Into an IF's arm and into a loop's body, whose condition isn't tested on the way in.
        Assert.Equal("i: in | w: 5", (await Run(setup, "EXEC dbo.obs_goto_in")).ToString());

        // Into TRY or CATCH the server refuses, but only when the procedure runs; a GOTO to a label the body lacks, or a second
        // label of a name, when it is created.
        Assert.Equal(1026, (await Run(setup, "EXEC dbo.obs_goto_try")).Error);
        Assert.Equal(133, await Fails([], "EXEC (N'CREATE PROCEDURE dbo.obs_goto_none AS GOTO nowhere;')"));
        Assert.Equal(132, await Fails([], "EXEC (N'CREATE PROCEDURE dbo.obs_goto_twice AS l: SELECT 1 AS a; L: SELECT 2 AS b;')"));

        // On a database of a case-sensitive collation as well: a label follows the server's collation, as a variable does.
        var token = TestContext.Current.CancellationToken;
        await using var scratch = await server.ScratchAsync("Latin1_General_100_CS_AS");
        await scratch.ExecuteAsync("CREATE PROCEDURE dbo.obs_goto_case AS GOTO Done; SELECT 1 AS skipped; done: SELECT 2 AS reached;", token);
        await using var connection = await scratch.OpenAsync(token);
#pragma warning disable CA2100 // Observation tests' own batches.
        await using var call = new SqlCommand("EXEC dbo.obs_goto_case", connection);
#pragma warning restore CA2100
        Assert.Equal("reached: 2", (await ObservedCall.Read(call, errorsAsMessages: false, token)).ToString());
    }

    [Fact]
    public async Task Set_rowcount_caps_the_rows_of_a_query_and_lasts_until_the_procedure_returns()
    {
        string[] setup = [
            Rows,
            "INSERT INTO dbo.obs_row (id, n) SELECT value, value FROM GENERATE_SERIES(3, 60);",
            "CREATE PROCEDURE dbo.obs_rowcount_off AS SET ROWCOUNT 0;",
            "CREATE PROCEDURE dbo.obs_rowcount_rows AS SELECT id FROM dbo.obs_row WHERE id < 4 ORDER BY id;",
            """
            CREATE PROCEDURE dbo.obs_rowcount AS
            SET ROWCOUNT 1;
            SELECT id FROM dbo.obs_row ORDER BY id;
            SELECT COUNT(*) AS n FROM dbo.obs_row;
            SELECT TOP (5) id FROM dbo.obs_row ORDER BY id FOR JSON PATH;
            SELECT REPLICATE(N'y', 3000) AS long FOR JSON PATH;
            SELECT id INTO #capped FROM dbo.obs_row;
            DECLARE @json nvarchar(max) = (SELECT id FROM dbo.obs_row FOR JSON PATH);
            SET ROWCOUNT 2;
            EXEC dbo.obs_rowcount_off;
            SELECT (SELECT COUNT(*) FROM #capped) AS into_rows, LEN(@json) - LEN(REPLACE(@json, N'{', N'')) AS objects;
            SELECT id FROM dbo.obs_row ORDER BY id;
            EXEC dbo.obs_rowcount_rows;
            DECLARE @r int = -1;
            SET ROWCOUNT @r;
            """,
        ];

        // A query's rows, a FOR JSON query's rows before its text is made (the text itself, sent as rows of 2,033 characters,
        // is not cut), and SELECT … INTO's are capped; an aggregate over every row, and a subquery's, are not. A callee's SET
        // ROWCOUNT ends when it returns, and a caller's holds in its callees. A negative limit fails the statement (507).
        var outcome = await Run(setup, "EXEC dbo.obs_rowcount", errorsAsMessages: true);
        var sets = outcome.Sets;
        Assert.Equal("id: 1", sets[0]);
        Assert.Equal("n: 60", sets[1]);
        Assert.Equal("JSON_F52E2B61-18A1-11d1-B105-00805F49916B: [{\"id\":1}]", sets[2]);
        Assert.Equal(3013, sets[3].Split(": ", 2)[1].Replace(";", "", StringComparison.Ordinal).Length);
        Assert.Equal(["into_rows,objects: 1,60", "id: 1;2", "id: 1;2"], sets.Skip(4));
        Assert.Equal([507], outcome.Messages);
    }

    [Fact]
    public async Task Waitfor_delay_and_time_return_nothing_and_waitfor_receive_returns_a_result_set()
    {
        string[] setup = [
            "CREATE QUEUE dbo.obs_queue;",
            """
            CREATE PROCEDURE dbo.obs_waitfor AS
            SET NOCOUNT OFF;
            WAITFOR DELAY '00:00:00.010';
            DECLARE @t char(12) = CONVERT(char(12), DATEADD(millisecond, 200, SYSDATETIME()), 114);
            WAITFOR TIME @t;
            SELECT 1 AS waited;
            WAITFOR (RECEIVE TOP (1) message_type_name FROM dbo.obs_queue), TIMEOUT 10;
            SELECT 2 AS received;
            """,
        ];

        // The RECEIVE returns a result set of the queue's columns even when no message arrives before the timeout.
        Assert.Equal("waited: 1 | message_type_name:  | received: 2", (await Run(setup, "EXEC dbo.obs_waitfor")).ToString());
    }

    [Fact]
    public async Task Execute_as_changes_permissions_and_masks_and_names_only_outside_a_module_and_ends_when_the_procedure_returns()
    {
        string[] setup = [
            "CREATE SCHEMA obs_elsewhere;",
            "CREATE TABLE obs_elsewhere.obs_named (other int NOT NULL); INSERT INTO obs_elsewhere.obs_named VALUES (1);",
            "CREATE TABLE dbo.obs_named (mine nvarchar(5) NOT NULL); INSERT INTO dbo.obs_named VALUES (N'dbo');",
            "CREATE TABLE dbo.obs_secret (code nvarchar(10) MASKED WITH (FUNCTION = 'default()') NOT NULL); INSERT INTO dbo.obs_secret VALUES (N'abc');",
            "CREATE USER obs_impersonated WITHOUT LOGIN WITH DEFAULT_SCHEMA = obs_elsewhere; GRANT SELECT, EXECUTE TO obs_impersonated;",
            """
            CREATE PROCEDURE dbo.obs_execute_as AS
            SELECT code FROM dbo.obs_secret;
            EXECUTE AS USER = 'obs_impersonated';
            SELECT USER_NAME() AS who;
            SELECT * FROM obs_named;
            SELECT code FROM dbo.obs_secret;
            REVERT;
            SELECT code FROM dbo.obs_secret;
            EXECUTE AS USER = 'obs_impersonated';
            """,
            """
            CREATE PROCEDURE dbo.obs_execute_as_cookie AS
            DECLARE @c varbinary(100);
            EXECUTE AS USER = 'obs_impersonated' WITH COOKIE INTO @c;
            """,
        ];

        // The test's own session holds UNMASK, and the user doesn't: under EXECUTE AS it reads the mask. An unqualified name
        // binds in the procedure's schema as before, not the user's default schema (outside a module it does: below). The
        // procedure's last EXECUTE AS ends when it returns, and a cookie, allowed only outside a module, fails the call when it
        // runs (15590).
        Assert.Equal(
            "code: abc | who: obs_impersonated | mine: dbo | code: xxxx | code: abc | who: dbo",
            (await Run(setup, "EXEC dbo.obs_execute_as; SELECT USER_NAME() AS who;")).ToString());
        Assert.Equal(15590, (await Run(setup, "EXEC dbo.obs_execute_as_cookie")).Error);

        // Outside a module, as a query file runs, an unqualified name binds in the impersonated user's default schema, directly
        // and through sp_executesql alike.
        Assert.Equal(
            "other: 1 | other: 1 | mine: dbo",
            (await Run(setup, "EXECUTE AS USER = 'obs_impersonated'; SELECT * FROM obs_named; EXEC sp_executesql N'SELECT * FROM obs_named'; REVERT; SELECT * FROM obs_named;")).ToString());
    }

    [Fact]
    public async Task The_language_changes_values_but_no_type_or_claimed_fact_and_lasts_until_the_procedure_returns()
    {
        // In every language the server has, with the first day of the week and the order of a date's parts it sets: each date
        // and time, in every style, converts to text that needs no escape inside a JSON string (Typer.JsonAssembled's
        // SafeTextPiece); each number converts to the same text as in us_english (NumberPiece); and DATENAME, DATEPART and a
        // conversion keep their types, DATENAME never NULL and never past nvarchar(30). Twelve dates, one in each month, cover
        // every day of the week, in the morning and the afternoon.
        const string Sweep = """
            DECLARE @l sysname, @sql nvarchar(max);
            CREATE TABLE #seen (lang sysname, needs_escape int, numbers nvarchar(200), types nvarchar(400), names_null int, longest_name int);
            DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.syslanguages;
            OPEN c; FETCH NEXT FROM c INTO @l;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @sql = N'SET LANGUAGE ' + QUOTENAME(@l) + N';
                WITH d AS (SELECT DATEADD(hour, 12 * (value % 2), DATEADD(day, 31 * value, CAST(''2026-01-05T01:02:03.1234567'' AS datetime2))) AS d FROM GENERATE_SERIES(0, 11)),
                s AS (SELECT value AS style FROM GENERATE_SERIES(0, 14) UNION ALL SELECT value FROM GENERATE_SERIES(20, 25) UNION ALL SELECT value FROM GENERATE_SERIES(100, 114) UNION ALL SELECT value FROM GENERATE_SERIES(120, 131)),
                t AS (
                    SELECT TRY_CONVERT(nvarchar(100), d, style) AS txt FROM d CROSS JOIN s
                    UNION ALL SELECT TRY_CONVERT(nvarchar(100), CAST(d AS datetime), style) FROM d CROSS JOIN s
                    UNION ALL SELECT TRY_CONVERT(nvarchar(100), CAST(d AS smalldatetime), style) FROM d CROSS JOIN s
                    UNION ALL SELECT TRY_CONVERT(nvarchar(100), CAST(d AS datetimeoffset), style) FROM d CROSS JOIN s
                    UNION ALL SELECT TRY_CONVERT(varchar(100), CAST(d AS date), style) FROM d CROSS JOIN s
                    UNION ALL SELECT TRY_CONVERT(varchar(100), CAST(d AS time), style) FROM d CROSS JOIN s
                    UNION ALL SELECT CAST(d AS nvarchar(100)) FROM d)
                INSERT INTO #seen
                SELECT @@LANGUAGE,
                    (SELECT COUNT(*) FROM t WHERE REPLACE(STRING_ESCAPE(txt, ''json''), N''\/'', N''/'') <> txt),
                    CONCAT_WS(N''|'', CAST(CAST(-12345.678 AS decimal(10, 3)) AS varchar(30)), CAST(-2147483647 AS varchar(30)),
                        CAST(CAST(-1234567.89 AS money) AS varchar(30)), CONVERT(varchar(30), CAST(-1234567.89 AS money), 2),
                        CONVERT(varchar(30), CAST(-1234567.89 AS money), 126), CAST(CAST(1 AS bit) AS varchar(5)), CAST(-1.5e10 AS varchar(30))),
                    (SELECT TOP (1) CONCAT_WS(N''|'',
                        CAST(SQL_VARIANT_PROPERTY(DATENAME(month, d), ''BaseType'') AS nvarchar(20)), CAST(SQL_VARIANT_PROPERTY(DATENAME(month, d), ''MaxLength'') AS nvarchar(20)),
                        CAST(SQL_VARIANT_PROPERTY(DATEPART(weekday, d), ''BaseType'') AS nvarchar(20)),
                        CAST(SQL_VARIANT_PROPERTY(CAST(d AS varchar(30)), ''BaseType'') AS nvarchar(20)), CAST(SQL_VARIANT_PROPERTY(CAST(d AS varchar(30)), ''MaxLength'') AS nvarchar(20)))
                        FROM d),
                    (SELECT COUNT(*) FROM d WHERE DATENAME(month, d) IS NULL OR DATENAME(weekday, d) IS NULL),
                    (SELECT MAX(LEN(n)) FROM d CROSS APPLY (VALUES (DATENAME(month, d)), (DATENAME(weekday, d))) AS v (n));';
                EXEC sp_executesql @sql;
                FETCH NEXT FROM c INTO @l;
            END
            SELECT COUNT(*) AS languages, SUM(needs_escape) AS needs_escape, COUNT(DISTINCT numbers) AS numbers, MIN(types) AS types,
                COUNT(DISTINCT types) AS kinds, SUM(names_null) AS names_null, MAX(longest_name) AS longest_name
            FROM #seen;
            """;
        Assert.Equal(
            "languages,needs_escape,numbers,types,kinds,names_null,longest_name: 34,0,1,nvarchar|60|int|varchar|30,1,0,14",
            (await Run([], Sweep)).ToString());

        // What changes is values: DATENAME's names, DATEPART's weekday and how a string reads as a date. A procedure's SET
        // LANGUAGE, DATEFIRST and DATEFORMAT end when it returns.
        string[] setup = [
            """
            CREATE PROCEDURE dbo.obs_language AS
            SET LANGUAGE French;
            SET DATEFIRST 1;
            SET DATEFORMAT dmy;
            SELECT DATENAME(month, '2026-03-02') AS month, DATEPART(weekday, '2026-03-02') AS day, MONTH(CAST('02/03/2026' AS datetime)) AS parsed;
            """,
        ];
        Assert.Equal(
            "month,day,parsed: mars,1,3 | month,day,parsed: March,2,2",
            (await Run(setup, "EXEC dbo.obs_language; SELECT DATENAME(month, '2026-03-02') AS month, DATEPART(weekday, '2026-03-02') AS day, MONTH(CAST('02/03/2026' AS datetime)) AS parsed;")).ToString());
    }
}

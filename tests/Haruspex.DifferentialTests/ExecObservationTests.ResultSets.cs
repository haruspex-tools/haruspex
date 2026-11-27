namespace Haruspex.DifferentialTests;

/// <summary>
/// What <c>EXEC … WITH RESULT SETS</c> guarantees, observed: the rules that typing a call by its declaration rests on
/// (Haruspex.SqlServer.Inference's README.md, "Result sets a call declares"). The declaration fixes how many result sets
/// arrive, each column's name and type, and which columns are never null; anything else fails the call. Observed on SQL
/// Server 2025 RTM-CU10.
/// </summary>
public partial class ExecObservationTests
{
    /// <summary>What the procedures the tests below call return: one set of an int and a string, a row of which is null.</summary>
    private static readonly string[] Declarable = [
        "CREATE TABLE dbo.obs_k (id int NOT NULL, name nvarchar(20) NULL);",
        "INSERT INTO dbo.obs_k VALUES (7, N'seven'), (1000, NULL);",
        "CREATE PROCEDURE dbo.obs_one AS SELECT id, name FROM dbo.obs_k ORDER BY id;",
        "CREATE PROCEDURE dbo.obs_two AS SELECT id FROM dbo.obs_k ORDER BY id; SELECT name FROM dbo.obs_k ORDER BY id;",
        "CREATE PROCEDURE dbo.obs_quiet AS DECLARE @x int = 1;",
        "CREATE PROCEDURE dbo.obs_early @a bit AS IF @a = 1 RETURN; SELECT id FROM dbo.obs_k ORDER BY id;",
        "CREATE PROCEDURE dbo.obs_empty AS SELECT id FROM dbo.obs_k WHERE id < 0;",
    ];

    [Fact]
    public async Task With_result_sets_fixes_how_many_result_sets_arrive_and_any_other_number_fails_the_call()
    {
        // So a call that succeeds returned exactly the sets declared, each present, whatever paths the callee has: one too
        // many fails as the callee tries to send it (11535), one too few once the callee ends (11536), after the sets before.
        // An early RETURN is no exception, and NONE is a count of 0.
        Assert.Equal("i,n: 7,seven;1000,null ! 11536", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i int, n nvarchar(20)), (j int))")).ToString());
        Assert.Equal("i: 7;1000 ! 11535", (await Run(Declarable, "EXEC dbo.obs_two WITH RESULT SETS ((i int))")).ToString());
        Assert.Equal(" ! 11536", (await Run(Declarable, "EXEC dbo.obs_quiet WITH RESULT SETS ((i int))")).ToString());
        Assert.Equal(" ! 11536", (await Run(Declarable, "EXEC dbo.obs_early 1 WITH RESULT SETS ((i int))")).ToString());
        Assert.Equal("i: 7;1000", (await Run(Declarable, "EXEC dbo.obs_early 0 WITH RESULT SETS ((i int))")).ToString());
        Assert.Equal("", (await Run(Declarable, "EXEC dbo.obs_quiet WITH RESULT SETS NONE")).ToString());
        Assert.Equal(" ! 11535", (await Run(Declarable, "EXEC dbo.obs_early 0 WITH RESULT SETS NONE")).ToString());
        Assert.Equal("", (await Run(Declarable, "EXEC dbo.obs_early 1 WITH RESULT SETS NONE")).ToString());
    }

    [Fact]
    public async Task A_set_of_another_width_or_of_a_type_with_no_conversion_fails_the_call_even_with_no_rows()
    {
        // 11537 for the number of columns, 11538 for a type the callee's can't convert to: both before any row, so a callee
        // whose set has the wrong width or type fails every call that reaches it.
        Assert.Equal(" ! 11537", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i int))")).ToString());
        Assert.Equal(" ! 11537", (await Run(Declarable, "EXEC dbo.obs_empty WITH RESULT SETS ((i int, j int))")).ToString());
        Assert.Equal(" ! 11538", (await Run(Declarable, "EXEC dbo.obs_empty WITH RESULT SETS ((i date))")).ToString());
    }

    [Fact]
    public async Task A_declared_column_holds_each_value_converted_to_its_type()
    {
        // As CAST converts: a string is cut short and a char padded, without an error; an int too long for a string is *; a
        // value that can't convert, or a number past a decimal's digits, fails the call (8114, not 8115), even under the
        // caller's ANSI_WARNINGS and ARITHABORT off, which make a CAST's overflow NULL. So a declared column's values are its
        // type's, and nothing of the callee's survives but what the type keeps.
        Assert.Equal("i,n: 7,sev;*,null", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i varchar(3), n varchar(3)))")).ToString());
        Assert.Equal("i,n: 7    ,seven;1000 ,null", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i char(5), n nvarchar(20)))")).ToString());
        Assert.Equal(" ! 8114", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i int, n int))")).ToString());
        Assert.Equal(" ! 8114", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i decimal(3,1), n nvarchar(20)))")).ToString());
        Assert.Equal(" ! 8114", (await Run(Declarable, "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; EXEC dbo.obs_one WITH RESULT SETS ((i tinyint NULL, n nvarchar(20)))")).ToString());
    }

    [Fact]
    public async Task A_column_declared_not_null_fails_the_call_on_a_null_after_the_rows_before_it()
    {
        // 11553, raised at the row: the rows before it have reached the client. A table type's NOT NULL column does the same.
        Assert.Equal("i,n: 7,seven ~ 11553", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i int NOT NULL, n nvarchar(20) NOT NULL))", errorsAsMessages: true)).ToString());
        string[] typed = [.. Declarable, "CREATE TYPE dbo.obs_kt AS TABLE (i int NOT NULL, n nvarchar(20) NOT NULL);"];
        Assert.Equal(" ! 11553", (await Run(typed, "EXEC dbo.obs_one WITH RESULT SETS (AS TYPE dbo.obs_kt)")).ToString());
    }

    [Fact]
    public async Task A_column_declared_neither_null_nor_not_null_follows_the_sessions_null_default()
    {
        // Nullable by default, but NOT NULL under the session's ANSI_NULL_DFLT_OFF (11553), as a column of a table created
        // then would be, in a procedure too: the option is the caller's, read when a batch is compiled, so not one in the batch
        // itself. An alias type declared NOT NULL makes the column nothing more: a NULL passes. So only NOT NULL,
        // written in the declaration, proves a column non-null.
        Assert.Equal("i,n: 7,seven;1000,null", (await Run(Declarable, "EXEC dbo.obs_one WITH RESULT SETS ((i int, n nvarchar(20)))")).ToString());
        string[] off = [
            .. Declarable,
            "CREATE PROCEDURE dbo.obs_defaulted AS EXEC dbo.obs_one WITH RESULT SETS ((i int, n nvarchar(20)));",
            "SET ANSI_NULL_DFLT_ON OFF; SET ANSI_NULL_DFLT_OFF ON;",
        ];
        Assert.Equal(" ! 11553", (await Run(off, "EXEC dbo.obs_one WITH RESULT SETS ((i int, n nvarchar(20)))")).ToString());
        Assert.Equal(" ! 11553", (await Run(off, "EXEC dbo.obs_defaulted")).ToString());
        string[] aliased = [.. Declarable, "CREATE TYPE dbo.obs_name FROM nvarchar(20) NOT NULL;"];
        Assert.Equal("i,n: 7,seven;1000,null", (await Run(aliased, "EXEC dbo.obs_one WITH RESULT SETS ((i int, n dbo.obs_name))")).ToString());
    }

    [Fact]
    public async Task A_declared_string_column_takes_the_databases_collation_unless_it_says_another()
    {
        // As the server describes the call: each column's type as declared, and a string's collation the database's or its own
        // COLLATE. The collation changes no value's type, so it is not part of the IR.
        const string describe = "SELECT d.name, d.system_type_name, CASE WHEN d.collation_name = CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation')) THEN N'database' ELSE d.collation_name END AS collation FROM sys.dm_exec_describe_first_result_set(N'EXEC dbo.obs_one WITH RESULT SETS ((i int, n nvarchar(20) COLLATE Latin1_General_BIN2), (m varchar(3)))', NULL, 0) AS d ORDER BY d.column_ordinal;";
        Assert.Equal("name,system_type_name,collation: i,int,null;n,nvarchar(20),Latin1_General_BIN2", (await Run(Declarable, describe)).ToString());
        const string second = "SELECT d.name, CASE WHEN d.collation_name = CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation')) THEN N'database' ELSE d.collation_name END AS collation FROM sys.dm_exec_describe_first_result_set(N'EXEC dbo.obs_one WITH RESULT SETS ((m varchar(3), n nvarchar(20)))', NULL, 0) AS d ORDER BY d.column_ordinal;";
        Assert.Equal("name,collation: m,database;n,database", (await Run(Declarable, second)).ToString());
    }

    [Fact]
    public async Task With_result_sets_types_dynamic_sql_as_it_types_a_procedure()
    {
        // sp_executesql and EXEC of a string take the clause too, and convert alike, whatever the text runs.
        Assert.Equal("id,name: 7,seven;1000,null", (await Run(Declarable, "DECLARE @t sysname = N'obs_k'; DECLARE @s nvarchar(200) = N'SELECT id, name FROM dbo.' + @t + N' ORDER BY id'; EXEC sp_executesql @s WITH RESULT SETS ((id int NOT NULL, name nvarchar(50) NULL));")).ToString());
        Assert.Equal("id: 7;*", (await Run(Declarable, "EXEC (N'SELECT id FROM dbo.obs_k ORDER BY id') WITH RESULT SETS ((id varchar(3)));")).ToString());
        Assert.Equal(" ! 11536", (await Run(Declarable, "EXEC sp_executesql N'DECLARE @x int = 1;' WITH RESULT SETS ((id int));")).ToString());
    }

    [Fact]
    public async Task An_error_with_result_sets_raises_is_caught_by_the_callers_catch_after_the_sets_already_sent()
    {
        string[] setup = [
            .. Declarable,
            "CREATE PROCEDURE dbo.obs_catching AS BEGIN TRY EXEC dbo.obs_one WITH RESULT SETS ((i int, n nvarchar(9) NOT NULL)); END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS caught; END CATCH",
            "CREATE PROCEDURE dbo.obs_catching_count AS BEGIN TRY EXEC dbo.obs_one WITH RESULT SETS ((i int, n nvarchar(9)), (j int)); END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS caught; END CATCH",
        ];

        Assert.Equal("i,n: 7,seven | caught: 11553", (await Run(setup, "EXEC dbo.obs_catching", errorsAsMessages: true)).ToString());
        Assert.Equal("i,n: 7,seven;1000,null | caught: 11536", (await Run(setup, "EXEC dbo.obs_catching_count")).ToString());
    }

    [Fact]
    public async Task As_object_and_as_type_take_the_columns_of_a_table_or_a_table_type_and_as_for_xml_one_ntext_column()
    {
        // A table's and a table type's columns: names, types and NOT NULL. AS FOR XML is one ntext column named as FOR XML output
        // is, which an int can't convert to (11538).
        string[] setup = [
            .. Declarable,
            "CREATE TABLE dbo.obs_shape (a int NOT NULL, b nvarchar(5) NULL);",
            "CREATE TYPE dbo.obs_kt AS TABLE (c bigint NOT NULL, d varchar(4) NULL);",
        ];
        const string describe = "SELECT d.name, d.is_nullable, d.system_type_name FROM sys.dm_exec_describe_first_result_set(N'EXEC dbo.obs_one WITH RESULT SETS ({0})', NULL, 0) AS d ORDER BY d.column_ordinal;";

        Assert.Equal("name,is_nullable,system_type_name: a,False,int;b,True,nvarchar(5)", (await Run(setup, string.Format(System.Globalization.CultureInfo.InvariantCulture, describe, "AS OBJECT dbo.obs_shape"))).ToString());
        Assert.Equal("name,is_nullable,system_type_name: c,False,bigint;d,True,varchar(4)", (await Run(setup, string.Format(System.Globalization.CultureInfo.InvariantCulture, describe, "AS TYPE dbo.obs_kt"))).ToString());
        Assert.Equal("name,is_nullable,system_type_name: XML_F52E2B61-18A1-11d1-B105-00805F49916B,True,ntext", (await Run(setup, string.Format(System.Globalization.CultureInfo.InvariantCulture, describe, "AS FOR XML"))).ToString());
        Assert.Equal(" ! 11538", (await Run([.. setup, "CREATE PROCEDURE dbo.obs_int AS SELECT 5 AS i;"], "EXEC dbo.obs_int WITH RESULT SETS (AS FOR XML)")).ToString());
    }

    [Fact]
    public async Task A_for_json_result_declared_as_a_column_arrives_as_its_pieces_one_row_each()
    {
        // The server sends FOR JSON output in rows of at most 2,033 characters, which a client joins only under the column
        // name SQL Server gives it; declared under another name, the pieces stay rows. So a declaration renames a JSON result
        // into text in pieces, not one document.
        string[] setup = ["CREATE PROCEDURE dbo.obs_long_json AS SELECT REPLICATE(N'x', 3000) AS v FOR JSON PATH;"];

        var outcome = await Run(setup, "EXEC dbo.obs_long_json WITH RESULT SETS ((j nvarchar(max) NOT NULL))");

        var rows = Assert.Single(outcome.Sets)["j: ".Length..].Split(';');
        Assert.Equal(2, rows.Length);
        Assert.Equal(2033, rows[0].Length);
        Assert.Equal("[{\"v\":\"" + new string('x', 3000) + "\"}]", string.Concat(rows));
    }

    [Fact]
    public async Task Insert_exec_refuses_with_result_sets_but_a_callee_may_use_it()
    {
        // INSERT … EXEC … WITH RESULT SETS is a syntax error (102), so no procedure holds one; a procedure that declares its
        // call's sets puts the converted rows into the table when it is itself called by INSERT … EXEC.
        string[] setup = [
            .. Declarable,
            "CREATE PROCEDURE dbo.obs_declaring AS EXEC dbo.obs_one WITH RESULT SETS ((i varchar(3) NOT NULL, n nvarchar(20)));",
        ];

        Assert.Equal(102, (await Run(setup, "CREATE TABLE #obs_t (i varchar(10), n nvarchar(20)); INSERT INTO #obs_t EXEC dbo.obs_one WITH RESULT SETS ((i varchar(3), n nvarchar(20)));")).Error);
        Assert.Equal("i,n: 7,seven;*,null", (await Run(setup, "CREATE TABLE #obs_t (i varchar(10), n nvarchar(20)); INSERT INTO #obs_t EXEC dbo.obs_declaring; SELECT i, n FROM #obs_t ORDER BY n DESC;")).ToString());
    }
}

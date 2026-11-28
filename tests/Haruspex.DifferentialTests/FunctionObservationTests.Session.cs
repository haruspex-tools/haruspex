namespace Haruspex.DifferentialTests;

// The values a call reads from its session or its database: the counters (@@ROWCOUNT, ROWCOUNT_BIG, @@TRANCOUNT, @@ERROR), the
// error functions, the identities (SCOPE_IDENTITY, @@IDENTITY, IDENT_CURRENT), USER_NAME of an id, and NEXT VALUE FOR. Each has
// a type the server fixes, and is nullable unless it is never NULL in any state (decision 0146). Inference's README.md,
// "Session values", gives the rules these rest on.
public partial class FunctionObservationTests
{
    [Fact]
    public async Task Rowcount_trancount_and_error_are_int_rowcount_big_a_bigint_and_none_is_ever_null()
    {
        Assert.Equal(
            "a int, b bigint, c int, d int",
            await Describe("SELECT @@ROWCOUNT AS a, ROWCOUNT_BIG() AS b, @@TRANCOUNT AS c, @@ERROR AS d"));

        // Not NULL at the start of a batch, after a query of no rows, after a statement that failed (in its CATCH), after one
        // that only declares, or inside a scalar function. Each answer is 1 when all four are values.
        const string Four = "IIF(@@ROWCOUNT IS NULL OR ROWCOUNT_BIG() IS NULL OR @@TRANCOUNT IS NULL OR @@ERROR IS NULL, 0, 1)";
        Assert.Equal(
            "a: 1 | b: 1 | c: 1 | d: 1 | e: 1",
            (await Run(
                [$"CREATE FUNCTION dbo.obs_counters() RETURNS int AS BEGIN RETURN {Four}; END;"],
                $"""
                SELECT {Four} AS a;
                DECLARE @w int;
                SELECT @w = 1 WHERE 1 = 0;
                SELECT {Four} AS b;
                BEGIN TRY SET @w = 1 / 0; END TRY BEGIN CATCH SELECT {Four} AS c; END CATCH;
                DECLARE @v int;
                SELECT {Four} AS d;
                SELECT dbo.obs_counters() AS e;
                """)).ToString());
    }

    [Fact]
    public async Task Error_functions_are_null_outside_a_catch_and_error_procedure_inside_one_for_an_error_outside_a_module()
    {
        Assert.Equal(
            "a nvarchar(4000), b int, c int, d int, e int, f nvarchar(128)",
            await Describe("SELECT ERROR_MESSAGE() AS a, ERROR_NUMBER() AS b, ERROR_SEVERITY() AS c, ERROR_STATE() AS d, ERROR_LINE() AS e, ERROR_PROCEDURE() AS f"));

        // NULL outside a CATCH, after an error as much as before one; inside one each is the error's, but ERROR_PROCEDURE is NULL
        // for an error raised outside a module.
        Assert.Equal(
            "a,b,c,d,e,f: null,null,null,null,null,null | a,b,c,d,e,f: Divide by zero error encountered.,8134,16,1,4,null | a,b,c,d,e,f: null,null,null,null,null,null",
            (await Run([], """
                SELECT ERROR_MESSAGE() AS a, ERROR_NUMBER() AS b, ERROR_SEVERITY() AS c, ERROR_STATE() AS d, ERROR_LINE() AS e, ERROR_PROCEDURE() AS f;
                DECLARE @w int;
                BEGIN TRY
                    SET @w = 1 / 0;
                END TRY
                BEGIN CATCH
                    SELECT ERROR_MESSAGE() AS a, ERROR_NUMBER() AS b, ERROR_SEVERITY() AS c, ERROR_STATE() AS d, ERROR_LINE() AS e, ERROR_PROCEDURE() AS f;
                END CATCH;
                SELECT ERROR_MESSAGE() AS a, ERROR_NUMBER() AS b, ERROR_SEVERITY() AS c, ERROR_STATE() AS d, ERROR_LINE() AS e, ERROR_PROCEDURE() AS f;
                """)).ToString());
    }

    [Fact]
    public async Task Scope_identity_and_the_last_identity_are_numeric_38_0_and_null_after_an_insert_into_a_table_without_one()
    {
        string[] setup =
        [
            "CREATE TABLE dbo.obs_counted (id int IDENTITY(1, 1) NOT NULL PRIMARY KEY, v int NULL);",
            "CREATE TABLE dbo.obs_uncounted (v int NULL);",
            "CREATE PROCEDURE dbo.obs_identities AS SELECT SCOPE_IDENTITY() AS inner_scope, @@IDENTITY AS inner_last;",
        ];
        Assert.Equal("a numeric(38,0), b numeric(38,0)", await Describe("SELECT SCOPE_IDENTITY() AS a, @@IDENTITY AS b"));

        // A value straight after an insert into a table with an identity; NULL, both, after an insert into one without, though
        // the statement before it gave one; and in a procedure called after the insert, SCOPE_IDENTITY is NULL (its scope has
        // inserted nothing) while @@IDENTITY is the caller's.
        Assert.Equal(
            "a,b: 1,1 | a,b: null,null | inner_scope,inner_last: null,2",
            (await Run(setup, """
                INSERT INTO dbo.obs_counted (v) VALUES (1);
                SELECT SCOPE_IDENTITY() AS a, @@IDENTITY AS b;
                INSERT INTO dbo.obs_uncounted (v) VALUES (1);
                SELECT SCOPE_IDENTITY() AS a, @@IDENTITY AS b;
                INSERT INTO dbo.obs_counted (v) VALUES (2);
                EXECUTE dbo.obs_identities;
                """)).ToString());

        // An insert of no rows leaves both as they were: the identity before it, or NULL when what ran before gave none.
        Assert.Equal(
            "a,b: 1,1 | a,b: null,null",
            (await Run(setup, """
                INSERT INTO dbo.obs_counted (v) VALUES (1);
                INSERT INTO dbo.obs_counted (v) SELECT v FROM dbo.obs_uncounted WHERE 1 = 0;
                SELECT SCOPE_IDENTITY() AS a, @@IDENTITY AS b;
                INSERT INTO dbo.obs_uncounted (v) VALUES (1);
                INSERT INTO dbo.obs_counted (v) SELECT v FROM dbo.obs_uncounted WHERE 1 = 0;
                SELECT SCOPE_IDENTITY() AS a, @@IDENTITY AS b;
                """)).ToString());
    }

    [Fact]
    public async Task Ident_current_is_numeric_38_0_and_null_for_a_table_without_an_identity_a_missing_one_or_one_the_caller_cannot_see()
    {
        string[] setup =
        [
            "CREATE TABLE dbo.obs_counted (id int IDENTITY(5, 1) NOT NULL PRIMARY KEY, v int NULL);",
            "CREATE TABLE dbo.obs_uncounted (v int NULL);",
            "CREATE USER obs_nobody WITHOUT LOGIN;",
        ];
        Assert.Equal(
            "a numeric(38,0), b numeric(38,0), c numeric(38,0)",
            await Describe("SELECT IDENT_CURRENT('dbo.t') AS a, IDENT_CURRENT(N'dbo.t') AS b, IDENT_CURRENT(CAST('dbo.t' AS varchar(max))) AS c"));

        // Its seed before any insert; NULL for a table without an identity, a name that names no table, an empty or NULL name,
        // and, to a user granted nothing, a table it can't see.
        Assert.Equal(
            "a,b,c,d,e: 5,null,null,null,null | hidden: null",
            (await Run(setup, """
                DECLARE @none nvarchar(10) = NULL;
                SELECT IDENT_CURRENT('dbo.obs_counted') AS a, IDENT_CURRENT('dbo.obs_uncounted') AS b, IDENT_CURRENT('dbo.obs_missing') AS c,
                       IDENT_CURRENT('') AS d, IDENT_CURRENT(@none) AS e;
                EXECUTE AS USER = 'obs_nobody';
                SELECT IDENT_CURRENT('dbo.obs_counted') AS hidden;
                REVERT;
                """)).ToString());
    }

    [Fact]
    public async Task User_name_of_an_id_is_nvarchar_128_and_null_for_an_id_that_names_no_user()
    {
        Assert.Equal("a nvarchar(128), b nvarchar(128), c nvarchar(128)", await Describe($"SELECT USER_NAME(i) AS a, USER_NAME(ti) AS b, USER_NAME(bi) AS c FROM {Row}"));

        // 1 is dbo and 0 public; an id that names no user, a negative one and NULL give NULL.
        Assert.Equal(
            "a,b,c,d,e: dbo,public,null,null,null",
            (await Run([], "DECLARE @n int = NULL; SELECT USER_NAME(1) AS a, USER_NAME(0) AS b, USER_NAME(32767) AS c, USER_NAME(-1) AS d, USER_NAME(@n) AS e;")).ToString());

        // The id is converted to int: a wider one may overflow, an error by default and NULL under the caller's options.
        const string Wide = "SELECT USER_NAME(CAST(3000000000 AS bigint)) AS v;";
        Assert.Equal(" ! 8115", (await Run([], Wide)).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Wide)).ToString());
    }

    [Fact]
    public async Task Next_value_for_is_its_sequences_type_never_null_and_an_error_once_a_sequence_without_cycle_is_spent()
    {
        string[] setup =
        [
            "CREATE TYPE dbo.obs_small FROM smallint NOT NULL;",
            "CREATE SEQUENCE dbo.obs_int AS int START WITH 1;",
            "CREATE SEQUENCE dbo.obs_tiny AS tinyint START WITH 1;",
            "CREATE SEQUENCE dbo.obs_decimal AS decimal(10, 0) START WITH 1;",
            "CREATE SEQUENCE dbo.obs_numeric AS numeric(5) START WITH 1;",
            "CREATE SEQUENCE dbo.obs_plain START WITH 1;",
            "CREATE SEQUENCE dbo.obs_alias AS dbo.obs_small START WITH 1;",
        ];

        // Its declared type, an alias type's base type, and bigint when none is declared; never NULL, the server says.
        Assert.Equal(
            "a int, b tinyint, c decimal(10,0), d numeric(5,0), e bigint, f smallint",
            await Describe(
                "SELECT NEXT VALUE FOR dbo.obs_int AS a, NEXT VALUE FOR dbo.obs_tiny AS b, NEXT VALUE FOR dbo.obs_decimal AS c, NEXT VALUE FOR dbo.obs_numeric AS d, NEXT VALUE FOR dbo.obs_plain AS e, NEXT VALUE FOR dbo.obs_alias AS f",
                setup));

        // A sequence without CYCLE gives each value up to its maximum and then fails (11728) rather than giving NULL; one with
        // CYCLE starts again at its minimum. No option makes it NULL: 11728 stays an error with ANSI_WARNINGS and ARITHABORT off.
        string[] ends =
        [
            "CREATE SEQUENCE dbo.obs_end AS tinyint START WITH 254 MAXVALUE 255 NO CACHE;",
            "CREATE SEQUENCE dbo.obs_cycle AS tinyint START WITH 255 MINVALUE 3 MAXVALUE 255 CYCLE NO CACHE;",
        ];
        Assert.Equal(
            "a: 254 | b: 255 ! 11728",
            (await Run(ends, "SELECT NEXT VALUE FOR dbo.obs_end AS a; SELECT NEXT VALUE FOR dbo.obs_end AS b; SELECT NEXT VALUE FOR dbo.obs_end AS c;")).ToString());
        Assert.Equal(
            "a: 254 | b: 255 ! 11728",
            (await Run(ends, "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT NEXT VALUE FOR dbo.obs_end AS a; SELECT NEXT VALUE FOR dbo.obs_end AS b; SELECT NEXT VALUE FOR dbo.obs_end AS c;")).ToString());
        Assert.Equal(
            "a: 255 | b: 3",
            (await Run(ends, "SELECT NEXT VALUE FOR dbo.obs_cycle AS a; SELECT NEXT VALUE FOR dbo.obs_cycle AS b;")).ToString());
    }
}

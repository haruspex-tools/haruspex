using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

// Where a name written without a schema is looked for, which depends on where it is written: the rule SchemaSearch and
// SchemaNames.Resolve follow. Each test runs once as dbo and once as obs_other, a user whose default schema is obs_b, so a name
// that followed the caller would read obs_b's object for one of them.
public partial class BindingObservationTests
{
    /// <summary>Two schemas of their own, each in a batch of its own as CREATE SCHEMA needs.</summary>
    private static readonly string[] TwoSchemas = ["CREATE SCHEMA obs_a;", "CREATE SCHEMA obs_b;"];

    /// <summary>
    /// A table, a view, a synonym and an inline function of one name in dbo, obs_a and obs_b, each saying whose it is, and the
    /// table obs_w in each to write to; obs_only_b only obs_b has, and obs_no_a only dbo and obs_b have.
    /// </summary>
    private static readonly string[] Everywhere =
    [
        .. TwoSchemas,
        """
        CREATE TABLE dbo.obs_t (who varchar(5) NOT NULL); INSERT INTO dbo.obs_t VALUES ('dbo');
        CREATE TABLE obs_a.obs_t (who varchar(5) NOT NULL); INSERT INTO obs_a.obs_t VALUES ('a');
        CREATE TABLE obs_b.obs_t (who varchar(5) NOT NULL); INSERT INTO obs_b.obs_t VALUES ('b');
        CREATE TABLE obs_b.obs_only_b (who varchar(5) NOT NULL); INSERT INTO obs_b.obs_only_b VALUES ('b');
        CREATE TABLE dbo.obs_no_a (who varchar(5) NOT NULL); INSERT INTO dbo.obs_no_a VALUES ('dbo');
        CREATE TABLE obs_b.obs_no_a (who varchar(5) NOT NULL); INSERT INTO obs_b.obs_no_a VALUES ('b');
        CREATE TABLE dbo.obs_w (who varchar(20) NOT NULL);
        CREATE TABLE obs_a.obs_w (who varchar(20) NOT NULL);
        CREATE TABLE obs_b.obs_w (who varchar(20) NOT NULL);
        CREATE SYNONYM dbo.obs_s FOR dbo.obs_t;
        CREATE SYNONYM obs_a.obs_s FOR obs_a.obs_t;
        CREATE SYNONYM obs_b.obs_s FOR obs_b.obs_t;
        """,
        "CREATE VIEW dbo.obs_v AS SELECT 'dbo' AS who;",
        "CREATE VIEW obs_a.obs_v AS SELECT 'a' AS who;",
        "CREATE VIEW obs_b.obs_v AS SELECT 'b' AS who;",
        "CREATE FUNCTION dbo.obs_f() RETURNS TABLE AS RETURN SELECT 'dbo' AS who;",
        "CREATE FUNCTION obs_a.obs_f() RETURNS TABLE AS RETURN SELECT 'a' AS who;",
        "CREATE FUNCTION obs_b.obs_f() RETURNS TABLE AS RETURN SELECT 'b' AS who;",
        "CREATE USER obs_other WITHOUT LOGIN WITH DEFAULT_SCHEMA = obs_b; GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE TO obs_other;",
    ];

    /// <summary>
    /// Runs <paramref name="call"/> as dbo and then as obs_other, after <paramref name="setup"/>, giving both outcomes as
    /// <c>dbo: … // obs_other: …</c>.
    /// </summary>
    private async Task<string> AsBoth(string[] setup, string call)
    {
        var asDbo = await Run(setup, call);
        var asOther = await Run(setup, AsOther(call));
        return $"dbo: {asDbo} // obs_other: {asOther}";
    }

    /// <summary>
    /// As <see cref="AsBoth"/>, in a database of the test's own whose setup is committed: a type created in the transaction that
    /// uses it deadlocks (1205, as CheckConstraintObservationTests notes), so the tests of types can't run in the shared one.
    /// </summary>
    private async Task<string> AsBothCommitted(string[] setup, string call)
    {
        await server.RequireAsync();
        var token = TestContext.Current.CancellationToken;
        await using var database = await server.ScratchAsync();
        foreach (var batch in setup)
            await database.ExecuteAsync(batch, token);
        var asDbo = await Committed(database, call, token);
        var asOther = await Committed(database, AsOther(call), token);
        return $"dbo: {asDbo} // obs_other: {asOther}";
    }

    private static async Task<CallOutcome> Committed(ScratchDatabase database, string call, CancellationToken token)
    {
        await using var connection = await database.OpenAsync(token);
#pragma warning disable CA2100 // The test's own batch.
        await using var command = new SqlCommand(call, connection);
#pragma warning restore CA2100
        return await ObservedCall.Read(command, errorsAsMessages: false, token);
    }

    /// <summary>
    /// <paramref name="call"/> as obs_other, reverting whatever it does: an error that ends the batch (5313, say) would
    /// otherwise skip REVERT and hand the pooled connection back still impersonating, and the next test to take it fails
    /// ("the session is in the kill state"). Run as dynamic SQL so that its compile errors are caught too; every call names
    /// its objects with a schema or EXECs them, so compiling it in the inner batch changes nothing it reads.
    /// </summary>
    private static string AsOther(string call) =>
        $"EXECUTE AS USER = 'obs_other'; BEGIN TRY EXEC (N'{call.Replace("'", "''", StringComparison.Ordinal)}'); END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;";

    /// <summary>What <see cref="AsBoth"/> gives when both users see <paramref name="outcome"/>.</summary>
    private static string Both(string outcome) => $"dbo: {outcome} // obs_other: {outcome}";

    [Theory]
    [InlineData("SELECT who FROM obs_t;")]
    [InlineData("SELECT who FROM obs_v;")]
    [InlineData("SELECT who FROM obs_s;")]
    [InlineData("SELECT who FROM obs_f();")]
    // A table, view, synonym or table-valued function named without a schema in a procedure is the procedure's schema's, for
    // every caller: not dbo's for dbo, not obs_b's for obs_other.
    public async Task A_name_in_a_procedure_is_looked_for_in_the_procedures_schema_whoever_calls(string query) =>
        Assert.Equal(
            Both("who: a"),
            await AsBoth([.. Everywhere, $"CREATE PROCEDURE obs_a.obs_p AS {query}"], "EXEC obs_a.obs_p;"));

    [Theory]
    [InlineData("CREATE VIEW obs_a.obs_read AS SELECT who FROM obs_t;", "obs_a.obs_read")]
    [InlineData("CREATE FUNCTION obs_a.obs_read() RETURNS TABLE AS RETURN SELECT who FROM obs_t;", "obs_a.obs_read()")]
    [InlineData("CREATE FUNCTION obs_a.obs_read() RETURNS @r TABLE (who varchar(5)) AS BEGIN INSERT INTO @r SELECT who FROM obs_t; RETURN; END;", "obs_a.obs_read()")]
    // So is one in a view or a function of either kind, read ad hoc or from a dbo procedure: its own schema decides, not the
    // reader's.
    public async Task A_name_in_a_view_or_function_is_looked_for_in_its_own_schema(string module, string read) =>
        Assert.Equal(
            Both("who: a | who: a"),
            await AsBoth([.. Everywhere, module, $"CREATE PROCEDURE dbo.obs_reads AS SELECT who FROM {read};"], $"SELECT who FROM {read}; EXEC dbo.obs_reads;"));

    [Fact]
    public async Task A_name_in_a_trigger_is_looked_for_in_its_tables_schema() =>
        // A trigger is in its table's schema, so its INSERT writes obs_a.obs_w.
        Assert.Equal(
            Both("s,who: a,kept;a,trigger;b,kept;dbo,kept"),
            await AsBoth(
                [.. Everywhere, Kept, "CREATE TRIGGER obs_a.obs_on_t ON obs_a.obs_t AFTER INSERT AS INSERT INTO obs_w VALUES ('trigger');"],
                $"INSERT INTO obs_a.obs_t VALUES ('x'); {EveryW}"));

    [Theory]
    [InlineData("INSERT INTO obs_w VALUES ('insert');", "a,insert;a,kept;b,kept;dbo,kept")]
    [InlineData("UPDATE obs_w SET who = 'update';", "a,update;b,kept;dbo,kept")]
    [InlineData("DELETE FROM obs_w;", "b,kept;dbo,kept")]
    [InlineData("MERGE obs_w AS w USING (SELECT 'merge' AS who) AS s ON 1 = 0 WHEN NOT MATCHED THEN INSERT (who) VALUES (s.who);", "a,kept;a,merge;b,kept;dbo,kept")]
    [InlineData("UPDATE obs_t SET who = who OUTPUT inserted.who INTO obs_w;", "a,a;a,kept;b,kept;dbo,kept")]
    // A write's target is the procedure's schema's too, and so is OUTPUT INTO's: each obs_w holds a row before the call, and
    // only obs_a's changes.
    public async Task A_write_in_a_procedure_changes_the_table_of_the_procedures_schema(string write, string written) =>
        Assert.Equal(
            Both($"s,who: {written}"),
            await AsBoth([.. Everywhere, Kept, $"CREATE PROCEDURE obs_a.obs_p AS {write}"], $"EXEC obs_a.obs_p; {EveryW}"));

    /// <summary>A row in each obs_w, so that a write to the wrong one shows.</summary>
    private const string Kept = "INSERT INTO dbo.obs_w VALUES ('kept'); INSERT INTO obs_a.obs_w VALUES ('kept'); INSERT INTO obs_b.obs_w VALUES ('kept');";

    /// <summary>Every obs_w's rows, each with its schema, in order.</summary>
    private const string EveryW = "SELECT s, who FROM (SELECT 'dbo' AS s, who FROM dbo.obs_w UNION ALL SELECT 'a', who FROM obs_a.obs_w UNION ALL SELECT 'b', who FROM obs_b.obs_w) AS w ORDER BY s, who;";

    [Fact]
    public async Task A_name_the_procedures_schema_lacks_is_dbos_and_never_another_schemas() =>
        // obs_no_a: dbo's for both, though obs_other's own schema has one. obs_only_b: only obs_b has it, and the procedure
        // can't read it even for obs_other (208).
        Assert.Equal(
            Both("who: dbo ! 208"),
            await AsBoth(
                [.. Everywhere, "CREATE PROCEDURE obs_a.obs_p AS BEGIN SELECT who FROM obs_no_a; SELECT who FROM obs_only_b; END;"],
                "EXEC obs_a.obs_p;"));

    [Fact]
    public async Task A_name_in_a_dbo_procedure_is_dbos_for_a_caller_whose_default_schema_has_one() =>
        Assert.Equal(
            Both("who: dbo"),
            await AsBoth([.. Everywhere, "CREATE PROCEDURE dbo.obs_p AS SELECT who FROM obs_t;"], "EXEC dbo.obs_p;"));

    [Theory]
    [InlineData("CREATE PROCEDURE obs_a.obs_p AS EXEC obs_q;")]
    [InlineData("CREATE PROCEDURE obs_a.obs_p AS EXEC sp_executesql N'SELECT who FROM obs_t;';")]
    // Two names keep the caller's rule inside a procedure: what an EXEC runs, and every name in dynamic SQL. obs_q is in all
    // three schemas, and the procedure's own is passed over.
    public async Task An_exec_or_dynamic_sql_in_a_procedure_follows_the_caller(string procedure) =>
        Assert.Equal(
            "dbo: who: dbo // obs_other: who: b",
            await AsBoth(
                [
                    .. Everywhere,
                    "CREATE PROCEDURE dbo.obs_q AS SELECT 'dbo' AS who;",
                    "CREATE PROCEDURE obs_a.obs_q AS SELECT 'a' AS who;",
                    "CREATE PROCEDURE obs_b.obs_q AS SELECT 'b' AS who;",
                    procedure,
                ],
                "EXEC obs_a.obs_p;"));

    /// <summary>An alias type of one name in dbo, obs_a and obs_b, of three base types told apart by SQL_VARIANT_PROPERTY.</summary>
    private static readonly string[] Types =
    [
        .. TwoSchemas,
        "CREATE TYPE dbo.obs_ty FROM int; CREATE TYPE obs_a.obs_ty FROM varchar(5); CREATE TYPE obs_b.obs_ty FROM bigint;",
        "CREATE TYPE dbo.obs_tt AS TABLE (in_dbo int); CREATE TYPE obs_a.obs_tt AS TABLE (in_a int); CREATE TYPE obs_b.obs_tt AS TABLE (in_b int);",
        "CREATE USER obs_other WITHOUT LOGIN WITH DEFAULT_SCHEMA = obs_b; GRANT SELECT, EXECUTE, REFERENCES TO obs_other;",
    ];

    [Theory]
    [InlineData("CREATE PROCEDURE obs_a.obs_p AS BEGIN DECLARE @y obs_ty = '1'; SELECT CAST(SQL_VARIANT_PROPERTY(@y, 'BaseType') AS varchar(20)) AS base; END;", "EXEC obs_a.obs_p;")]
    [InlineData("CREATE PROCEDURE obs_a.obs_p @x obs_ty AS SELECT CAST(SQL_VARIANT_PROPERTY(@x, 'BaseType') AS varchar(20)) AS base;", "EXEC obs_a.obs_p '1';")]
    [InlineData("CREATE PROCEDURE obs_a.obs_p AS BEGIN DECLARE @v TABLE (c obs_ty); INSERT INTO @v VALUES ('1'); SELECT CAST(SQL_VARIANT_PROPERTY(c, 'BaseType') AS varchar(20)) AS base FROM @v; END;", "EXEC obs_a.obs_p;")]
    [InlineData("CREATE PROCEDURE obs_a.obs_p AS SELECT CAST(SQL_VARIANT_PROPERTY(c, 'BaseType') AS varchar(20)) AS base FROM OPENJSON(N'[{\"c\":\"1\"}]') WITH (c obs_ty);", "EXEC obs_a.obs_p;")]
    [InlineData("CREATE FUNCTION obs_a.obs_fs(@a obs_ty) RETURNS obs_ty AS BEGIN RETURN @a; END;", "SELECT CAST(SQL_VARIANT_PROPERTY(obs_a.obs_fs('1'), 'BaseType') AS varchar(20)) AS base;")]
    [InlineData("CREATE FUNCTION obs_a.obs_fs() RETURNS @r TABLE (c obs_ty) AS BEGIN INSERT INTO @r VALUES ('1'); RETURN; END;", "SELECT CAST(SQL_VARIANT_PROPERTY(c, 'BaseType') AS varchar(20)) AS base FROM obs_a.obs_fs();")]
    // An alias type named without a schema in a module's parameters, return type or body (a DECLARE, a table variable's
    // column, OPENJSON's WITH) is the module's schema's: obs_a's varchar, not dbo's int nor obs_b's bigint.
    public async Task An_alias_type_in_a_module_is_looked_for_in_the_modules_schema(string module, string call) =>
        Assert.Equal(Both("base: varchar"), await AsBothCommitted([.. Types, module], call));

    [Fact]
    public async Task A_table_type_parameter_is_looked_for_in_the_modules_schema() =>
        Assert.Equal(
            Both("in_a: 1"),
            await AsBothCommitted([.. Types, "CREATE PROCEDURE obs_a.obs_p @x obs_tt READONLY AS SELECT * FROM @x;"], "DECLARE @a obs_a.obs_tt; INSERT INTO @a VALUES (1); EXEC obs_a.obs_p @a;"));

    [Fact]
    public async Task An_alias_type_in_a_module_another_user_creates_is_still_the_modules_schemas() =>
        // obs_other's default schema is obs_b, and it creates the procedure: the type is still obs_a's.
        Assert.Equal(
            Both("base: varchar"),
            await AsBothCommitted(
                [
                    .. Types,
                    "GRANT CREATE PROCEDURE TO obs_other; GRANT ALTER ON SCHEMA::obs_a TO obs_other;",
                    "EXECUTE AS USER = 'obs_other'; EXEC (N'CREATE PROCEDURE obs_a.obs_p @x obs_ty AS SELECT CAST(SQL_VARIANT_PROPERTY(@x, ''BaseType'') AS varchar(20)) AS base;'); REVERT;",
                ],
                "EXEC obs_a.obs_p '1';"));

    [Theory]
    [InlineData("CREATE TABLE obs_a.obs_made (c obs_ty);", "obs_made", false, "dbo.obs_ty")]
    [InlineData("CREATE TYPE obs_a.obs_made AS TABLE (c obs_ty);", "TT_obs_made_%", false, "dbo.obs_ty")]
    [InlineData("CREATE TABLE obs_a.obs_made (c obs_ty);", "obs_made", true, "obs_b.obs_ty")]
    [InlineData("CREATE TYPE obs_a.obs_made AS TABLE (c obs_ty);", "TT_obs_made_%", true, "obs_b.obs_ty")]
    // A table's or table type's column is not in a module: its alias type is the creator's default schema's (dbo's for dbo,
    // obs_b's for obs_other), though the table is in obs_a. So the project can't say which type it is when several schemas
    // have one of that name. obs_other creates through EXEC, since a statement in the batch of EXECUTE AS compiles as the
    // batch's user.
    public async Task An_alias_type_in_a_tables_column_follows_whoever_creates_it(string create, string table, bool byOther, string type) =>
        Assert.Equal(
            Both($"type: {type}"),
            await AsBothCommitted(
                [
                    .. Types,
                    "GRANT CREATE TABLE, CREATE TYPE TO obs_other; GRANT ALTER ON SCHEMA::obs_a TO obs_other;",
                    byOther ? $"EXECUTE AS USER = 'obs_other'; EXEC (N'{create}'); REVERT;" : create,
                ],
                $"SELECT SCHEMA_NAME(y.schema_id) + '.' + y.name AS type FROM sys.columns AS c JOIN sys.types AS y ON y.user_type_id = c.user_type_id WHERE OBJECT_NAME(c.object_id) LIKE '{table}';"));

    [Fact]
    public async Task A_cast_to_an_alias_type_is_refused() =>
        // CAST and CONVERT take only a system type, alias or not (243, when the batch or module is compiled), so a module that
        // casts to one is never created.
        Assert.Equal(Both(" ! 243"), await AsBothCommitted([.. Types], "EXEC sp_executesql N'SELECT CAST(''1'' AS obs_ty) AS x;';"));

    [Theory]
    [InlineData("CREATE SYNONYM obs_a.obs_syn FOR obs_t;", "who: a")]
    [InlineData("CREATE SYNONYM obs_a.obs_syn FOR obs_no_a;", "who: dbo")]
    [InlineData("CREATE SYNONYM obs_a.obs_syn FOR obs_only_b;", " ! 5313")]
    // A synonym's target written without a schema is looked for in the synonym's schema and then in dbo, whoever reads it, ad
    // hoc or not: never in the caller's default schema (5313 for obs_other, whose own schema has obs_only_b).
    public async Task A_synonyms_target_is_looked_for_in_the_synonyms_schema_then_dbo(string synonym, string outcome) =>
        Assert.Equal(Both(outcome), await AsBoth([.. Everywhere, synonym], "SELECT who FROM obs_a.obs_syn;"));
}

using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Which definition of a procedure, function or view stands once a script that defines it more than once has run, batch by
/// batch as sqlcmd sends it: the rules <c>DefinitionsInEffect</c> in Haruspex.SqlServer.Sources rests on
/// (<c>docs/decisions/0080-the-last-definition-in-a-script-stands.md</c>).
/// </summary>
public partial class ExecObservationTests
{
    /// <summary>A script, as sqlcmd sends it: its batches in order, split at <c>GO</c>.</summary>
    private static string[] Script(string text) => [.. Deployer.Batches(text)];

    private const string Stub = "CREATE PROCEDURE dbo.obs_archive AS RETURN 0;";

    private const string Real = "ALTER PROCEDURE dbo.obs_archive @before int AS SELECT @before AS before;";

    [Fact]
    public async Task A_stub_altered_later_in_its_script_runs_the_alter_with_the_alters_parameters()
    {
        // ALTER replaces the whole definition, parameters included: the stub took none, the procedure now takes @before.
        Assert.Equal("before: 7", (await Run(Script($"{Stub}\nGO\n{Real}"), "EXEC dbo.obs_archive @before = 7;")).ToString());

        // The same when the stub is made by dynamic SQL, which only runs the CREATE when nothing of the name exists yet.
        var guarded = $"IF OBJECT_ID(N'dbo.obs_archive') IS NULL EXEC sp_executesql N'{Stub}';\nGO\n{Real}";
        Assert.Equal("before: 7", (await Run(Script(guarded), "EXEC dbo.obs_archive @before = 7;")).ToString());
        Assert.Equal("before: 7", (await Run([Stub, .. Script(guarded)], "EXEC dbo.obs_archive @before = 7;")).ToString());
    }

    [Fact]
    public async Task An_alter_of_a_procedure_that_does_not_exist_is_refused_and_leaves_none()
    {
        // So an ALTER that runs leaves its own definition, whatever stood before, and one that doesn't leaves no procedure
        // (208, invalid object name), never another definition. Only the result sets are compared: the error caught dooms the
        // test's own transaction, which the batch's end then reports (3998).
        Assert.Equal(
            ["refused: 208", "id: null"],
            (await Run([], $"BEGIN TRY EXEC sp_executesql N'{Real}'; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS refused; END CATCH; SELECT OBJECT_ID(N'dbo.obs_archive') AS id;")).Sets);
    }

    [Theory]
    [InlineData("DROP PROCEDURE dbo.obs_archive;")]
    [InlineData("DROP PROCEDURE IF EXISTS dbo.obs_archive;")]
    [InlineData("IF OBJECT_ID(N'dbo.obs_archive') IS NOT NULL DROP PROCEDURE dbo.obs_archive;")]
    [InlineData("IF OBJECT_ID('dbo.obs_archive', 'P') IS NOT NULL BEGIN DROP PROCEDURE dbo.obs_archive; END")]
    public async Task A_procedure_dropped_and_created_again_runs_its_last_create(string drop)
    {
        var script = $"{Stub}\nGO\n{drop}\nGO\n{Real.Replace("ALTER", "CREATE", StringComparison.Ordinal)}";

        Assert.Equal("before: 7", (await Run(Script(script), "EXEC dbo.obs_archive @before = 7;")).ToString());
    }

    [Theory]
    [InlineData("dbo.obs_archive', 'V")]
    [InlineData("dbo.obs_archive', 'PC")]
    public async Task Object_id_with_a_type_finds_only_an_object_of_that_type_so_a_drop_it_guards_may_not_run(string tested)
    {
        // A T-SQL procedure is P: OBJECT_ID(…, 'V') or (…, 'PC') is null for it, the guarded drop doesn't run, and a CREATE after
        // it is refused (2714), so the first definition stands. Only the result sets are compared, as above.
        var outcome = await Run(
            ["CREATE PROCEDURE dbo.obs_archive AS SELECT 0 AS stub;", $"IF OBJECT_ID(N'{tested}') IS NOT NULL DROP PROCEDURE dbo.obs_archive;"],
            "BEGIN TRY EXEC sp_executesql N'CREATE PROCEDURE dbo.obs_archive AS SELECT 1 AS second;'; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS refused; END CATCH; EXEC dbo.obs_archive;");

        Assert.Equal(["refused: 2714", "stub: 0"], outcome.Sets);
    }

    [Fact]
    public async Task A_second_create_is_refused_so_the_first_stands_and_create_or_alter_replaces_it()
    {
        // As above, only the result sets are compared.
        Assert.Equal(
            ["refused: 2714", "stub: 0"],
            (await Run(
                ["CREATE PROCEDURE dbo.obs_archive AS SELECT 0 AS stub;"],
                $"BEGIN TRY EXEC sp_executesql N'{Real.Replace("ALTER", "CREATE", StringComparison.Ordinal)}'; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS refused; END CATCH; EXEC dbo.obs_archive;")).Sets);
        Assert.Equal(
            "before: 7",
            (await Run(Script($"CREATE OR ALTER PROCEDURE dbo.obs_archive AS RETURN 0;\nGO\n{Real.Replace("ALTER", "CREATE OR ALTER", StringComparison.Ordinal)}"), "EXEC dbo.obs_archive @before = 7;")).ToString());
    }

    [Fact]
    public async Task An_alter_keeps_the_ansi_nulls_setting_in_force_when_it_runs()
    {
        // Not the stub's: an ALTER records its own settings, as a CREATE does.
        const string Compare = "ALTER PROCEDURE dbo.obs_archive AS SELECT CASE WHEN NULL = NULL THEN 1 ELSE 0 END AS equal;";
        Assert.Equal("equal: 0", (await Run(["SET ANSI_NULLS OFF;", Stub, "SET ANSI_NULLS ON;", Compare], "EXEC dbo.obs_archive;")).ToString());
        Assert.Equal("equal: 1", (await Run([Stub, "SET ANSI_NULLS OFF;", Compare, "SET ANSI_NULLS ON;"], "EXEC dbo.obs_archive;")).ToString());
    }

    [Theory]
    [InlineData("ALTER FUNCTION dbo.obs_total() RETURNS TABLE AS RETURN SELECT 2 AS total;", "SELECT dbo.obs_total() AS total;")]
    [InlineData("CREATE OR ALTER FUNCTION dbo.obs_total() RETURNS TABLE AS RETURN SELECT 2 AS total;", "SELECT dbo.obs_total() AS total;")]
    [InlineData("ALTER FUNCTION dbo.obs_lines() RETURNS @t TABLE (total int) AS BEGIN INSERT @t VALUES (2); RETURN; END", "SELECT total FROM dbo.obs_lines();")]
    public async Task A_function_s_kind_cannot_change_by_alter(string change, string read)
    {
        // Scalar to inline, or inline to multi-statement, by ALTER or CREATE OR ALTER: refused (2010), and the earlier stands.
        string[] stubs =
        [
            "CREATE FUNCTION dbo.obs_total() RETURNS int AS BEGIN RETURN 1; END",
            "CREATE FUNCTION dbo.obs_lines() RETURNS TABLE AS RETURN SELECT 1 AS total;",
        ];
        var outcome = await Run(stubs, $"BEGIN TRY EXEC sp_executesql N'{change.Replace("'", "''", StringComparison.Ordinal)}'; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS refused; END CATCH; {read}");

        // Only the result sets are compared, as above.
        Assert.Equal(["refused: 2010", "total: 1"], outcome.Sets);
    }

    [Fact]
    public async Task A_function_and_a_view_altered_or_recreated_in_their_script_have_their_last_definition()
    {
        var script = """
            CREATE FUNCTION dbo.obs_total() RETURNS int AS BEGIN RETURN 0; END
            GO
            ALTER FUNCTION dbo.obs_total() RETURNS decimal(9,2) AS BEGIN RETURN 1.5; END
            GO
            CREATE VIEW dbo.obs_open AS SELECT 1 AS stub;
            GO
            DROP VIEW IF EXISTS dbo.obs_open;
            GO
            CREATE VIEW dbo.obs_open AS SELECT 2 AS order_id;
            GO
            """;

        Assert.Equal("total,order_id: 1.50,2", (await Run(Script(script), "SELECT dbo.obs_total() AS total, v.order_id FROM dbo.obs_open AS v;")).ToString());
    }

    // ---- QUOTED_IDENTIFIER: where a SET takes effect (ScriptParser) -----------------------------------------------------

    private const string Pot = "CREATE TABLE dbo.obs_pot (id int NOT NULL PRIMARY KEY, glaze int NULL); INSERT dbo.obs_pot VALUES (1, 7);";

    /// <summary>Every option <c>SET ANSI_DEFAULTS OFF</c> turns off, on again as a connection has them, outside a transaction's start.</summary>
    private const string Defaults = "SET ANSI_DEFAULTS ON; SET IMPLICIT_TRANSACTIONS OFF; SET CURSOR_CLOSE_ON_COMMIT OFF;";

    [Theory]
    [InlineData("SET QUOTED_IDENTIFIER OFF;")]
    [InlineData("SET ANSI_NULLS, QUOTED_IDENTIFIER OFF;")]
    [InlineData("IF 1 = 0 SET QUOTED_IDENTIFIER OFF;")]
    [InlineData("SET ANSI_DEFAULTS OFF;")]
    public async Task A_quoted_identifier_set_outside_a_body_takes_effect_where_it_is_parsed(string set)
    {
        // It takes effect when the batch is parsed, not when it runs, so under an IF that is false too, and in every batch after:
        // the procedure keeps it, its "glaze" is a string, and the server describes it as one. SET ANSI_DEFAULTS OFF turns it off
        // with the others.
        string[] setup = [Pot, set, "CREATE PROCEDURE dbo.obs_qo AS SELECT \"glaze\" AS g, [glaze] AS g2 FROM dbo.obs_pot;", Defaults];

        Assert.Equal("g,g2: glaze,7", (await Run(setup, "EXEC dbo.obs_qo;")).ToString());
        Assert.Equal(
            "name,system_type_name: g,varchar(5);g2,int",
            (await Run(setup, "SELECT name, system_type_name FROM sys.dm_exec_describe_first_result_set(N'dbo.obs_qo', NULL, 0) ORDER BY column_ordinal;")).ToString());
    }

    [Fact]
    public async Task A_quoted_identifier_set_takes_effect_in_its_own_batch_after_it()
    {
        string[] setup = ["CREATE TABLE dbo.obs_named (id int NOT NULL, glaze AS \"id\"); SET QUOTED_IDENTIFIER OFF; CREATE TABLE dbo.obs_label (id int NOT NULL, glaze AS \"id\"); SET QUOTED_IDENTIFIER ON;"];

        Assert.Equal(
            "named,label: 1,id",
            (await Run([.. setup, "INSERT dbo.obs_named (id) VALUES (1); INSERT dbo.obs_label (id) VALUES (1);"], "SELECT n.glaze AS named, l.glaze AS label FROM dbo.obs_named AS n CROSS JOIN dbo.obs_label AS l;")).ToString());
    }

    [Fact]
    public async Task A_quoted_identifier_set_in_a_body_changes_neither_the_body_nor_the_session()
    {
        // The body is parsed with the setting in force where it is created, and the SET in it, which runs, is undone when it returns.
        string[] setup = [Pot, "CREATE PROCEDURE dbo.obs_body AS SET QUOTED_IDENTIFIER OFF; SELECT \"glaze\" AS g FROM dbo.obs_pot;"];

        Assert.Equal("g: 7 | qi: 1", (await Run(setup, "EXEC dbo.obs_body; SELECT SESSIONPROPERTY('QUOTED_IDENTIFIER') AS qi;")).ToString());
    }

    // ---- synonyms (ScriptReader) ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("DROP SYNONYM dbo.obs_bin;")]
    [InlineData("DROP SYNONYM IF EXISTS dbo.obs_bin;")]
    [InlineData("IF OBJECT_ID(N'dbo.obs_bin') IS NOT NULL DROP SYNONYM dbo.obs_bin;")]
    [InlineData("IF OBJECT_ID('dbo.obs_bin', 'SN') IS NOT NULL BEGIN DROP SYNONYM dbo.obs_bin; END")]
    public async Task A_synonym_dropped_and_created_again_stands_for_its_last_target(string drop)
    {
        // A procedure that reads through it reads the last target, whose column is a nullable varchar, not the first's tinyint.
        string[] setup = [
            "CREATE TABLE dbo.obs_bin_small (weight tinyint NOT NULL); INSERT dbo.obs_bin_small VALUES (3);",
            "CREATE TABLE dbo.obs_bin_large (weight varchar(20) NULL); INSERT dbo.obs_bin_large VALUES (NULL);",
            "CREATE SYNONYM dbo.obs_bin FOR dbo.obs_bin_small;",
            "CREATE PROCEDURE dbo.obs_bins AS SELECT weight FROM dbo.obs_bin;",
            drop,
            "CREATE SYNONYM dbo.obs_bin FOR dbo.obs_bin_large;",
        ];

        Assert.Equal("weight: null", (await Run(setup, "EXEC dbo.obs_bins;")).ToString());
        Assert.Equal(
            "system_type_name,is_nullable: varchar(20),True",
            (await Run(setup, "SELECT system_type_name, is_nullable FROM sys.dm_exec_describe_first_result_set(N'dbo.obs_bins', NULL, 0);")).ToString());
    }

    [Fact]
    public async Task A_synonym_created_in_a_body_stands_for_the_body_s_target()
    {
        // A procedure may drop and create a synonym, and when it runs the synonym moves, as it does under an IF.
        Assert.Equal(
            "target: [dbo].[obs_bin_large] | target: [dbo].[obs_bin_large]",
            (await Run(
                [
                    "CREATE SYNONYM dbo.obs_bin FOR dbo.obs_bin_small;",
                    "CREATE PROCEDURE dbo.obs_move AS DROP SYNONYM dbo.obs_bin; CREATE SYNONYM dbo.obs_bin FOR dbo.obs_bin_large;",
                ],
                "EXEC dbo.obs_move; SELECT base_object_name AS target FROM sys.synonyms WHERE name = N'obs_bin'; "
                + "IF 1 = 1 BEGIN DROP SYNONYM dbo.obs_bin; CREATE SYNONYM dbo.obs_bin FOR dbo.obs_bin_large; END; SELECT base_object_name AS target FROM sys.synonyms WHERE name = N'obs_bin';")).ToString());
    }

    [Fact]
    public async Task A_synonym_created_again_without_a_drop_is_refused()
    {
        // So the first target stands (2714), as a second CREATE of a procedure is refused.
        Assert.Equal(
            ["refused: 2714", "target: [dbo].[obs_bin_small]"],
            (await Run(
                ["CREATE SYNONYM dbo.obs_bin FOR dbo.obs_bin_small;"],
                "BEGIN TRY EXEC sp_executesql N'CREATE SYNONYM dbo.obs_bin FOR dbo.obs_bin_large;'; END TRY BEGIN CATCH SELECT ERROR_NUMBER() AS refused; END CATCH; SELECT base_object_name AS target FROM sys.synonyms WHERE name = N'obs_bin';")).Sets);
    }
}

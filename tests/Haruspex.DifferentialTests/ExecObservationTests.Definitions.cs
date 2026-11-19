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
}

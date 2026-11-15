using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server runs once a deployment has replaced a SQLCMD variable, <c>$(Region)</c>, with its value, and that what haruspex
/// infers from the text as written still holds for it (decision 0046). A deployment puts the value in as text before the server
/// parses anything: observed on 15 Nov 2026 with SqlPackage 170.5.96 publishing a DACPAC (the module's definition in
/// <c>sys.sql_modules</c> holds the value; an empty value given in a publish profile gives <c>''</c>; a value with a quotation mark
/// ends the string) and with sqlcmd running a script (a value from <c>-v</c> or the environment; the script's own <c>:setvar</c>
/// wins over both). Neither tool can run here, so each test replaces the variable as they were seen to, then creates the object
/// and runs it, in a write session that is rolled back.
/// </summary>
public class SqlcmdVariableObservationTests(SqlServerFixture server)
{
    /// <summary>The values a deployment was given: none, one character, a long one, and one that ends the string it is put in.</summary>
    public static TheoryData<string> Values { get; } = ["", "n", "north-west-europe", "the-region-name-that-is-forty-chars-long", "1'+1+'"];

    /// <summary>The values that leave a string a string: in a CHECK, one that turns it into an int makes every insert compare as int.</summary>
    public static TheoryData<string> Strings { get; } = ["", "n", "north-west-europe", "the-region-name-that-is-forty-chars-long"];

    private const string WhoAmI = "CREATE PROCEDURE dbo.obs_whoami AS SELECT '$(Region)' AS region, N'$(Region)' AS region_n, len('$(Region)') AS n;";

    /// <summary>The text as a deployment with <c>Region</c> set to <paramref name="value"/> sends it: the variable replaced as text.</summary>
    private static string Deployed(string sql, string value) => sql.Replace("$(Region)", value, StringComparison.Ordinal);

    /// <summary>Creates the deployed text of <paramref name="create"/>, after <paramref name="setup"/>, and runs <paramref name="procedure"/>; rolled back.</summary>
    private async Task<IReadOnlyList<ObservedResultSet>> Run(string create, string procedure, string? setup = null)
    {
        server.Require();
        await using var session = await server.WriteAsync(TestContext.Current.CancellationToken);
        foreach (var batch in new[] { setup, create }.OfType<string>())
        {
            await using var command = session.Command(batch);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return await ProcedureRunner.RunAsync(session, new ProcedureRun(procedure, "none", procedure, new Dictionary<string, object?>()));
    }

    /// <summary>What haruspex infers from <paramref name="files"/> as written, before any deployment.</summary>
    private static IrDocument Written(string procedure, params (string File, string Sql)[] files) => InferProcedure.Infer(procedure, files);

    [Theory]
    [MemberData(nameof(Values))]
    public async Task A_deployment_puts_the_value_in_place_of_a_variable_in_a_literal_whatever_its_length(string value)
    {
        var observed = await Run(Deployed(WhoAmI, value), "dbo.obs_whoami");

        // The literal is the value, not the nine characters written: any length, the empty one too; and a value holding a
        // quotation mark ends the string, so '$(Region)' became '1'+1+'', the int 2, in both columns.
        var row = Assert.Single(Assert.Single(observed).Rows);
        if (value.Contains('\'', StringComparison.Ordinal))
        {
            Assert.Equal(2, row[0]);
            Assert.Equal(2, row[1]);
        }
        else
        {
            Assert.Equal(value, row[0]);
            Assert.Equal(value.Length, row[2]);
        }
    }

    [Theory]
    [MemberData(nameof(Values))]
    public async Task What_haruspex_infers_from_the_written_literal_holds_for_every_deployed_value(string value)
    {
        var observed = await Run(Deployed(WhoAmI, value), "dbo.obs_whoami");

        Assert.Empty(IrValidator.Validate(Written("dbo.obs_whoami", ("p.sql", WhoAmI)), "dbo.obs_whoami", observed).Violations);
    }

    [Theory]
    [MemberData(nameof(Strings))]
    public async Task A_check_constraint_holds_the_value_and_haruspex_claims_no_set_from_the_written_text(string value)
    {
        const string table = "CREATE TABLE dbo.obs_stall (stall_id int NOT NULL PRIMARY KEY, region varchar(60) NOT NULL CONSTRAINT ck_obs_region CHECK (region IN ('$(Region)', 'south')));";
        const string read = "CREATE PROCEDURE dbo.obs_regions AS SELECT s.region FROM dbo.obs_stall AS s;";

        // The constraint admits the value, which the set written, {'$(Region)', 'south'}, does not hold.
        var observed = await Run(read, "dbo.obs_regions", Deployed(table, value) + $" INSERT INTO dbo.obs_stall VALUES (1, '{value}');");

        Assert.Equal(value, Assert.Single(Assert.Single(observed).Rows)[0]);
        Assert.Empty(IrValidator.Validate(Written("dbo.obs_regions", ("t.sql", table), ("p.sql", read)), "dbo.obs_regions", observed).Violations);
    }

    [Fact]
    public async Task A_deployment_puts_the_value_in_place_of_a_variable_in_a_bracketed_name()
    {
        const string labelled = "CREATE PROCEDURE dbo.obs_labelled AS SELECT 1 AS [$(Region)];";

        var observed = await Run(Deployed(labelled, "zone"), "dbo.obs_labelled");

        // The result's key is the value, so haruspex leaves the result's keys unknown.
        Assert.Equal(["zone"], Assert.Single(observed).Columns);
        var result = Assert.Single(Written("dbo.obs_labelled", ("p.sql", labelled)).Objects[0].Results!);
        Assert.Equal(IrUnknownReason.SqlcmdVariable, Assert.IsType<IrUnknownType>(result.Type).Reason);
    }
}

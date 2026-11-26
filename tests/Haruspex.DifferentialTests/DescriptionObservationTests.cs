using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// How SQL Server takes <c>sp_addextendedproperty</c>, which the script reader follows to read a description without guessing
/// (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md): the arguments it takes as text, the
/// levels it compares ignoring case, and the calls it refuses, which a deployment would stop at, so the reader passes them over.
/// Each test runs in a write session that is rolled back, so nothing it adds stays.
/// </summary>
public class DescriptionObservationTests(SqlServerFixture server)
{
    private const string Table = "CREATE TABLE dbo.obs_dog (dog_id int NOT NULL, name nvarchar(20) NULL);";

    private const string Procedure = "CREATE PROCEDURE dbo.obs_get_dog @dog_id int AS SELECT d.name FROM dbo.obs_dog AS d WHERE d.dog_id = @dog_id;";

    /// <summary>Runs each batch in turn, in a session rolled back afterwards, and returns the last one's rows as name and text.</summary>
    private async Task<List<(string Name, string? Value, string Type)>> Run(params string[] batches)
    {
        await server.RequireAsync();
        await using var session = await server.WriteAsync(TestContext.Current.CancellationToken);
        foreach (var batch in batches[..^1])
        {
            await using var command = session.Command(batch);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var query = session.Command(batches[^1]);
        await using var reader = await query.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<(string, string?, string)>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            rows.Add((reader.GetString(0), await reader.IsDBNullAsync(1, TestContext.Current.CancellationToken) ? null : reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    /// <summary>The properties set on <c>dbo.obs_dog</c> and its columns, each name, value as text and the value's type.</summary>
    private const string OnTheDog = """
        SELECT name, CAST(value AS nvarchar(200)), CAST(SQL_VARIANT_PROPERTY(value, 'BaseType') AS nvarchar(20))
        FROM fn_listextendedproperty(NULL, 'SCHEMA', 'dbo', 'TABLE', 'obs_dog', NULL, NULL)
        UNION ALL
        SELECT name, CAST(value AS nvarchar(200)), CAST(SQL_VARIANT_PROPERTY(value, 'BaseType') AS nvarchar(20))
        FROM fn_listextendedproperty(NULL, 'SCHEMA', 'dbo', 'TABLE', 'obs_dog', 'COLUMN', NULL);
        """;

    [Fact]
    public async Task An_identifier_argument_is_taken_as_the_string_it_names()
    {
        var rows = await Run(Table, "EXEC [sys].[sp_addextendedproperty] N'MS_Description', N'A dog.', N'SCHEMA', [dbo], N'TABLE', [obs_dog];", OnTheDog);

        Assert.Equal([("MS_Description", "A dog.", "nvarchar")], rows);
    }

    [Fact]
    public async Task Positional_arguments_may_be_followed_by_named_ones()
    {
        var rows = await Run(Table, "EXEC sp_addextendedproperty 'MS_Description', @value = 'The name.', @level0type = 'SCHEMA', @level0name = 'dbo', @level1type = 'TABLE', @level1name = 'obs_dog', @level2type = 'COLUMN', @level2name = 'name';", OnTheDog);

        Assert.Equal([("MS_Description", "The name.", "varchar")], rows);
    }

    [Fact]
    public async Task Level_types_and_names_are_compared_ignoring_case()
    {
        // In this database, whose collation ignores case, as the names are.
        var rows = await Run(Table, "EXEC sp_addextendedproperty 'MS_Description', 'The name.', 'schema', 'DBO', 'table', 'OBS_DOG', 'column', 'NAME';", OnTheDog);

        Assert.Equal([("MS_Description", "The name.", "varchar")], rows);
    }

    [Fact]
    public async Task A_value_that_is_a_number_is_kept_as_one()
    {
        var rows = await Run(Table, "EXEC sp_addextendedproperty 'MS_Description', 42, 'SCHEMA', 'dbo', 'TABLE', 'obs_dog';", OnTheDog);

        Assert.Equal([("MS_Description", "42", "int")], rows);
    }

    [Theory]
    [InlineData("MS_Description")]
    [InlineData("ms_description")]
    public async Task A_second_add_of_a_property_is_refused_and_names_differing_only_in_case_are_one_here(string second)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Run(
            Table,
            "EXEC sp_addextendedproperty 'MS_Description', 'A dog.', 'SCHEMA', 'dbo', 'TABLE', 'obs_dog';",
            $"EXEC sp_addextendedproperty '{second}', 'A hound.', 'SCHEMA', 'dbo', 'TABLE', 'obs_dog';",
            OnTheDog));

        Assert.Equal(15233, e.Number);
    }

    [Theory]
    [InlineData("'SCHEMA', 'dbo', 'TABLE', 'obs_cat'")]
    [InlineData("'SCHEMA', 'dbo', 'TABLE', 'obs_dog', 'COLUMN', 'breed'")]
    [InlineData("'SCHEMA', 'dbo', 'PROCEDURE', 'obs_get_dog', 'PARAMETER', 'dog_id'")]
    public async Task A_property_on_what_isn_t_defined_is_refused_and_a_parameter_is_named_with_its_at_sign(string levels)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Run(Table, Procedure, $"EXEC sp_addextendedproperty 'MS_Description', 'Words.', {levels};", OnTheDog));

        Assert.Equal(15135, e.Number);
    }

    [Fact]
    public async Task A_call_through_master_runs_there_and_finds_none_of_this_database_s_tables()
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Run(Table, "EXEC master.sys.sp_addextendedproperty 'MS_Description', 'A dog.', 'SCHEMA', 'dbo', 'TABLE', 'obs_dog';", OnTheDog));

        Assert.Equal(15135, e.Number);
    }

    [Fact]
    public async Task A_parameter_named_with_its_at_sign_takes_a_property()
    {
        var rows = await Run(
            Table,
            Procedure,
            "EXEC sp_addextendedproperty 'MS_Description', 'The dog to find.', 'SCHEMA', 'dbo', 'PROCEDURE', 'obs_get_dog', 'PARAMETER', '@dog_id';",
            """
            SELECT name, CAST(value AS nvarchar(200)), CAST(SQL_VARIANT_PROPERTY(value, 'BaseType') AS nvarchar(20))
            FROM fn_listextendedproperty(NULL, 'SCHEMA', 'dbo', 'PROCEDURE', 'obs_get_dog', 'PARAMETER', '@dog_id');
            """);

        Assert.Equal([("MS_Description", "The dog to find.", "varchar")], rows);
    }
}

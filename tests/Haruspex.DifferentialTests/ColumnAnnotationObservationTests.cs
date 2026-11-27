using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// Where SQL Server takes the extended property <c>haruspex.jsonSchema</c>, which says which levels an annotation can be on, and so
/// which haruspex reads, refuses or warns of (docs/decisions/0141-a-column-s-json-schema-may-be-named-in-the-sql.md). Each test
/// runs in a write session that is rolled back, so nothing it adds stays.
/// </summary>
public class ColumnAnnotationObservationTests(SqlServerFixture server)
{
    private static readonly string[] Objects =
    [
        "CREATE TABLE dbo.obs_order (order_id int NOT NULL, lines_json nvarchar(max) NULL);",
        "CREATE VIEW dbo.obs_order_view AS SELECT o.order_id, o.lines_json FROM dbo.obs_order AS o;",
        "CREATE PROCEDURE dbo.obs_save_order @lines nvarchar(max) AS SELECT @lines AS lines;",
        "CREATE PROCEDURE dbo.obs_save_order;2 @lines nvarchar(max) AS SELECT @lines AS lines;",
        "CREATE FUNCTION dbo.obs_lines_of (@doc nvarchar(max)) RETURNS nvarchar(max) AS BEGIN RETURN @doc; END;",
        "CREATE FUNCTION dbo.obs_order_lines () RETURNS TABLE AS RETURN SELECT o.lines_json FROM dbo.obs_order AS o;",
        "CREATE FUNCTION dbo.obs_order_docs () RETURNS @t TABLE (doc nvarchar(max)) AS BEGIN RETURN; END;",
    ];

    /// <summary>Creates the objects, adds the property at <paramref name="levels"/>, and counts the properties so named on them (the database holds the fixtures' too).</summary>
    private async Task<int> Annotate(string levels)
    {
        await server.RequireAsync();
        await using var session = await server.WriteAsync(TestContext.Current.CancellationToken);
        foreach (var batch in Objects.Append($"EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/lines.schema.json', N'SCHEMA', N'dbo', {levels};"))
        {
            await using var command = session.Command(batch);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var count = session.Command("SELECT COUNT(*) FROM sys.extended_properties WHERE name = N'haruspex.jsonSchema' AND OBJECT_NAME(major_id) LIKE N'obs[_]%';");
        return (int)await count.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("N'TABLE', N'obs_order', N'COLUMN', N'lines_json'")]
    [InlineData("N'VIEW', N'obs_order_view', N'COLUMN', N'lines_json'")]
    [InlineData("N'FUNCTION', N'obs_order_lines', N'COLUMN', N'lines_json'")]
    [InlineData("N'FUNCTION', N'obs_order_docs', N'COLUMN', N'doc'")]
    [InlineData("N'PROCEDURE', N'obs_save_order', N'PARAMETER', N'@lines'")]
    [InlineData("N'FUNCTION', N'obs_lines_of', N'PARAMETER', N'@doc'")]
    public async Task A_table_s_view_s_or_table_function_s_column_and_a_procedure_s_or_function_s_parameter_take_it(string levels) =>
        Assert.Equal(1, await Annotate(levels));

    [Theory]
    [InlineData("N'PROCEDURE', N'obs_save_order;2', N'PARAMETER', N'@lines'")]
    [InlineData("N'PROCEDURE', N'obs_save_order', N'PARAMETER', N'lines'")]
    [InlineData("N'FUNCTION', N'obs_lines_of', N'PARAMETER', N'@RETURN_VALUE'")]
    [InlineData("N'FUNCTION', N'obs_lines_of', N'COLUMN', N'@RETURN_VALUE'")]
    [InlineData("N'TABLE', N'obs_order', N'COLUMN', N'missing'")]
    public async Task A_numbered_procedure_s_parameter_a_parameter_without_its_at_sign_and_a_scalar_function_s_result_are_refused(string levels)
    {
        var e = await Assert.ThrowsAsync<SqlException>(() => Annotate(levels));

        Assert.Equal(15135, e.Number);
    }
}

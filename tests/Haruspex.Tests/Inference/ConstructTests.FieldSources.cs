using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Where a field's value came from (IR 0.25): the table columns it is read from, which <c>haruspex explain</c> starts a field's
/// derivation at. A column that only decides which value, or is read in a subquery, is not one of them.
/// </summary>
public partial class ConstructTests
{
    private static string[] From(IrDocument doc, string name, int result = 0) => [.. Field(doc, name, result).From.Select(c => c.Column)];

    [Fact]
    public void A_bare_column_is_read_from_its_column_with_its_type_and_declaration()
    {
        var doc = Procedure("SELECT t.name FROM dbo.t AS t;");

        Assert.Equal([new IrSourceColumn("dbo.t.name", "nvarchar(10)", "tables.sql:1:50")], Field(doc, "name").From);
    }

    [Fact]
    public void An_aggregate_over_an_outer_join_and_arithmetic_are_read_from_the_columns_inside_them_in_order()
    {
        var doc = Procedure("SELECT max(u.name) AS last_name, t.code + u.t_id AS total FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.id GROUP BY t.id;");

        Assert.Equal(["dbo.u.name"], From(doc, "last_name"));
        Assert.Equal(["dbo.t.code", "dbo.u.t_id"], From(doc, "total"));
    }

    [Fact]
    public void A_column_only_tested_or_read_in_a_subquery_or_a_window_s_order_is_not_where_a_value_comes_from()
    {
        var doc = Procedure("""
            SELECT CASE WHEN t.code > 1 THEN t.name END AS picked,
                   IIF(t.code > 1, 1, 0) AS flag,
                   (SELECT max(u.name) FROM dbo.u AS u WHERE u.t_id = t.id) AS first_u,
                   row_number() OVER (ORDER BY t.code) AS n,
                   count(*) OVER () AS rows_seen,
                   1 AS one
            FROM dbo.t AS t;
            """);

        Assert.Equal(["dbo.t.name"], From(doc, "picked"));
        Assert.Empty(From(doc, "flag"));
        Assert.Empty(From(doc, "first_u"));
        Assert.Empty(From(doc, "n"));
        Assert.Empty(From(doc, "rows_seen"));
        Assert.Empty(From(doc, "one"));
    }

    [Fact]
    public void A_column_of_a_derived_table_or_a_cte_is_traced_to_the_table_column_its_query_reads()
    {
        var doc = Procedure("""
            WITH named AS (SELECT t.id, t.name FROM dbo.t AS t)
            SELECT d.label, n.name FROM (SELECT u.name AS label, u.t_id FROM dbo.u AS u) AS d JOIN named AS n ON n.id = d.t_id;
            """);

        Assert.Equal(["dbo.u.name"], From(doc, "label"));
        Assert.Equal(["dbo.t.name"], From(doc, "name"));
    }

    [Fact]
    public void A_column_of_a_set_operation_is_read_from_each_query_s_column()
    {
        var doc = Procedure("SELECT t.name FROM dbo.t AS t UNION ALL SELECT u.name FROM dbo.u AS u;");

        Assert.Equal(["dbo.t.name", "dbo.u.name"], From(doc, "name"));
    }

    [Fact]
    public void A_star_and_a_for_json_key_are_read_from_their_columns()
    {
        var star = Procedure("SELECT * FROM dbo.u;");
        var json = Procedure("SELECT t.name AS [person.name] FROM dbo.t AS t FOR JSON PATH;");

        Assert.Equal(["dbo.u.name"], From(star, "name"));
        var person = Assert.Single(json.Types.Values, t => t.Fields.Any(f => f.Name == "name"));
        Assert.Equal(["dbo.t.name"], person.Fields.Single(f => f.Name == "name").From.Select(c => c.Column));
    }

    [Fact]
    public void A_document_whose_fields_say_where_they_came_from_is_written_as_ir_0_25()
    {
        var doc = Procedure("SELECT t.name FROM dbo.t AS t;");

        Assert.Equal("0.25", doc.Version);
        Assert.StartsWith("{\n  \"irVersion\": \"0.25\",\n", IrJsonWriter.Write(doc), StringComparison.Ordinal);
    }
}

using System.Text.Json;
using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What the mssql driver hands an application for each form of JSON a procedure can return, with its <c>parseJSON</c>
/// setting on and off, observed through the version <c>tests/emit-check</c> pins (12.7.14). The generated schemas are
/// written to accept exactly this (decision 0048, and the project's README on reading results), and
/// <c>MssqlDriverAcceptanceTests</c> parses it with them. The driver recognises a top-level FOR JSON result set by the name
/// SQL Server gives its one column, joins the rows it is split over into one row, and with <c>parseJSON</c> parses it.
/// </summary>
public class MssqlDriverObservationTests(SqlServerFixture server)
{
    /// <summary>The name SQL Server gives the one column of a top-level FOR JSON result set.</summary>
    private const string JsonColumn = "JSON_F52E2B61-18A1-11d1-B105-00805F49916B";

    private const string XmlColumn = "XML_F52E2B61-18A1-11d1-B105-00805F49916B";

    /// <summary>Runs a batch with parseJSON on and then off: what the driver handed over each time.</summary>
    private async Task<(MssqlDriver.Response On, MssqlDriver.Response Off)> Both(string batch, params string[] setup)
    {
        var calls = await MssqlDriver.RunAsync(
            server,
            null,
            setup,
            [new("on", ParseJson: true, Batch: batch), new("off", ParseJson: false, Batch: batch)],
            TestContext.Current.CancellationToken);
        return (calls[0], calls[1]);
    }

    /// <summary>The one row of the one result set a call returned.</summary>
    private static (MssqlDriver.Recordset Set, JsonElement Row) OnlyRow(MssqlDriver.Response response)
    {
        Assert.Null(response.Error);
        var set = Assert.Single(response.Recordsets);
        return (set, Assert.Single(set.Rows));
    }

    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);

    // ---- what the driver recognises -----------------------------------------------------------------

    [Theory]
    [InlineData("FOR JSON PATH", """[{"id":1,"name":"a"},{"id":2,"name":"b"}]""")]
    [InlineData("FOR JSON AUTO", """[{"id":1,"name":"a"},{"id":2,"name":"b"}]""")]
    [InlineData("FOR JSON PATH, WITHOUT_ARRAY_WRAPPER", """{"id":1,"name":"a"},{"id":2,"name":"b"}""")]
    [InlineData("FOR JSON PATH, ROOT('shop')", """{"shop":[{"id":1,"name":"a"},{"id":2,"name":"b"}]}""")]
    public async Task A_top_level_for_json_result_is_one_row_holding_the_document_parsed_or_as_text(string clause, string text)
    {
        var (on, off) = await Both($"SELECT v.id, v.name FROM (VALUES (1, N'a'), (2, N'b')) AS v(id, name) ORDER BY v.id {clause};");

        // Two objects without the array wrapper are not one JSON document, which parseJSON reports (below), so only
        // the text is compared there.
        if (!clause.Contains("WITHOUT_ARRAY_WRAPPER", StringComparison.Ordinal))
        {
            var (set, row) = OnlyRow(on);
            Assert.Equal([JsonColumn], set.Columns);
            Assert.Equal(text, Compact(row));
        }

        var (textSet, textRow) = OnlyRow(off);
        Assert.Equal([JsonColumn], textSet.Columns);
        Assert.Equal(text, textRow.GetProperty(JsonColumn).GetString());
    }

    [Fact]
    public async Task One_object_without_the_array_wrapper_is_parsed_into_that_object()
    {
        var (on, _) = await Both("SELECT v.id FROM (VALUES (1)) AS v(id) FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;");

        Assert.Equal("""{"id":1}""", Compact(OnlyRow(on).Row));
    }

    [Fact]
    public async Task Text_that_does_not_parse_fails_the_call_with_ejson()
    {
        // Several rows without the array wrapper are {…},{…}, which is not JSON: the driver throws, rather than handing
        // over part of it. The IR's README relies on this to claim one object where it can't prove one row.
        var (on, off) = await Both("SELECT v.id FROM (VALUES (1), (2)) AS v(id) FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;");

        Assert.Equal("EJSON", on.Error?.Code);
        Assert.Equal("""{"id":1},{"id":2}""", OnlyRow(off).Row.GetProperty(JsonColumn).GetString());
    }

    [Theory]
    [InlineData("FOR JSON PATH")]
    [InlineData("FOR JSON AUTO")]
    [InlineData("FOR JSON PATH, WITHOUT_ARRAY_WRAPPER")]
    [InlineData("FOR JSON PATH, ROOT('shop')")]
    [InlineData("FOR JSON PATH, INCLUDE_NULL_VALUES")]
    public async Task No_rows_is_one_row_of_null_parsed_or_of_empty_text(string clause)
    {
        // SQL Server returns a result set with no row in it (ForJsonObservationTests); the driver still hands over one row.
        var (on, off) = await Both($"SELECT v.id FROM (VALUES (1)) AS v(id) WHERE v.id = 0 {clause};");

        Assert.Equal(JsonValueKind.Null, OnlyRow(on).Row.ValueKind);
        Assert.Equal("", OnlyRow(off).Row.GetProperty(JsonColumn).GetString());
    }

    [Fact]
    public async Task A_document_split_over_many_rows_is_joined_into_one()
    {
        // About 100,000 characters, which SQL Server splits over rows of about 2,000.
        var (on, off) = await Both(
            "SELECT TOP (3000) v.n, REPLICATE(N'x', 20) AS pad FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n " +
            "FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS v ORDER BY v.n FOR JSON PATH;");

        var document = OnlyRow(on).Row;
        Assert.Equal(3000, document.GetArrayLength());
        Assert.Equal(3000, document[2999].GetProperty("n").GetInt32());
        Assert.Equal(Compact(document), OnlyRow(off).Row.GetProperty(JsonColumn).GetString());
    }

    [Fact]
    public async Task Each_result_set_is_handed_over_alone_rowsets_as_rows_and_json_as_one_row()
    {
        var (on, _) = await Both(
            "SELECT 1 AS a; SELECT v.id FROM (VALUES (1)) AS v(id) FOR JSON PATH; " +
            "SELECT v.id FROM (VALUES (1)) AS v(id) WHERE v.id = 0 FOR JSON PATH; SELECT v.b FROM (VALUES (2), (3)) AS v(b);");

        Assert.Null(on.Error);
        Assert.Equal(["""[{"a":1}]""", """[[{"id":1}]]""", "[null]", """[{"b":2},{"b":3}]"""], on.Recordsets.Select(s => $"[{string.Join(",", s.Rows.Select(Compact))}]"));
        Assert.Equal(["a", JsonColumn, JsonColumn, "b"], on.Recordsets.Select(s => Assert.Single(s.Columns)));
    }

    [Fact]
    public async Task A_procedure_called_by_name_hands_over_the_same()
    {
        // execute, as an application calls a procedure, goes through another path of the driver's code than a batch.
        var calls = await MssqlDriver.RunAsync(
            server,
            null,
            ["CREATE PROCEDURE dbo.obs_driver_json @id int AS SELECT v.id FROM (VALUES (1)) AS v(id) WHERE v.id = @id FOR JSON PATH;"],
            [
                new("found", ParseJson: true, Procedure: "dbo.obs_driver_json", Parameters: new Dictionary<string, object?> { ["id"] = 1 }),
                new("none", ParseJson: true, Procedure: "dbo.obs_driver_json", Parameters: new Dictionary<string, object?> { ["id"] = 0 }),
                new("none as text", ParseJson: false, Procedure: "dbo.obs_driver_json", Parameters: new Dictionary<string, object?> { ["id"] = 0 }),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal("""[{"id":1}]""", Compact(OnlyRow(calls[0]).Row));
        Assert.Equal(JsonValueKind.Null, OnlyRow(calls[1]).Row.ValueKind);
        Assert.Equal("", OnlyRow(calls[2]).Row.GetProperty(JsonColumn).GetString());
    }

    [Fact]
    public async Task A_result_set_whose_statement_does_not_run_is_not_handed_over()
    {
        // Not an empty result set, and not a row of null: the next result set takes its place (the IR's not-run).
        var calls = await MssqlDriver.RunAsync(
            server,
            null,
            ["CREATE PROCEDURE dbo.obs_driver_branch @x int AS IF @x = 1 SELECT v.id FROM (VALUES (1)) AS v(id) FOR JSON PATH; SELECT 2 AS b;"],
            [new("skipped", ParseJson: true, Procedure: "dbo.obs_driver_branch", Parameters: new Dictionary<string, object?> { ["x"] = 0 })],
            TestContext.Current.CancellationToken);

        Assert.Equal(["b"], Assert.Single(calls[0].Recordsets).Columns);
    }

    // ---- what it does not recognise -----------------------------------------------------------------

    [Theory]
    [InlineData("SELECT (SELECT v.id FROM (VALUES (1)) AS v(id) FOR JSON PATH) AS doc;", "doc", """[{"id":1}]""")]
    [InlineData("SELECT (SELECT v.id FROM (VALUES (1)) AS v(id) FOR JSON PATH);", "", """[{"id":1}]""")]
    [InlineData("SELECT json_object('id': 1) AS doc;", "doc", """{"id":1}""")]
    [InlineData("SELECT json_object('id': 1 RETURNING json) AS doc;", "doc", """{"id":1}""")]
    [InlineData("SELECT CAST(N'{\"id\":1}' AS json) AS doc;", "doc", """{"id":1}""")]
    public async Task Json_in_a_column_of_a_rowset_stays_text_whatever_parse_json_says(string batch, string column, string text)
    {
        var (on, off) = await Both(batch);

        foreach (var response in new[] { on, off })
        {
            var (set, row) = OnlyRow(response);
            Assert.Equal([column], set.Columns);
            Assert.Equal(text, row.GetProperty(column).GetString());
        }
    }

    [Fact]
    public async Task For_json_beside_another_column_is_an_ordinary_rowset()
    {
        var (on, _) = await Both("SELECT 1 AS a, (SELECT v.id FROM (VALUES (1)) AS v(id) FOR JSON PATH) AS doc;");

        var (set, row) = OnlyRow(on);
        Assert.Equal(["a", "doc"], set.Columns);
        Assert.Equal(1, row.GetProperty("a").GetInt32());
        Assert.Equal("""[{"id":1}]""", row.GetProperty("doc").GetString());
    }

    [Fact]
    public async Task A_column_named_as_for_json_output_is_taken_for_it_whatever_holds_it()
    {
        // The driver goes by the name alone, so a rowset whose one column is given that name is parsed too.
        var (on, _) = await Both($"SELECT N'[1,2]' AS [{JsonColumn}];");

        Assert.Equal("[1,2]", Compact(OnlyRow(on).Row));
    }

    [Theory]
    [InlineData("", "<r><id>1</id></r><r><id>2</id></r>")]
    [InlineData("WHERE v.id = 0", "")]
    public async Task For_xml_is_joined_into_one_row_and_never_parsed(string filter, string text)
    {
        var (on, off) = await Both($"SELECT v.id FROM (VALUES (1), (2)) AS v(id) {filter} FOR XML PATH('r');");

        foreach (var response in new[] { on, off })
        {
            var (set, row) = OnlyRow(response);
            Assert.Equal([XmlColumn], set.Columns);
            Assert.Equal(text, row.GetProperty(XmlColumn).GetString());
        }
    }
}

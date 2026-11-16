using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// CAST and CONVERT of JSON built here. Observed: Converted_json_stays_json_only_as_its_own_type_or_json and
// A_conversion_cuts_or_pads_json_as_any_string_and_varchar_replaces_what_its_code_page_lacks.
public partial class JsonTracingTests
{
    private const string LineQuery = "(SELECT l.sku, l.qty FROM sales.order_line AS l WHERE l.order_id = o.order_id FOR JSON PATH)";

    [Theory]
    [InlineData("CAST({0} AS nvarchar(max))")]
    [InlineData("CONVERT(nvarchar(max), {0})")]
    [InlineData("TRY_CAST({0} AS nvarchar(max))")]
    [InlineData("CAST(CAST({0} AS nvarchar(max)) AS nvarchar(max))")]
    [InlineData("CAST({0} AS json)")]
    [InlineData("CAST(CAST({0} AS json) AS json)")]
    [InlineData("CONVERT(json, {0}, 1)")]
    public void Json_converted_to_its_own_type_or_json_nests_as_json(string conversion)
    {
        var doc = Infer($"SELECT o.order_id, {conversion.Replace("{0}", LineQuery, StringComparison.Ordinal)} AS lines FROM sales.[order] AS o FOR JSON PATH;");

        var lines = Field(doc, "lines");
        AssertLines(doc, lines.Type);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(lines));
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    [InlineData("CAST({0} AS nvarchar(max))")]
    [InlineData("CAST({0} AS json)")]
    public void Json_converted_to_its_own_type_or_json_is_json_text_in_a_rowset(string conversion)
    {
        var doc = Infer($"SELECT {conversion.Replace("{0}", LineQuery, StringComparison.Ordinal)} AS lines FROM sales.[order] AS o;");

        var lines = Field(doc, "lines");
        AssertLines(doc, Assert.IsType<IrJsonText>(lines.Type).Content!);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(lines));
    }

    [Theory]
    [InlineData("CAST(CAST({0} AS json) AS nvarchar(max))")]
    [InlineData("CONVERT(nvarchar(max), {0}, 0)")]
    [InlineData("TRY_CONVERT(nvarchar(max), {0}, 1)")]
    public void Json_of_another_type_or_converted_with_a_style_to_nvarchar_max_is_text_holding_it(string conversion)
    {
        var doc = Infer($"SELECT o.order_id, {conversion.Replace("{0}", LineQuery, StringComparison.Ordinal)} AS lines FROM sales.[order] AS o FOR JSON PATH;");

        var lines = Field(doc, "lines");
        AssertLines(doc, Assert.IsType<IrJsonText>(lines.Type).Content!);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(lines));
    }

    [Theory]
    [InlineData("nvarchar(4000)", "nvarchar(4000)")]
    [InlineData("nvarchar(10)", "nvarchar(10)")]
    [InlineData("varchar(max)", "varchar(max)")]
    [InlineData("varchar(100)", "varchar(100)")]
    [InlineData("nchar(20)", "nchar(20)")]
    [InlineData("nvarchar", "nvarchar(30)")]
    public void Json_converted_to_another_string_type_is_a_string_of_that_type(string target, string sql)
    {
        var doc = Infer($"SELECT o.order_id, CAST({LineQuery} AS {target}) AS lines FROM sales.[order] AS o FOR JSON PATH;");

        var lines = Field(doc, "lines");
        var scalar = Assert.IsType<IrScalar>(lines.Type);
        Assert.Equal(IrScalarKind.String, scalar.Kind);
        Assert.Equal(sql, scalar.Sql);
        Assert.Equal([IrNullRule.EmptySubquery], Rules(lines));
    }

    [Fact]
    public void Json_text_converted_to_json_is_json_again()
    {
        var doc = Infer("SELECT o.order_id, CAST((SELECT o.total FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS json) AS detail FROM sales.[order] AS o FOR JSON PATH;");

        var detail = Field(doc, "detail");
        Assert.Equal(["total"], Names(doc, detail.Type));
        Assert.Empty(doc.Unknowns);
    }

    [Theory]
    // json_query's type follows its argument's: nvarchar(4000) over a shorter string, so whether either cast keeps it JSON is
    // not known here.
    [InlineData("nvarchar(max)")]
    [InlineData("nvarchar(4000)")]
    public void Json_of_a_type_not_known_converted_to_a_type_it_may_have_is_unknown(string target)
    {
        var doc = Infer($$"""SELECT CAST(json_query(N'{"a": 1}') AS {{target}}) AS x FOR JSON PATH;""");

        Assert.Contains("depends on its own type", Unknown(doc, "x").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_converted_to_a_type_that_is_not_a_string_is_unknown()
    {
        var doc = Infer($"SELECT o.order_id, CAST({LineQuery} AS int) AS lines FROM sales.[order] AS o FOR JSON PATH;");

        Assert.Contains("a conversion of JSON is not modelled yet", Unknown(doc, "lines").Detail, StringComparison.Ordinal);
    }
}

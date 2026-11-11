namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Fact]
    public void String_agg_is_8000_bytes_unless_max_and_null_over_no_rows_or_only_nulls()
    {
        // String_agg_is_8000_bytes_unless_max_and_null_over_no_rows_or_only_nulls;
        // String_agg_past_its_limit_is_an_error_even_under_the_callers_ansi_warnings_and_arithabort_off.
        var doc = Procedure("""
            SELECT STRING_AGG(s, ',') AS a1, STRING_AGG(ns, ',') AS a2, STRING_AGG(vm, ',') WITHIN GROUP (ORDER BY id) AS a3, STRING_AGG(c, '-') AS a4,
                   STRING_AGG(n, ',') AS a5, STRING_AGG(nm, N',') AS a6, STRING_AGG(da, ',') AS a7, ISNULL(STRING_AGG(s, ','), '') AS a8
            FROM dbo.t;
            """);
        Assert.Equal("varchar(8000) empty-aggregate", Typed(doc, "a1"));
        Assert.Equal("nvarchar(4000) nullable-column empty-aggregate", Typed(doc, "a2"));
        Assert.Equal("varchar(max) nullable-column empty-aggregate", Typed(doc, "a3"));
        Assert.Equal("varchar(8000) empty-aggregate", Typed(doc, "a4"));
        Assert.Equal("nvarchar(4000) empty-aggregate", Typed(doc, "a5"));
        Assert.Equal("nvarchar(max) nullable-column empty-aggregate", Typed(doc, "a6"));
        Assert.Equal("nvarchar(4000) empty-aggregate", Typed(doc, "a7"));
        Assert.Equal("varchar(8000)", Typed(doc, "a8"));

        var grouped = Procedure("SELECT g, STRING_AGG(s, ',') AS a, STRING_AGG(ns, ',') AS b FROM dbo.t GROUP BY g;");
        Assert.Equal("varchar(8000)", Typed(grouped, "a"));
        Assert.Equal("nvarchar(4000) nullable-column", Typed(grouped, "b"));
    }
}

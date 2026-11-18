using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Inference;

/// <summary>A query file's <c>-- @sets</c> comment: read where the file is, and checked as a procedure's <c>sets</c> is.</summary>
public partial class QueryFileTests
{
    private const string TwoSets = "-- @sets customers, orders\nSELECT c.customer_id FROM shop.customers AS c;\nSELECT o.order_id FROM shop.orders AS o;\n";

    /// <summary>The query file <c>sql/q.sql</c> holding <paramref name="query"/>, through the config file's naming, as <c>generate</c> runs it.</summary>
    private static Outcome<IrDocument> Generated(string query)
    {
        using var dir = new TempDirectory();
        dir.File("db/schema.sql", Shop);
        dir.File("sql/q.sql", query);
        var config = ConfigReader.Read("{ \"queries\": \"sql\" }"u8.ToArray(), Path.Combine(dir.Path, HaruspexConfig.FileName), dir.Path, new ReservedWords(TypeScriptNames.Language, TypeScriptNames.IsReserved)).Expected();
        var files = QuerySelection.Select(config).Expected().Select(f => QueryFileReader.Read(f.Path, f.Relative).Expected()).ToList();
        return SqlProject.Load(Path.Combine(dir.Path, "db")).WithQueries(dir.Path, files).Infer([], config);
    }

    [Fact]
    public void A_sets_comment_is_read_in_order_where_it_is_written()
    {
        var file = QueryFileReader.Parse(TwoSets, "q.sql").Expected();

        Assert.Equal(["customers", "orders"], file.Sets);
        Assert.Equal(1, file.SetsAt?.Line);
    }

    [Theory]
    [InlineData("-- @sets\n", "-- @sets: the sets are named between commas, in order, such as -- @sets order, lines.")]
    [InlineData("-- @sets a,, b\n", "-- @sets a,, b: the sets are named between commas, in order, such as -- @sets order, lines.")]
    public void A_sets_comment_that_names_no_set_or_leaves_one_out_is_refused(string comment, string why) =>
        Assert.Equal([$"q.sql:1:1: error HAR1018: {why}"], Refused(comment + "SELECT 1 AS one;\n"));

    [Fact]
    public void A_second_sets_comment_is_refused_at_it() =>
        Assert.Equal(
            ["q.sql:2:1: error HAR1018: -- @sets rows: the sets are named already, at line 1; name them once."],
            Refused("-- @sets ones\n-- @sets rows\nSELECT 1 AS one;\n"));

    [Fact]
    public void A_query_s_named_sets_are_its_results_names_and_its_types_follow_them()
    {
        var query = Generated(TwoSets).Expected().Query();

        Assert.Equal(["customers", "orders"], query.Results!.Select(r => r.Name));
        Assert.Equal("QCustomersRow", ((IrRef)((IrArray)query.Results![0].Type).Items).Name);
    }

    [Theory]
    [InlineData("-- @sets customers\n", "error HAR4112: names one result set, but sql/q.sql returns 2 result sets. Name every set it returns, in order, or none.")]
    [InlineData("-- @sets customers, delete\n", "error HAR3023: -- @sets: 'delete' can't name a set: a set's name is a key in camel case, a lower-case ASCII letter then letters and digits, and not a reserved word.")]
    [InlineData("-- @sets rows, rows\n", "error HAR3023: -- @sets: 'rows' names two sets; give each its own name.")]
    public void A_query_s_sets_are_checked_as_a_procedure_s_are_at_its_comment(string comment, string expected)
    {
        var problem = Assert.Single(Generated(comment + "SELECT c.customer_id FROM shop.customers AS c;\nSELECT o.order_id FROM shop.orders AS o;\n").Diagnostics, d => d.IsError);

        Assert.Equal(new SourceLocation("sql/q.sql", 1, 1), problem.At);
        Assert.EndsWith(expected, problem.ToString(), StringComparison.Ordinal);
    }
}

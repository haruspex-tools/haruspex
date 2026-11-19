using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A query file's <c>-- @module</c> comment: the module of a split config file it belongs to, read where the file is
/// (docs/decisions/0074-a-config-may-split-its-output-into-modules.md).
/// </summary>
public partial class QueryFileTests
{
    [Fact]
    public void A_module_comment_is_read_where_it_is_written()
    {
        var file = QueryFileReader.Parse("SELECT 1 AS one;\n-- @module reports\n", "q.sql").Expected();

        Assert.Equal("reports", file.Module);
        Assert.Equal(2, file.ModuleAt?.Line);
    }

    [Fact]
    public void A_file_without_one_names_no_module() =>
        Assert.Null(QueryFileReader.Parse("SELECT 1 AS one;\n", "q.sql").Expected().Module);

    [Theory]
    [InlineData("-- @module\n", "-- @module: the module is named by one word, such as -- @module reports.")]
    [InlineData("-- @module sales reports\n", "-- @module sales reports: the module is named by one word, such as -- @module reports.")]
    public void A_module_comment_that_names_none_or_more_than_one_is_refused(string comment, string why) =>
        Assert.Equal([$"q.sql:1:1: error HAR1019: {why}"], Refused(comment + "SELECT 1 AS one;\n"));

    [Fact]
    public void A_second_module_comment_is_refused_at_it() =>
        Assert.Equal(
            ["q.sql:2:1: error HAR1019: -- @module stock: the module is named already, at line 1; name it once."],
            Refused("-- @module sales\n-- @module stock\nSELECT 1 AS one;\n"));
}

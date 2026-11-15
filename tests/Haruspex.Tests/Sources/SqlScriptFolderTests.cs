using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>Which of a folder's scripts Sources reads: all of them, or the ones named, as a project file names them.</summary>
public class SqlScriptFolderTests
{
    [Fact]
    public void Reads_only_the_files_it_is_given()
    {
        using var dir = new TempDirectory();
        dir.File("Tables/orders.sql", "CREATE TABLE dbo.orders (id int NOT NULL);");
        dir.File("Deployment/load.sql", ":r .\\orders.csv.sql");

        var read = SqlScriptFolder.Read(dir.Path, ["Tables\\orders.sql"]);

        Assert.Empty(read.Diagnostics);
        Assert.Equal("Tables/orders.sql", read.Model.FindTable(new ObjectName("dbo", "orders"))?.Span.File);
    }

    [Fact]
    public void A_procedure_only_altered_is_in_source_order_among_those_created()
    {
        // Its definition is kept apart until every script is read; the model still puts it where it is.
        using var dir = new TempDirectory();
        dir.File("a.sql", "ALTER PROCEDURE dbo.zeta AS SELECT 1 AS x;");
        dir.File("b.sql", "CREATE PROCEDURE dbo.alpha AS SELECT 1 AS x;");
        dir.File("c.sql", "CREATE PROCEDURE dbo.mu AS SELECT 1 AS x;");

        var read = SqlScriptFolder.Read(dir.Path);

        Assert.Equal(["dbo.zeta", "dbo.alpha", "dbo.mu"], read.Model.Procedures.Select(p => p.Name.ToString()));
    }

    [Fact]
    public void An_edge_table_that_declares_no_columns_is_a_graph_table_with_none_listed()
    {
        // Observed: An_edge_table_may_declare_no_columns_and_its_star_is_its_pseudo_columns. Read as its package reads it
        // (DacpacReaderTests, A_graph_table_is_marked_and_its_pseudo_columns_are_not_listed).
        using var dir = new TempDirectory();
        dir.File("follows.sql", "CREATE TABLE shop.follows AS EDGE;");

        var read = SqlScriptFolder.Read(dir.Path);

        Assert.Empty(read.Diagnostics);
        var follows = read.Model.FindTable(new ObjectName("shop", "follows"))!;
        Assert.True(follows.IsGraph);
        Assert.Empty(follows.Columns);
    }

    [Theory]
    [InlineData("CREATE TABLE shop.docs AS FILETABLE WITH (FILETABLE_DIRECTORY = 'docs');", "a FileTable")]
    [InlineData("CREATE TABLE shop.copy WITH (DISTRIBUTION = ROUND_ROBIN) AS SELECT 1 AS a;", "made from a query")]
    public void A_table_whose_definition_lists_no_columns_is_passed_over_with_a_note(string create, string kind)
    {
        // Its columns are the FileTable's fixed ones or its query's, which the reader doesn't model; a name in it is then
        // unresolved, never guessed.
        using var dir = new TempDirectory();
        dir.File("t.sql", create);

        var read = SqlScriptFolder.Read(dir.Path);

        var note = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.ColumnsNotListed, note.Code);
        Assert.Contains(kind, note.Message, StringComparison.Ordinal);
        Assert.Empty(read.Model.Tables);
    }

    [Fact]
    public void A_file_it_is_given_that_does_not_exist_is_an_error()
    {
        using var dir = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() => SqlScriptFolder.Read(dir.Path, ["Tables/missing.sql"]));
    }
}

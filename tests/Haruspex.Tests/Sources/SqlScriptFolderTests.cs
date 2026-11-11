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
        Assert.Equal("Tables/orders.sql", read.Model.FindTable(new ObjectName("dbo", "orders"))?.At.File);
    }

    [Fact]
    public void A_file_it_is_given_that_does_not_exist_is_an_error()
    {
        using var dir = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() => SqlScriptFolder.Read(dir.Path, ["Tables/missing.sql"]));
    }
}

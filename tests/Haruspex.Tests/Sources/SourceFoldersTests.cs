using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// A database read from several folders (<c>source</c> as a list): read as one, every script once, with each location relative to
/// the folder the list is written in, so that it says which folder its script is in (decision 0127).
/// </summary>
public class SourceFoldersTests
{
    private const string Ingot = "CREATE TABLE dbo.ingot (id int NOT NULL PRIMARY KEY, weight int NOT NULL);";

    private const string Weigh = "CREATE PROCEDURE dbo.weigh AS SELECT id, weight FROM dbo.ingot;";

    [Fact]
    public void Folders_side_by_side_are_read_as_one_database_each_location_saying_its_folder()
    {
        using var dir = new TempDirectory();
        dir.File("tables/ingot.sql", Ingot);
        dir.File("procedures/weigh.sql", Weigh);
        dir.File("queries/heaviest.sql", "SELECT TOP (1) id FROM dbo.ingot ORDER BY weight DESC;");

        var read = SqlScriptFolder.ReadFolders(dir.Path, ["tables", "procedures"]);

        Assert.Empty(read.Diagnostics);
        Assert.Equal("tables/ingot.sql", Assert.Single(read.Model.Tables).Span.File);
        Assert.Equal("procedures/weigh.sql", Assert.Single(read.Model.Procedures).Span.File);
    }

    [Fact]
    public void Folders_are_read_alike_whatever_order_they_are_listed_in()
    {
        using var dir = new TempDirectory();
        dir.File("tables/ingot.sql", Ingot);
        dir.File("procedures/weigh.sql", Weigh);

        var listed = SqlScriptFolder.ReadFolders(dir.Path, ["tables", "procedures"]);
        var reversed = SqlScriptFolder.ReadFolders(dir.Path, ["procedures", Path.Combine(dir.Path, "tables")]);

        Assert.Equal(listed.Model.Tables.Select(t => t.Span), reversed.Model.Tables.Select(t => t.Span));
        Assert.Equal(listed.Model.Procedures.Select(p => p.Span), reversed.Model.Procedures.Select(p => p.Span));
    }

    [Fact]
    public void A_folder_listed_inside_another_is_an_error_naming_both_and_nothing_is_read()
    {
        using var dir = new TempDirectory();
        dir.File("db/tables/ingot.sql", Ingot);
        dir.File("db/weigh.sql", Weigh);

        var read = SqlScriptFolder.ReadFolders(dir.Path, ["db/tables", "db"]);

        var error = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.FoldersOverlap, error.Code);
        Assert.Contains("db/tables is inside db", error.Message, StringComparison.Ordinal);
        Assert.Empty(read.Model.Tables);
        Assert.Empty(read.Model.Procedures);
    }

    [Fact]
    public void The_same_folder_listed_twice_in_two_spellings_is_an_error_naming_both()
    {
        using var dir = new TempDirectory();
        dir.File("db/ingot.sql", Ingot);

        var error = Assert.Single(SqlScriptFolder.ReadFolders(dir.Path, ["db", "./db/"]).Diagnostics);

        Assert.Equal(SourceCodes.FoldersOverlap, error.Code);
        Assert.Contains("db and ./db/ are the same folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_folder_listed_in_another_case_where_the_file_system_ignores_case_is_an_error_naming_both()
    {
        using var dir = new TempDirectory();
        dir.File("Tables/ingot.sql", Ingot);
        if (!Directory.Exists(Path.Combine(dir.Path, "tables")))
            Assert.Skip("this file system tells case apart, so tables and Tables would be two folders");

        var error = Assert.Single(SqlScriptFolder.ReadFolders(dir.Path, ["Tables", "tables"]).Diagnostics);

        Assert.Equal(SourceCodes.FoldersOverlap, error.Code);
        Assert.Contains("Tables and tables are the same folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_folders_whose_names_differ_only_in_case_where_the_file_system_tells_case_apart_are_two()
    {
        // The exact name listed wins: each entry is the folder of its own name, and both are read.
        using var dir = new TempDirectory();
        dir.File("Tables/ingot.sql", Ingot);
        if (Directory.Exists(Path.Combine(dir.Path, "tables")))
            Assert.Skip("this file system ignores case, so tables and Tables are one folder");
        dir.File("tables/weigh.sql", Weigh);

        var read = SqlScriptFolder.ReadFolders(dir.Path, ["Tables", "tables"]);

        Assert.Empty(read.Diagnostics);
        Assert.Equal("Tables/ingot.sql", Assert.Single(read.Model.Tables).Span.File);
        Assert.Equal("tables/weigh.sql", Assert.Single(read.Model.Procedures).Span.File);
    }

    [Fact]
    public void Each_folder_read_is_given_as_found_relative_to_the_folder_the_list_is_written_in()
    {
        using var dir = new TempDirectory();
        dir.File("tables/ingot.sql", Ingot);
        dir.File("procedures/weigh.sql", Weigh);

        var read = SqlScriptFolder.ReadFolders(dir.Path, ["./tables/", Path.Combine(dir.Path, "procedures")]);

        Assert.Equal(["tables", "procedures"], read.Folders);
    }

    [Fact]
    public void A_folder_listed_in_another_case_where_the_file_system_ignores_case_is_given_by_its_name_on_disk()
    {
        using var dir = new TempDirectory();
        dir.File("tables/ingot.sql", Ingot);
        dir.File("procedures/weigh.sql", Weigh);
        if (!Directory.Exists(Path.Combine(dir.Path, "TABLES")))
            Assert.Skip("this file system tells case apart, so TABLES isn't there");

        var read = SqlScriptFolder.ReadFolders(dir.Path, ["TABLES", "procedures"]);

        Assert.Equal(["tables", "procedures"], read.Folders);
        Assert.Equal("tables/ingot.sql", Assert.Single(read.Model.Tables).Span.File);
    }

    [Fact]
    public void An_entry_is_named_in_a_message_as_the_user_wrote_it()
    {
        using var dir = new TempDirectory();
        dir.File("db/ingot.sql", Ingot);

        var error = Assert.Single(SqlScriptFolder.ReadFolders(dir.Path, ["db", "db"], ["db", "./db"]).Diagnostics);

        Assert.Contains("db and ./db are the same folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Folders_whose_names_share_a_beginning_are_not_one_inside_the_other()
    {
        using var dir = new TempDirectory();
        dir.File("db/ingot.sql", Ingot);
        dir.File("db2/weigh.sql", Weigh);

        var read = SqlScriptFolder.ReadFolders(dir.Path, ["db", "db2"]);

        Assert.Empty(read.Diagnostics);
        Assert.Single(read.Model.Procedures);
    }

    [Theory]
    [InlineData(FolderLinkKind.Symbolic)]
    [InlineData(FolderLinkKind.Junction)]
    public void A_folder_two_entries_reach_through_a_link_is_an_error_naming_both(FolderLinkKind kind)
    {
        using var dir = new TempDirectory();
        dir.File("tables/ingot.sql", Ingot);
        dir.File("procedures/weigh.sql", Weigh);
        FolderLink.Make(kind, Path.Combine(dir.Path, "procedures", "shared"), Path.Combine(dir.Path, "tables"));

        var read = SqlScriptFolder.ReadFolders(dir.Path, ["tables", "procedures"]);

        var error = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.FoldersOverlap, error.Code);
        Assert.Contains("tables and procedures both reach", error.Message, StringComparison.Ordinal);
        Assert.Empty(read.Model.Tables);
    }

    [Fact]
    public void A_folder_listed_that_isn_t_there_is_not_read()
    {
        using var dir = new TempDirectory();
        dir.File("tables/ingot.sql", Ingot);

        Assert.Throws<DirectoryNotFoundException>(() => SqlScriptFolder.ReadFolders(dir.Path, ["tables", "procedures"]));
    }
}

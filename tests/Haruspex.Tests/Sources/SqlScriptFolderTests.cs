using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>Which of a folder's scripts Sources reads: all of them, or the ones named, as a project file names them.</summary>
public class SqlScriptFolderTests
{
    [Fact]
    public void A_table_type_is_read_with_its_columns_keys_checks_and_defaults()
    {
        using var dir = new TempDirectory();
        dir.File("types.sql", """
            CREATE TYPE dbo.lines AS TABLE (
                id int NOT NULL PRIMARY KEY WITH (IGNORE_DUP_KEY = ON),
                qty smallint NOT NULL DEFAULT 1 CHECK (qty > 0),
                note nvarchar(20) COLLATE Latin1_General_100_BIN2,
                serial int IDENTITY,
                loud AS upper(note),
                INDEX ix_qty UNIQUE (qty, note)
            );
            """);

        var type = Assert.IsType<TableType>(SqlScriptFolder.Read(dir.Path).Model.FindUserType(new ObjectName("dbo", "lines")));

        Assert.Equal(["id", "qty", "note", "serial", "loud"], type.Columns.Select(c => c.Name));
        Assert.Equal([DeclaredNullability.NotNull, DeclaredNullability.NotNull, DeclaredNullability.NotDeclared], type.Columns.Take(3).Select(c => c.Nullability));
        Assert.Equal([false, true, false, false, false], type.Columns.Select(c => c.HasDefault));
        Assert.Equal((true, true), (type.Columns[3].IsIdentity, type.Columns[4].IsComputed));
        Assert.Equal("Latin1_General_100_BIN2", type.Columns[2].Collation);
        Assert.Equal(["id"], type.PrimaryKey);
        Assert.Equal([(["id"], true), (["qty", "note"], false)], type.UniqueKeys.Select(k => (k.Columns.ToArray(), k.IgnoresDuplicates)));
        Assert.Equal("qty", Assert.Single(type.Checks).Column);
    }

    private const string Ingot = "CREATE TABLE dbo.ingot (id int NOT NULL, weight int NOT NULL CONSTRAINT ck_weight CHECK (weight BETWEEN 1 AND 500));";

    [Fact]
    public void A_script_whose_extension_is_not_in_lower_case_is_read_on_every_platform()
    {
        // As a build's **/*.sql matches it, on Linux too (observed): a folder read and a project read of one folder read the same scripts.
        using var dir = new TempDirectory();
        dir.File("TABLES.SQL", Ingot);
        dir.File("Maintenance.Sql", "ALTER TABLE dbo.ingot NOCHECK CONSTRAINT ALL;");

        var read = SqlScriptFolder.Read(dir.Path);

        Assert.Empty(read.Diagnostics);
        Assert.Equal(new SourceLocation("Maintenance.Sql", 1, 1), Assert.Single(read.Model.FindTable(new ObjectName("dbo", "ingot"))!.Checks).DisabledAt);
    }

    [Fact]
    public void A_link_back_up_the_tree_reads_each_script_once()
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Ingot);
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        FolderLink.Make(FolderLinkKind.Symbolic, Path.Combine(dir.Path, "sub", "back"), dir.Path);

        var read = SqlScriptFolder.Read(dir.Path);

        Assert.Empty(read.Diagnostics);
        Assert.Equal("tables.sql", Assert.Single(read.Model.Tables).Span.File);
    }

    [Fact]
    public void A_script_another_process_holds_is_an_input_error_naming_it_not_a_fault()
    {
        // As Windows holds a file another program has open, and as a file the user may not read: the error is the input's.
        using var dir = new TempDirectory();
        dir.File("tables.sql", Ingot);
        var held = dir.File("held.sql", "CREATE PROCEDURE dbo.p AS SELECT 1 AS one;");
        using var holder = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var read = SqlScriptFolder.Read(dir.Path);

        var error = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.UnreadableScript, error.Code);
        Assert.Equal<DiagnosticLocation?>(new SourceLocation("held.sql", 1, 1), error.At);
        Assert.NotNull(read.Model.FindTable(new ObjectName("dbo", "ingot")));
    }

    [Fact]
    public void A_link_to_no_file_is_an_input_error_naming_it()
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Ingot);
        try
        {
            File.CreateSymbolicLink(Path.Combine(dir.Path, "gone.sql"), Path.Combine(dir.Path, "nowhere.sql"));
        }
        catch (IOException e) when (OperatingSystem.IsWindows())
        {
            Assert.Skip($"this machine doesn't let this user make a symbolic link: {e.Message}");
        }

        var error = Assert.Single(SqlScriptFolder.Read(dir.Path).Diagnostics);

        Assert.Equal(SourceCodes.UnreadableScript, error.Code);
        Assert.Equal<DiagnosticLocation?>(new SourceLocation("gone.sql", 1, 1), error.At);
    }

    [Fact]
    public void A_folder_that_can_t_be_entered_is_an_input_error_naming_it()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("a folder's Unix mode is what shuts it here");
            return;
        }

        using var dir = new TempDirectory();
        dir.File("tables.sql", Ingot);
        dir.File("shut/load.sql", "CREATE PROCEDURE dbo.p AS SELECT 1 AS one;");
        var shut = Path.Combine(dir.Path, "shut");
        File.SetUnixFileMode(shut, UnixFileMode.None);
        try
        {
            if (Directory.EnumerateFileSystemEntries(shut).Any())
                Assert.Skip("this user may enter any folder (root)");
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            var error = Assert.Single(SqlScriptFolder.Read(dir.Path).Diagnostics);
            Assert.Equal(SourceCodes.UnreadableScript, error.Code);
            Assert.Equal<DiagnosticLocation?>(new FileLocation("shut/"), error.At);
        }
        finally
        {
            File.SetUnixFileMode(shut, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void A_temporary_table_a_body_creates_alters_and_drops_is_no_change_to_the_model()
    {
        // A temporary table is the session's, not the database's: changing it in a body changes no table of the model.
        using var dir = new TempDirectory();
        dir.File("p.sql", "CREATE PROCEDURE dbo.p AS BEGIN CREATE TABLE #t (code int NOT NULL); ALTER TABLE #t ADD note nvarchar(20) NULL; SELECT x.code FROM #t AS x; DROP TABLE #t; END");

        Assert.Empty(SqlScriptFolder.Read(dir.Path).Model.TableRedefinitions);
    }

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

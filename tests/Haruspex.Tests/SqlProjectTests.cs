using System.Text;
using Haruspex.Cli;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests;

public class SqlProjectTests
{
    [Fact]
    public void Reports_a_parse_error_with_its_file_and_line()
    {
        using var dir = new TempDirectory();
        dir.File("procedures/bad.sql", "CREATE PROCEDURE sales.bad\nAS\nSELECT FROM WHERE;\n");

        var project = SqlProject.Load(dir.Path);

        var diagnostic = Assert.Single(project.Diagnostics);
        Assert.Equal("procedures/bad.sql", diagnostic.At?.File);
        Assert.Equal(3, diagnostic.At?.Line);
        Assert.StartsWith("procedures/bad.sql:3:", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_an_object_defined_twice()
    {
        using var dir = new TempDirectory();
        dir.File("a.sql", "CREATE TABLE dbo.t (id int NOT NULL);");
        dir.File("b.sql", "CREATE TABLE DBO.T (id int NOT NULL);");

        var project = SqlProject.Load(dir.Path);

        var diagnostic = Assert.Single(project.Diagnostics);
        Assert.Contains("dbo.t", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("b.sql", diagnostic.At?.File);
    }

    [Fact]
    public void Reads_utf16_files_with_a_byte_order_mark_and_crlf_line_endings()
    {
        using var dir = new TempDirectory();
        var sql = "CREATE TABLE dbo.t (id int NOT NULL);\r\nGO\r\nCREATE PROCEDURE dbo.p AS SELECT t.id FROM dbo.t AS t;\r\n";
        File.WriteAllText(Path.Combine(dir.Path, "all.sql"), sql, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        var project = SqlProject.Load(dir.Path);

        Assert.Empty(project.Diagnostics);
        Assert.Equal(["dbo.p"], project.Procedures.Select(p => p.ToString()));
    }

    [Fact]
    public void Lists_procedures_in_a_stable_order_whatever_the_file_layout()
    {
        using var dir = new TempDirectory();
        dir.File("z.sql", "CREATE PROCEDURE b.first AS SELECT 1 AS x;");
        dir.File("a/y.sql", "CREATE PROCEDURE a.second AS SELECT 1 AS x;");

        var project = SqlProject.Load(dir.Path);

        Assert.Equal(["a.second", "b.first"], project.Procedures.Select(p => p.ToString()));
    }

    [Fact]
    public void An_include_pattern_that_matches_nothing_is_reported()
    {
        var project = SqlProject.Load(TestPaths.Fixture("orders"));

        var selection = project.Select([ObjectPattern.Parse("sales.get_*"), ObjectPattern.Parse("sales.nothing_*")]);

        Assert.Equal(["sales.get_order"], selection.Procedures.Select(p => p.ToString()));
        Assert.Equal(["sales.nothing_*"], selection.Unmatched.Select(p => p.Text));
    }
}

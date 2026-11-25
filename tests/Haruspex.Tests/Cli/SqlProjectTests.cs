using System.Text;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Cli;

public class SqlProjectTests
{
    [Fact]
    public void Reports_a_parse_error_with_its_file_and_line()
    {
        using var dir = new TempDirectory();
        dir.File("procedures/bad.sql", "CREATE PROCEDURE sales.bad\nAS\nSELECT FROM WHERE;\n");

        var project = SqlProject.Load(dir.Path);

        var diagnostic = Assert.Single(project.Diagnostics);
        Assert.Equal(SourceCodes.ParseError, diagnostic.Code);
        var at = Assert.IsType<SourceLocation>(diagnostic.At);
        Assert.Equal("procedures/bad.sql", at.File);
        Assert.Equal(3, at.Line);
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
        Assert.Equal(SourceCodes.DefinedTwice, diagnostic.Code);
        Assert.Contains("dbo.t", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("b.sql", Assert.IsType<SourceLocation>(diagnostic.At).File);
    }

    [Fact]
    public void Reports_two_columns_that_differ_only_in_case_under_a_case_sensitive_collation()
    {
        // The table of decision 0119: on Latin1_General_100_CS_AS the server gives code as an int that may be null and Qty as an int
        // that is never null, where matching without case read Code's varchar(10) and qty's CHECK and nullability. Each pair is an
        // error, so nothing is inferred from it (decision 0119).
        using var dir = new TempDirectory();
        dir.File("tables.sql", "CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, Code varchar(10) NOT NULL, code int NULL, qty int NULL CHECK (qty BETWEEN 1 AND 5), Qty int NOT NULL);");
        dir.File("p.sql", "CREATE PROCEDURE dbo.read_columns AS SELECT code, Qty FROM dbo.item;");

        var project = SqlProject.Load(dir.Path, "Latin1_General_100_CS_AS");

        Assert.Equal(
            ["Code and code", "qty and Qty"],
            project.Diagnostics.Select(d => (d.Code, d.IsError, d.Message)).Where(d => d.Code == SourceCodes.CaseVariants && d.IsError).Select(d => d.Message.Contains("Code (", StringComparison.Ordinal) ? "Code and code" : "qty and Qty"));
        Assert.Equal(2, project.Diagnostics.Count);
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
}

using Haruspex.Diagnostics;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;
using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Tests.Sources;

/// <summary>
/// Two names in one namespace that differ only in case are an error (HAR1022, decision 0119): haruspex matches names without
/// case, so it can't tell them apart. Each namespace is checked once, where the reading builds it: a table's or table type's
/// columns, a schema's objects, a body's variables and its temporary tables, and the columns of each table a body declares.
/// </summary>
public class CaseVariantsTests
{
    /// <summary>
    /// The table of decision 0119: on a database of Latin1_General_100_CS_AS, code is an int that may be null and Qty an int that is
    /// never null, but matched without case they were read as Code's varchar(10) and with qty's CHECK and nullability.
    /// </summary>
    private const string Item = "CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, Code varchar(10) NOT NULL, code int NULL, qty int NULL CHECK (qty BETWEEN 1 AND 5), Qty int NOT NULL);";

    private static Diagnostic[] Read(params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);
        return [.. SqlScriptFolder.Read(dir.Path).Diagnostics];
    }

    private static Diagnostic Reported(params (string File, string Sql)[] files) =>
        Assert.Single(Read(files), d => d.Code == SourceCodes.CaseVariants);

    [Fact]
    public void Two_columns_of_a_table_that_differ_only_in_case_are_an_error()
    {
        var problems = Read(("tables.sql", Item)).Where(d => d.Code == SourceCodes.CaseVariants).ToList();

        Assert.Equal(2, problems.Count);
        Assert.True(problems[0].IsError);
        Assert.Equal(
            "dbo.item declares the columns Code (tables.sql:1:53) and code (tables.sql:1:80), whose names differ only in case: haruspex matches names without case, so it can't tell them apart.",
            problems[0].Message);
        Assert.Equal(new SourceLocation("tables.sql", 1, 80), problems[0].At);
        Assert.Contains("qty (tables.sql:1:95) and Qty (tables.sql:1:137)", problems[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_columns_of_a_table_type_that_differ_only_in_case_are_an_error() =>
        Assert.Contains("the columns a (types.sql:1:33) and A (types.sql:1:49)", Reported(("types.sql", "CREATE TYPE dbo.lines AS TABLE (a int NOT NULL, A varchar(5) NULL);")).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("CREATE TABLE dbo.Thing (a int);", "CREATE VIEW dbo.THING AS SELECT 1 AS a;")]
    [InlineData("CREATE TABLE dbo.Thing (a int);", "CREATE PROCEDURE dbo.THING AS SELECT 1 AS a;")]
    [InlineData("CREATE FUNCTION dbo.Thing () RETURNS int AS BEGIN RETURN 1; END;", "CREATE SYNONYM dbo.THING FOR dbo.other;")]
    public void Two_objects_of_a_schema_of_different_kinds_that_differ_only_in_case_are_an_error(string first, string second) =>
        Assert.Contains("dbo.Thing (a.sql:1:1) and dbo.THING (b.sql:1:1)", Reported(("a.sql", first), ("b.sql", second)).Message, StringComparison.Ordinal);

    [Theory]
    // Two of one kind are HAR1002 (an object defined twice), from two scripts or one, which says so where they differ only in case.
    [InlineData("a.sql", "CREATE TABLE dbo.Thing (a int);", "b.sql", "CREATE TABLE dbo.THING (a int);", "dbo.Thing (a.sql:1:1) and dbo.THING (b.sql:1:1)")]
    [InlineData("a.sql", "CREATE PROCEDURE dbo.Thing AS SELECT 1 AS a;", "b.sql", "CREATE PROCEDURE dbo.THING AS SELECT 1 AS a;", "dbo.Thing (a.sql:1:1) and dbo.THING (b.sql:1:1)")]
    [InlineData("a.sql", "CREATE PROCEDURE dbo.Thing AS SELECT 1 AS a;\nGO\nCREATE PROCEDURE dbo.THING AS SELECT 1 AS a;", "b.sql", "", "dbo.Thing (a.sql:1:1) and dbo.THING (a.sql:3:1)")]
    public void Two_objects_of_one_kind_that_differ_only_in_case_are_defined_twice_naming_both(string file, string sql, string other, string more, string named)
    {
        var problem = Assert.Single(Read((file, sql), (other, more)));

        Assert.Equal(SourceCodes.DefinedTwice, problem.Code);
        Assert.Contains($"{named}, whose names differ only in case", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@Total int", "DECLARE @total varchar(10);", "@Total (p.sql:1:24) and @total (p.sql:3:9)")]
    [InlineData("", "DECLARE @n int; DECLARE @N varchar(10);", "@n (p.sql:3:9) and @N (p.sql:3:25)")]
    [InlineData("", "DECLARE @rows TABLE (a int); DECLARE @Rows int;", "@rows (p.sql:3:9) and @Rows (p.sql:3:38)")]
    [InlineData("@a int, @A int", "SELECT 1 AS one;", "@a (p.sql:1:24) and @A (p.sql:1:32)")]
    public void Two_variables_of_a_body_that_differ_only_in_case_are_an_error(string parameters, string body, string named) =>
        Assert.Contains($"dbo.p declares the variables {named}", Reported(("p.sql", $"CREATE PROCEDURE dbo.p {parameters}\nAS\n{body}")).Message, StringComparison.Ordinal);

    [Fact]
    public void A_function_s_return_table_and_a_variable_that_differ_only_in_case_are_an_error() =>
        Assert.Contains(
            "@r (f.sql:1:34) and @R (f.sql:1:68)",
            Reported(("f.sql", "CREATE FUNCTION dbo.f () RETURNS @r TABLE (a int) AS BEGIN DECLARE @R int; RETURN; END;")).Message,
            StringComparison.Ordinal);

    [Theory]
    [InlineData("CREATE TABLE #t (a int); CREATE TABLE #T (b int);", "#t (p.sql:3:14) and #T (p.sql:3:39)")]
    [InlineData("CREATE TABLE #t (a int); SELECT 1 AS b INTO #T;", "#t (p.sql:3:14) and #T (p.sql:3:45)")]
    public void Two_temporary_tables_of_a_body_that_differ_only_in_case_are_an_error(string body, string named) =>
        Assert.Contains($"dbo.p creates the temporary tables {named}", Reported(("p.sql", $"CREATE PROCEDURE dbo.p\nAS\n{body}")).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("CREATE TABLE #t (a int NOT NULL, A varchar(5) NULL);", "#t, in dbo.p, declares the columns a (p.sql:3:18) and A (p.sql:3:34)")]
    [InlineData("DECLARE @t TABLE (a int NOT NULL, A varchar(5) NULL);", "@t, in dbo.p, declares the columns a (p.sql:3:19) and A (p.sql:3:35)")]
    public void Two_columns_of_a_table_a_body_declares_that_differ_only_in_case_are_an_error(string body, string named) =>
        Assert.Contains(named, Reported(("p.sql", $"CREATE PROCEDURE dbo.p\nAS\n{body}")).Message, StringComparison.Ordinal);

    [Fact]
    public void Two_columns_of_a_function_s_return_table_that_differ_only_in_case_are_an_error() =>
        Assert.Contains("the columns a (f.sql:1:44) and A (f.sql:1:51)", Reported(("f.sql", "CREATE FUNCTION dbo.f () RETURNS @r TABLE (a int, A int) AS BEGIN RETURN; END;")).Message, StringComparison.Ordinal);

    [Fact]
    public void A_trigger_s_variables_that_differ_only_in_case_are_an_error() =>
        Assert.Contains("dbo.tr declares the variables @n", Reported(("t.sql", "CREATE TABLE dbo.t (a int);\nGO\nCREATE TRIGGER dbo.tr ON dbo.t AFTER INSERT AS DECLARE @n int; DECLARE @N int;")).Message, StringComparison.Ordinal);

    [Theory]
    // A name written in another case than it is declared is one name, matched without case as before: on a database that
    // compares names by case the script would not deploy, and on one that doesn't it is the same name.
    [InlineData("CREATE TABLE dbo.item (id int, code int);\nGO\nCREATE PROCEDURE dbo.p AS SELECT CODE, ID FROM DBO.ITEM;")]
    [InlineData("CREATE PROCEDURE dbo.p @Total int AS SELECT @TOTAL AS t;")]
    [InlineData("CREATE PROCEDURE dbo.p AS CREATE TABLE #t (a int); SELECT A FROM #T;")]
    // One spelling declared twice is no pair (a body that declares a name twice the server refuses, Msg 134, but not for case).
    [InlineData("CREATE PROCEDURE dbo.p AS DECLARE @n int; IF 1 = 0 DECLARE @n int;")]
    [InlineData("CREATE PROCEDURE dbo.p AS CREATE TABLE #t (a int); DROP TABLE #t; CREATE TABLE #t (b int);")]
    // Each body is its own namespace, and so is each table's list of columns.
    [InlineData("CREATE PROCEDURE dbo.p @n int AS SELECT 1 AS one;\nGO\nCREATE PROCEDURE dbo.q @N int AS SELECT 1 AS one;")]
    [InlineData("CREATE TABLE dbo.a (code int);\nGO\nCREATE TABLE dbo.b (Code int);")]
    // A type is in a namespace of its own, apart from the schema's tables and views, so a type and a table may share a name.
    [InlineData("CREATE TYPE dbo.Item AS TABLE (a int);\nGO\nCREATE TABLE dbo.item (a int);")]
    public void Names_that_differ_only_in_case_in_different_namespaces_or_in_their_uses_are_no_error(string sql) =>
        Assert.Empty(Read(("a.sql", sql)));

    [Fact]
    public void A_package_s_columns_that_differ_only_in_case_are_an_error()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "store.dacpac");
        TestPackage.Write(path, TestPackage.Table("dbo", "item", "Code varchar(10)", "code int?"), "Latin1_General_100_CS_AS");

        var read = DacpacReader.Read(path);

        Assert.Contains(read.Diagnostics, d => d.Code == SourceCodes.CaseVariants && d.Message.Contains("the columns Code (", StringComparison.Ordinal) && d.Message.Contains(") and code (", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_database_s_columns_that_differ_only_in_case_are_an_error()
    {
        // A body reads another database's tables by a three-part name, matched as this database's are, so its pairs are found too.
        using var dir = new TempDirectory();
        TestPackage.Write(Path.Combine(dir.Path, "ledger.dacpac"), TestPackage.Table("dbo", "entry", "Code varchar(10)", "code int?"), "Latin1_General_100_CS_AS");
        var path = Path.Combine(dir.Path, "store.dacpac");
        TestPackage.Write(path, TestPackage.Procedure("dbo", "p", "SELECT 1 AS one"), references: new TestPackage.Reference("ledger.dacpac", "$(Ledger)"));

        var read = DacpacReader.Read(path);

        Assert.Contains(read.Diagnostics, d => d.Code == SourceCodes.CaseVariants && d.Message.StartsWith("dbo.entry declares the columns Code (ledger.dacpac/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_project_s_columns_that_differ_only_in_case_are_an_error()
    {
        using var dir = new TempDirectory();
        dir.File("p/tables.sql", Item);
        var project = dir.File("p/p.sqlproj", """
            <Project Sdk="Microsoft.Build.Sql/2.3.0">
              <PropertyGroup>
                <DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP>
              </PropertyGroup>
            </Project>
            """);

        var read = SqlProjectReader.Read(project, [], new PackageCache(Path.Combine(dir.Path, "no-packages")));

        Assert.Equal(2, read.Diagnostics.Count(d => d.Code == SourceCodes.CaseVariants));
    }

    [Theory]
    [InlineData("DECLARE @n int = 1; DECLARE @N int = 2; SELECT @n AS a;", "@n (q.sql:1:9) and @N (q.sql:1:29)")]
    [InlineData("CREATE TABLE #t (a int); CREATE TABLE #T (a int); SELECT a FROM #t;", "#t (q.sql:1:14) and #T (q.sql:1:39)")]
    // Parameters it only uses are its batch's variables too: on a case-sensitive server @id would be undeclared (Msg 137).
    [InlineData("SELECT 1 AS one WHERE @Id = 1 AND @id = 2;", "q.sql uses the parameters @Id (q.sql:1:23) and @id (q.sql:1:35)")]
    public void A_query_file_s_names_that_differ_only_in_case_are_an_error(string text, string named)
    {
        var read = Assert.IsType<Failed<SqlServer.Model.QueryFile>>(QueryFileReader.Parse(text, "q.sql"));

        Assert.Contains(read.Diagnostics, d => d.Code == SourceCodes.CaseVariants && d.Message.Contains(named, StringComparison.Ordinal));
    }

    [Theory]
    // A use in another case than a declaration is one name, and one parameter used twice is one parameter.
    [InlineData("DECLARE @n int = 1; SELECT @N AS a;")]
    [InlineData("SELECT 1 AS one WHERE @id = 1 OR @id = 2;")]
    public void A_query_file_s_name_used_in_another_case_than_declared_is_no_error(string text) =>
        Assert.IsType<Succeeded<SqlServer.Model.QueryFile>>(QueryFileReader.Parse(text, "q.sql"));
}

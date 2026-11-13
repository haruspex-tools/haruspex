using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// sqlcmd's scripting variables in a script that Sources reads: <c>:setvar</c> defines one, and <c>$(name)</c> is replaced
/// by its value before the server sees the text, as sqlcmd and SSMS's SQLCMD mode do. An install script such as
/// AdventureWorks' names its database this way, so without them it doesn't parse.
/// </summary>
public class SqlcmdVariableTests
{
    private static SchemaReadResult Read(string sql)
    {
        using var dir = new TempDirectory();
        dir.File("install.sql", sql);
        return SqlScriptFolder.Read(dir.Path);
    }

    /// <summary>The text of a procedure's definition, as ScriptDom read it.</summary>
    private static string Text(Procedure procedure)
    {
        var body = procedure.Body;
        return string.Concat(body.ScriptTokenStream
            .Skip(body.FirstTokenIndex)
            .Take(body.LastTokenIndex - body.FirstTokenIndex + 1)
            .Select(t => t.Text));
    }

    [Fact]
    public void A_variable_set_with_setvar_is_replaced_where_it_is_used()
    {
        var read = Read(":setvar Schema \"sales\"\nGO\nCREATE TABLE $(Schema).orders (id int NOT NULL);\n");

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(read.Model.FindTable(new ObjectName("sales", "orders")));
    }

    [Fact]
    public void A_value_may_be_written_without_quotes()
    {
        var read = Read(":setvar Schema sales\nCREATE TABLE $(Schema).orders (id int NOT NULL);\n");

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(read.Model.FindTable(new ObjectName("sales", "orders")));
    }

    [Fact]
    public void A_setvar_may_be_indented_and_in_any_case()
    {
        var read = Read("  :SETVAR Schema \"sales\"\nCREATE TABLE $(schema).orders (id int NOT NULL);\n");

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(read.Model.FindTable(new ObjectName("sales", "orders")));
    }

    [Fact]
    public void A_variable_is_replaced_inside_a_string_too()
    {
        // sqlcmd replaces the text before the server parses it, so a quoted $(name) is replaced as well. AdventureWorks'
        // script relies on it, to stop when it isn't run in SQLCMD mode.
        var read = Read(":setvar Greeting \"hello\"\nGO\nCREATE PROCEDURE dbo.greet AS SELECT '$(Greeting)' AS g;\n");

        Assert.Empty(read.Diagnostics);
        var procedure = read.Model.FindProcedure(new ObjectName("dbo", "greet"))!;
        Assert.Contains("'hello'", Text(procedure), StringComparison.Ordinal);
    }

    [Fact]
    public void A_variable_is_replaced_inside_a_quoted_name()
    {
        var read = Read(":setvar Suffix \"2026\"\nCREATE TABLE dbo.[orders_$(Suffix)] (id int NOT NULL);\nCREATE TABLE dbo.\"lines_$(Suffix)\" (id int NOT NULL);\n");

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(read.Model.FindTable(new ObjectName("dbo", "orders_2026")));
        Assert.NotNull(read.Model.FindTable(new ObjectName("dbo", "lines_2026")));
    }

    [Fact]
    public void A_variable_in_a_comment_is_not_replaced()
    {
        // If it were, this value would end the comment and define a table.
        var read = Read(":setvar Escape \"*/ CREATE TABLE dbo.leaked (id int NOT NULL); /*\"\n/* $(Escape) */\n-- $(Escape)\nCREATE TABLE dbo.kept (id int NOT NULL);\n");

        Assert.Empty(read.Diagnostics);
        Assert.Null(read.Model.FindTable(new ObjectName("dbo", "leaked")));
        Assert.NotNull(read.Model.FindTable(new ObjectName("dbo", "kept")));
    }

    [Theory]
    [InlineData("/*\n:setvar Schema other\n*/\n")]
    [InlineData("SELECT 'a\n:setvar Schema other\n' AS s;\nGO\n")]
    [InlineData("SELECT 1 AS [a\n:setvar Schema other\n];\nGO\n")]
    [InlineData("SELECT 1 AS \"a\n:setvar Schema other\n\";\nGO\n")]
    public void A_setvar_inside_a_comment_a_string_or_a_quoted_name_is_not_a_command(string between)
    {
        var read = Read($":setvar Schema \"sales\"\n{between}CREATE TABLE $(Schema).orders (id int NOT NULL);\n");

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(read.Model.FindTable(new ObjectName("sales", "orders")));
    }

    [Fact]
    public void A_setvar_line_leaves_every_later_line_where_it_was()
    {
        // The command's line is kept, empty, so a position in the file is still a position in what is parsed.
        var read = Read(":setvar Schema \"sales\"\nGO\n\nCREATE PROCEDURE $(Schema).list AS SELECT 1 AS one;\n");

        var procedure = read.Model.FindProcedure(new ObjectName("sales", "list"))!;
        Assert.Equal(4, procedure.Span.Line);
    }

    [Fact]
    public void A_variable_used_before_it_is_set_is_left_as_written()
    {
        // sqlcmd fails on it; so does the parse, which is reported, rather than a guess at the value.
        var read = Read("CREATE TABLE $(Schema).orders (id int NOT NULL);\nGO\n:setvar Schema \"sales\"\n");

        Assert.NotEmpty(read.Diagnostics);
        Assert.Null(read.Model.FindTable(new ObjectName("sales", "orders")));
    }

    [Fact]
    public void A_setvar_with_no_value_removes_the_variable()
    {
        var read = Read(":setvar Schema \"sales\"\n:setvar Schema\nCREATE TABLE $(Schema).orders (id int NOT NULL);\n");

        Assert.NotEmpty(read.Diagnostics);
    }

    [Fact]
    public void A_script_with_no_setvar_is_read_as_it_is()
    {
        // Text that only looks like a variable is the script's own, and stays.
        var read = Read("CREATE PROCEDURE dbo.price AS SELECT '$(USD)' AS currency;\n");

        Assert.Empty(read.Diagnostics);
        Assert.Contains("'$(USD)'", Text(read.Model.FindProcedure(new ObjectName("dbo", "price"))!), StringComparison.Ordinal);
    }

    [Fact]
    public void Another_sqlcmd_command_is_still_an_error()
    {
        // :r includes another file, which a script read on its own cannot follow. Leaving it to fail says so; dropping
        // the line would read a different script from the one that runs.
        var read = Read(":r .\\tables.sql\nGO\nCREATE TABLE dbo.orders (id int NOT NULL);\n");

        Assert.NotEmpty(read.Diagnostics);
    }
}

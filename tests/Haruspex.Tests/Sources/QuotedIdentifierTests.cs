using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Sources;

/// <summary>
/// A script is parsed with the <c>QUOTED_IDENTIFIER</c> in force where each batch is, as the server parses it: a <c>SET</c> takes
/// effect where it is parsed, in its own batch and every one after, under an <c>IF</c> that is false too, and not inside a
/// module's body (<c>ScriptParser</c>; observed: <c>A_quoted_identifier_set_outside_a_body_takes_effect_where_it_is_parsed</c>).
/// With it off, <c>"x"</c> is a string, not a name.
/// </summary>
public class QuotedIdentifierTests
{
    private const string Pot = "CREATE TABLE s.pot (id int NOT NULL PRIMARY KEY, glaze int NULL);";

    private static SchemaReadResult Read(string script)
    {
        using var dir = new TempDirectory();
        dir.File("000_pot.sql", Pot);
        dir.File("001_script.sql", script);
        return SqlScriptFolder.Read(dir.Path);
    }

    /// <summary>A procedure selecting <c>"glaze"</c>, named <paramref name="name"/>.</summary>
    private static string Selecting(string name) => $"CREATE PROCEDURE s.{name} AS SELECT \"glaze\" AS g, [glaze] AS g2 FROM s.pot;";

    /// <summary>Whether the procedure's <c>"glaze"</c> is a string (or else a name), and its <c>[glaze]</c> a name either way.</summary>
    private static bool QuotesAString(SchemaReadResult read, string name)
    {
        var procedure = read.Model.FindProcedure(new ObjectName("s", name)) ?? throw new InvalidOperationException($"No s.{name}.");
        var select = (QuerySpecification)Assert.IsType<SqlBody>(procedure.Body).Statements.Statements.OfType<SelectStatement>().First().QueryExpression;
        var elements = select.SelectElements.Cast<SelectScalarExpression>().Select(e => e.Expression).ToList();
        Assert.IsType<ColumnReferenceExpression>(elements[1]);
        return elements[0] switch
        {
            StringLiteral => true,
            ColumnReferenceExpression => false,
            var other => throw new InvalidOperationException($"\"glaze\" read as {other.GetType().Name}."),
        };
    }

    [Fact]
    public void A_procedure_after_set_quoted_identifier_off_reads_double_quotes_as_a_string()
    {
        // The form SSMS scripts a module created with the option off in.
        var read = Read($"SET ANSI_NULLS ON\nGO\nSET QUOTED_IDENTIFIER OFF\nGO\n{Selecting("qo")}\nGO\n");

        Assert.Empty(read.Diagnostics);
        Assert.True(QuotesAString(read, "qo"));
    }

    [Fact]
    public void The_setting_holds_into_every_batch_after_it_until_set_again()
    {
        var read = Read(
            $"{Selecting("before")}\nGO\nSET QUOTED_IDENTIFIER OFF\nGO\n{Selecting("first")}\nGO\n{Selecting("second")}\nGO\n"
            + $"SET QUOTED_IDENTIFIER ON\nGO\n{Selecting("after")}\nGO\nSET QUOTED_IDENTIFIER OFF;\nGO\n{Selecting("last")}");

        Assert.Empty(read.Diagnostics);
        Assert.False(QuotesAString(read, "before"));
        Assert.True(QuotesAString(read, "first"));
        Assert.True(QuotesAString(read, "second"));
        Assert.False(QuotesAString(read, "after"));
        Assert.True(QuotesAString(read, "last"));
    }

    [Theory]
    [InlineData("SET QUOTED_IDENTIFIER OFF;")]
    [InlineData("set quoted_identifier off")]
    [InlineData("SET ANSI_NULLS, QUOTED_IDENTIFIER OFF;")]
    [InlineData("SET ANSI_DEFAULTS OFF;")]
    [InlineData("IF 1 = 0 SET QUOTED_IDENTIFIER OFF;")]
    [InlineData("IF 1 = 0 BEGIN SET QUOTED_IDENTIFIER OFF; END")]
    [InlineData("/* SET QUOTED_IDENTIFIER ON */ SET QUOTED_IDENTIFIER OFF; -- SET QUOTED_IDENTIFIER ON")]
    public void Every_set_outside_a_body_turns_it_off_where_it_is_parsed(string set)
    {
        // A SET takes effect when it is parsed, so one under an IF that is false does too.
        var read = Read($"{set}\nGO\n{Selecting("qo")}");

        Assert.Empty(read.Diagnostics);
        Assert.True(QuotesAString(read, "qo"));
    }

    [Theory]
    [InlineData("SET QUOTED_IDENTIFIER ON;")]
    [InlineData("SET ANSI_DEFAULTS ON; SET IMPLICIT_TRANSACTIONS OFF;")]
    public void A_set_turns_it_on_again(string set)
    {
        var read = Read($"SET QUOTED_IDENTIFIER OFF;\nGO\n{set}\nGO\n{Selecting("qo")}");

        Assert.Empty(read.Diagnostics);
        Assert.False(QuotesAString(read, "qo"));
    }

    [Theory]
    [InlineData("ALTER DATABASE CURRENT SET QUOTED_IDENTIFIER OFF;")]
    [InlineData("ALTER DATABASE [pottery] SET QUOTED_IDENTIFIER OFF WITH NO_WAIT;")]
    [InlineData("ALTER DATABASE pottery SET QUOTED_IDENTIFIER OFF, ANSI_NULLS ON;")]
    public void Altering_the_database_s_default_changes_nothing_a_connection_has_set(string set)
    {
        // Observed with sqlcmd, which sets the option when it connects: ALTER DATABASE can't run in the differential tests'
        // transaction.
        var read = Read($"{set}\nGO\n{Selecting("qo")}");

        Assert.Empty(read.Diagnostics);
        Assert.False(QuotesAString(read, "qo"));
    }

    [Fact]
    public void Altering_the_database_s_default_changes_nothing_later_in_its_batch()
    {
        var read = Read("ALTER DATABASE CURRENT SET QUOTED_IDENTIFIER OFF; CREATE TABLE s.label (id int NOT NULL, glaze AS \"id\");");

        Assert.Empty(read.Diagnostics);
        Assert.IsType<ColumnReferenceExpression>(Assert.Single(read.Model.FindTable(new ObjectName("s", "label"))!.Columns, c => c.Name == "glaze").ComputedExpression);
    }

    [Theory]
    [InlineData("SELECT 1 AS one WHERE 'quoted' = 'quoted'; SET QUOTED_IDENTIFIER OFF; CREATE TABLE s.label (id int NOT NULL, glaze AS \"glaze\");")]
    [InlineData("SET QUOTED_IDENTIFIER OFF; CREATE TABLE s.label (id int NOT NULL, glaze AS \"glaze\");")]
    [InlineData("SET ANSI_NULLS, QUOTED_IDENTIFIER OFF; CREATE TABLE s.label (id int NOT NULL, glaze AS \"glaze\");")]
    [InlineData("IF 1 = 0 BEGIN SET QUOTED_IDENTIFIER OFF; END CREATE TABLE s.label (id int NOT NULL, glaze AS \"glaze\");")]
    [InlineData("SET QUOTED_IDENTIFIER ON\nGO\nSET QUOTED_IDENTIFIER OFF; SET QUOTED_IDENTIFIER ON; SET QUOTED_IDENTIFIER OFF; CREATE TABLE s.label (id int NOT NULL, glaze AS \"glaze\");")]
    public void A_set_takes_effect_in_its_own_batch(string batch)
    {
        var read = Read(batch);

        Assert.Empty(read.Diagnostics);
        var glaze = Assert.Single(read.Model.FindTable(new ObjectName("s", "label"))!.Columns, c => c.Name == "glaze");
        Assert.IsType<StringLiteral>(glaze.ComputedExpression);
    }

    [Fact]
    public void A_set_in_a_procedure_s_body_changes_nothing_after_the_body()
    {
        // Observed: the session keeps its setting.
        var read = Read($"CREATE PROCEDURE s.body AS SET QUOTED_IDENTIFIER OFF; SELECT 1 AS one;\nGO\n{Selecting("after")}");

        Assert.Empty(read.Diagnostics);
        Assert.False(QuotesAString(read, "after"));
    }

    [Theory]
    [InlineData("SET QUOTED_IDENTIFIER OFF\nGO\nCREATE PROCEDURE s.body AS SET QUOTED_IDENTIFIER ON; SELECT \"glaze\" AS g FROM s.pot;", 3, 61)]
    [InlineData("CREATE PROCEDURE s.body AS SET QUOTED_IDENTIFIER OFF; SELECT \"glaze\" AS g FROM s.pot;", 1, 62)]
    [InlineData("SET ANSI_DEFAULTS OFF; SET IMPLICIT_TRANSACTIONS OFF; CREATE TABLE s.label (id int NOT NULL, glaze AS \"glaze\");", 1, 103)]
    public void A_double_quote_haruspex_s_parser_would_read_otherwise_than_the_server_is_a_parse_error(string script, int line, int column)
    {
        // The server parses a body with the setting in force where it is created, which ScriptDom changes at a SET in it; and
        // ScriptDom doesn't follow SET ANSI_DEFAULTS within its batch. Observed: a body that turns it off still reads "glaze" as a name.
        var read = Read(script);

        var error = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.ParseError, error.Code);
        Assert.Equal(new SourceLocation("001_script.sql", line, column), error.At);
    }

    [Fact]
    public void A_string_longer_than_a_name_after_the_set_parses()
    {
        // With the option on, "…" is a name, which may be at most 128 characters; with it off, a string, which may be longer.
        var read = Read($"SET QUOTED_IDENTIFIER OFF\nGO\nCREATE PROCEDURE s.long AS SELECT \"{new string('x', 200)}\" AS note;");

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(read.Model.FindProcedure(new ObjectName("s", "long")));
    }

    [Fact]
    public void Every_location_after_a_set_is_where_it_is_in_the_file()
    {
        const string Before = "-- a comment\nSET QUOTED_IDENTIFIER OFF\nGO\n\n  ";
        var read = Read($"{Before}{Selecting("qo")}\nGO\nSET QUOTED_IDENTIFIER ON\nGO\nCREATE PROCEDURE s.after AS SELECT 1 AS one;");
        var broken = Read($"{Before}{Selecting("qo")}\nGO\nSET QUOTED_IDENTIFIER ON\nGO\nCREATE PROCEDURE s.broken AS SELECT FROM;");

        Assert.Equal(new SourceLocation("001_script.sql", 5, 3), read.Model.FindProcedure(new ObjectName("s", "qo"))!.Span.Start);
        Assert.Equal(new SourceLocation("001_script.sql", 9, 1), read.Model.FindProcedure(new ObjectName("s", "after"))!.Span.Start);
        var error = Assert.Single(broken.Diagnostics);
        Assert.Equal(SourceCodes.ParseError, error.Code);
        Assert.Equal(new SourceLocation("001_script.sql", 9, 37), error.At);
    }

    [Fact]
    public void Double_quotes_on_both_sides_of_a_set_in_one_batch_are_each_read_with_the_setting_in_force_there()
    {
        var read = Read("CREATE TABLE s.named (id int NOT NULL, glaze AS \"id\"); SET QUOTED_IDENTIFIER OFF; CREATE TABLE s.label (id int NOT NULL, glaze AS \"id\");");

        Assert.Empty(read.Diagnostics);
        Assert.IsType<ColumnReferenceExpression>(Assert.Single(read.Model.FindTable(new ObjectName("s", "named"))!.Columns, c => c.Name == "glaze").ComputedExpression);
        Assert.IsType<StringLiteral>(Assert.Single(read.Model.FindTable(new ObjectName("s", "label"))!.Columns, c => c.Name == "glaze").ComputedExpression);
    }

    [Fact]
    public void A_project_script_that_starts_with_it_off_follows_a_set_that_turns_it_on()
    {
        var declarations = new SchemaDeclarations();
        using var dir = new TempDirectory();
        dir.File("001_script.sql", $"{Selecting("first")}\nGO\nSET QUOTED_IDENTIFIER ON\nGO\n{Selecting("second")}");
        SqlScriptFolder.ReadInto(declarations, [new ScriptFile(Path.Combine(dir.Path, "001_script.sql"), "001_script.sql", new ScriptSettings(AnsiNulls: true, QuotedIdentifier: false))]);
        var read = new SchemaReadResult(declarations.ToModel(), [.. declarations.Diagnostics]);

        Assert.Empty(read.Diagnostics);
        Assert.True(QuotesAString(read, "first"));
        Assert.False(QuotesAString(read, "second"));
    }
}

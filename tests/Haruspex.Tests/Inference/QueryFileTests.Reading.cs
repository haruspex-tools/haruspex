using Haruspex.Diagnostics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Inference;

/// <summary>Reading a query file: one batch, and declaration comments that each name a parameter the file uses, once.</summary>
public partial class QueryFileTests
{
    private static IReadOnlyList<string> Refused(string text) =>
        [.. Assert.IsType<Failed<SqlServer.Model.QueryFile>>(QueryFileReader.Parse(text, "q.sql")).Diagnostics.Select(d => d.ToString())];

    [Fact]
    public void A_file_split_by_go_is_refused_since_a_query_runs_as_one_batch() =>
        Assert.Equal(
            ["q.sql:1:1: error HAR1014: q.sql isn't one batch: GO splits it into 2 batches; GO is no part of SQL, but a client's separator, and a query file is run as one batch."],
            Refused("SELECT 1 AS one;\nGO\nSELECT 2 AS two;\n"));

    [Fact]
    public void A_file_with_no_statement_is_refused() =>
        Assert.Equal(["q.sql:1:1: error HAR1014: q.sql isn't one batch: it holds no statement, so it runs nothing."], Refused("-- nothing yet\n"));

    [Fact]
    public void A_file_that_does_not_parse_is_refused_where_the_parser_stops() =>
        Assert.StartsWith("q.sql:1:8: error HAR1001:", Assert.Single(Refused("SELECT FROM;")), StringComparison.Ordinal);

    [Theory]
    [InlineData("-- @param @id", "-- @param @id: a declaration is written -- @param @name type, such as -- @param @order_id int.")]
    [InlineData("-- @param @id integer please", "-- @param @id integer please: \"integer please\" doesn't read as a type of @id; write it as DECLARE would.")]
    [InlineData("-- @param @id int = 1", "-- @param @id int = 1: \"int = 1\" doesn't read as a type of @id; write it as DECLARE would.")]
    [InlineData("-- @param @@id int", "-- @param @@id int: \"int\" doesn't read as a type of @@id; write it as DECLARE would.")]
    [InlineData("-- @param @other int", "-- @param @other int: the file doesn't use @other, so it isn't a parameter; check its spelling.")]
    [InlineData("-- @param @limit int", "-- @param @limit int: the file declares @limit itself, with DECLARE, so it is a variable of the batch, not a parameter.")]
    public void A_declaration_that_can_not_be_taken_is_refused_saying_why(string comment, string why) =>
        Assert.Equal(
            [$"q.sql:1:1: error HAR1015: {why}"],
            Refused(comment + "\nDECLARE @limit int = 1; SELECT TOP (@limit) 1 AS one WHERE @id = 1;\n"));

    [Fact]
    public void A_parameter_declared_twice_is_refused_at_the_second() =>
        Assert.Equal(
            ["q.sql:2:1: error HAR1015: -- @param @ID bigint: @ID is declared already; declare a parameter once."],
            Refused("-- @param @id int\n-- @param @ID bigint\nSELECT @id AS id;\n"));

    [Fact]
    public void A_variable_used_before_its_declare_is_refused_as_the_server_refuses_it() =>
        Assert.Equal(
            ["q.sql:1:8: error HAR1016: @x is used before the DECLARE of it, which SQL Server refuses (error 137); declare it first, or rename one of them."],
            Refused("SELECT @x AS v; DECLARE @x int = 1; SELECT @x AS w;\n"));

    [Theory]
    [InlineData("SELECT 1 AS one;\nRETURN 5;\n", 2)]
    [InlineData("IF 1 = 0 BEGIN\n  RETURN NULL;\nEND\nSELECT 1 AS one;\n", 2)]
    public void A_return_with_a_value_is_refused_since_the_server_refuses_it_in_such_a_batch(string text, int line) =>
        Assert.Equal(
            [$"q.sql:{line}:{(line == 2 && text.StartsWith("IF", StringComparison.Ordinal) ? 3 : 1)}: error HAR1017: a batch run through sp_executesql may RETURN, but SQL Server refuses RETURN with a value there (error 178), so every call would fail; write RETURN alone."],
            Refused(text));

    [Fact]
    public void A_bare_return_is_read() =>
        Assert.Equal(["@id int"], InferQuery.Infer(Shop, "SELECT 1 AS one FROM shop.customers AS c WHERE c.customer_id = @id; RETURN;").Parameters());

    [Fact]
    public void A_comment_that_only_mentions_a_parameter_is_not_a_declaration() =>
        Assert.Equal(
            ["@id int"],
            InferQuery.Infer(Shop, "-- @parameters, and @param in a sentence\nSELECT 1 AS one FROM shop.customers AS c WHERE c.customer_id = @id; -- @id: the customer\n").Parameters());

    [Fact]
    public void A_name_passed_to_a_procedure_is_the_callee_s_not_a_parameter_of_the_file() =>
        Assert.Equal(["@w nvarchar(20)"], Parameters("EXEC shop.touch @customer_id = 1, @why = @w;"));

    /// <summary>The characters .NET's <c>ReplaceLineEndings</c> also takes for a line's end, which neither the parser nor SQL Server does.</summary>
    private const string NotLineEnds = "\f\u2028\u2029\u0085";

    [Fact]
    public void A_query_file_s_text_is_kept_as_written_but_for_cr_and_crlf_which_become_lf()
    {
        // The reader once turned each into LF with ReplaceLineEndings, so the function sent another literal than the file's. SQL Server
        // keeps each in a literal and ends neither a comment nor a line at one (QueryTextObservationTests).
        using var dir = new TempDirectory();
        var path = dir.File("q.sql", $"-- a{NotLineEnds}b\r\nSELECT N'c{NotLineEnds}d' AS label;\rSELECT 1 AS one;\n");

        var file = QueryFileReader.Read(path, "q.sql").Expected();

        Assert.Equal($"-- a{NotLineEnds}b\nSELECT N'c{NotLineEnds}d' AS label;\nSELECT 1 AS one;\n", file.Text);
    }

    [Fact]
    public void A_parse_error_after_a_character_that_ends_no_line_is_reported_on_the_line_an_editor_shows()
    {
        using var dir = new TempDirectory();
        var path = dir.File("q.sql", $"-- a{NotLineEnds}b\nSELECT N'{NotLineEnds}' AS label;\nSELECT FROM;\n");

        var refused = Assert.IsType<Failed<SqlServer.Model.QueryFile>>(QueryFileReader.Read(path, "q.sql"));

        Assert.StartsWith("q.sql:3:8: error HAR1001:", Assert.Single(refused.Diagnostics).ToString(), StringComparison.Ordinal);
    }
}

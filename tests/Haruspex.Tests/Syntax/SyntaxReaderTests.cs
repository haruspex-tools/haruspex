using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Syntax;

/// <summary>Where a fragment is: a span starts at its first token and ends after its last, in lines as ScriptDom counts them.</summary>
public class SyntaxReaderTests
{
    [Fact]
    public void A_span_on_one_line_ends_at_the_column_after_its_last_character()
    {
        var span = SyntaxReader.Span("p.sql", First("SELECT 1 AS a;"));

        Assert.Equal(("p.sql", 1, 1, 1, 15), (span.File, span.Line, span.Column, span.EndLine, span.EndColumn));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void A_span_ends_after_a_string_over_lines_ended_by_crlf_lf_or_a_lone_cr(string end)
    {
        var span = SyntaxReader.Span("p.sql", First($"SELECT 'a{end}bc'"));

        Assert.Equal((1, 1, 2, 4), (span.Line, span.Column, span.EndLine, span.EndColumn));
    }

    [Fact]
    public void A_position_inside_a_span_is_in_its_file()
    {
        var statement = First("\n\tSELECT 1 AS a;");

        Assert.Equal("q.sql:2:2", SyntaxReader.Span("q.sql", statement).Start.ToString());
        Assert.Equal("q.sql:2:2", SyntaxReader.Span("q.sql", statement).At(statement).ToString());
    }

    private static TSqlStatement First(string sql)
    {
        var script = (TSqlScript)new TSql170Parser(initialQuotedIdentifiers: true).Parse(new StringReader(sql), out var errors);
        Assert.Empty(errors);
        return script.Batches[0].Statements[0];
    }
}

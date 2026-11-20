using Haruspex.SqlServer.Inference;

namespace Haruspex.Tests.Inference;

/// <summary>The note HAR2025, JSON built as text, given by its construction wherever it is written (decision 0085).</summary>
public partial class JsonAssembledTests
{
    /// <summary>The notes of JSON built as text that inferring <c>dbo.p</c>, beside <paramref name="more"/>, gives: where, and the expression's start.</summary>
    private static List<string> Notes(string body, string more = "") =>
        [.. InferProcedure.Inferred(
                "dbo.p",
                [("tables.sql", Tables), .. more.Length > 0 ? [("more.sql", more)] : System.Array.Empty<(string, string)>(), ("p.sql", $"CREATE PROCEDURE dbo.p @id int AS\n{body}")])
            .Diagnostics
            .Where(d => d.Code == InferenceCodes.JsonBuiltAsText)
            .Select(d => $"{d.At}: {d.Message[..d.Message.IndexOf(" builds", StringComparison.Ordinal)]}")];

    [Fact]
    public void Brackets_joined_around_a_value_are_noted_even_when_nothing_reads_them_as_json()
    {
        var notes = Notes($"SELECT N'[' + coalesce({LineIds}, N'') + N']' AS ids FROM dbo.[order] AS o;");

        var note = Assert.Single(notes);
        Assert.StartsWith("p.sql:2:8: N'[' + coalesce((SELECT string_agg", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_form_is_noted_once_at_its_outermost_expression()
    {
        var notes = Notes("""
            SELECT concat(N'[', (SELECT string_agg(N'"' + string_escape(t.name, 'json') + N'"', N',') FROM dbo.tag AS t), N']') AS a,
                N'[' + concat_ws(N',', CASE WHEN @id > 1 THEN N'"retail"' END, N'"trade"') + N']' AS b,
                concat_ws(N',', N'"retail"', CASE WHEN @id > 2 THEN N'"trade"' END) AS c,
                concat(N'["', N'x', N'"]') AS literals_only, N'a' + o.ref AS plain, concat_ws(N',', o.ref, N'x') AS unquoted
            FROM dbo.[order] AS o;
            """);

        // The text inside the first form, concat_ws inside brackets and literals only are not noted again or at all.
        Assert.Equal(["p.sql:2:8", "p.sql:3:5", "p.sql:4:5"], notes.Select(n => n[..n.IndexOf(": ", StringComparison.Ordinal)]));
    }

    [Fact]
    public void Brackets_quoting_an_identifier_or_around_display_text_are_not_noted_but_a_lone_json_fragment_in_brackets_is()
    {
        // Quoting an identifier, and text that only starts and ends with a bracket, are not JSON; a FOR JSON fragment is.
        var notes = Notes("""
            SELECT N'[' + o.ref + N']' AS quoted, N'[' + o.ref + N'].[' + o.ref + N']' AS dotted,
                N'[' + cast(o.order_id AS nvarchar(10)) + N' KEYS]' AS display,
                concat(N'[', (SELECT o.ref AS ref FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), N']') AS fragment
            FROM dbo.[order] AS o;
            """);

        Assert.Equal(["p.sql:4:5"], notes.Select(n => n[..n.IndexOf(": ", StringComparison.Ordinal)]));
    }

    [Fact]
    public void A_function_and_a_view_the_procedure_never_reads_are_noted_too()
    {
        var notes = Notes("SELECT 1 AS one;", more: """
            CREATE FUNCTION dbo.ids (@id int) RETURNS nvarchar(max) AS
            BEGIN
                RETURN (SELECT N'[' + string_agg(convert(nvarchar(max), l.line_id), N',') + N']' FROM dbo.line AS l WHERE l.order_id = @id);
            END
            GO
            CREATE VIEW dbo.order_tags AS SELECT t.order_id, concat(N'[', string_agg(N'"' + string_escape(t.name, 'json') + N'"', N','), N']') AS tags FROM dbo.tag AS t GROUP BY t.order_id;
            """);

        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.StartsWith("more.sql:", n, StringComparison.Ordinal));
    }
}

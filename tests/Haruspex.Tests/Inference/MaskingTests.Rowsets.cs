using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Rowset functions given a masked argument, and JSON read from a masked column: the server masks every column of every row a
/// built-in or multi-statement function returns when an argument is computed from a masked column, and writes <c>json_query</c>
/// of one into a top-level <c>FOR JSON</c> unquoted.
/// </summary>
public partial class MaskingTests
{
    /// <summary>A multi-statement function whose rows hold a literal and CHECK-bound columns, whatever its argument.</summary>
    private static readonly (string File, string Sql) Cut = ("cut.sql", """
        CREATE FUNCTION clinic.cut (@code char(6))
        RETURNS @r TABLE (v char(6) NOT NULL, c int NOT NULL CHECK (c BETWEEN 5 AND 9), s varchar(10) NOT NULL CHECK (s IN ('A', 'B')))
        AS
        BEGIN
            INSERT @r VALUES (@code, 7, 'A');
            RETURN;
        END;
        """);

    /// <summary>An inline function, which the server inlines: only what its query computes from a masked argument is masked.</summary>
    private static readonly (string File, string Sql) Seven = ("seven.sql", "CREATE FUNCTION clinic.seven (@x int) RETURNS TABLE AS RETURN SELECT @x AS x, 7 AS seven;");

    [Fact]
    public void A_multi_statement_function_given_a_masked_argument_masks_every_column_it_gives()
    {
        var masked = Procedure("SELECT x.v, x.c, x.s FROM clinic.patient AS p CROSS APPLY clinic.cut(p.code) AS x;", more: Cut);
        var plain = Procedure("SELECT x.v, x.c, x.s FROM clinic.patient AS p CROSS APPLY clinic.cut(p.code_plain) AS x;", more: Cut);

        Assert.True(Facets(plain, "v")!.Length!.IsFixed);
        Assert.Equal(IrFactSourceKind.Check, Facets(plain, "c")!.Min!.From.Kind);
        Assert.NotNull(Facets(plain, "s")!.Values);
        Assert.False(Facets(masked, "v")!.Length!.IsFixed);
        foreach (var name in new[] { "v", "c", "s" })
            Assert.Equal(TypeOnly(Facets(masked, name)), Facets(masked, name));
    }

    [Fact]
    public void String_split_given_a_masked_string_masks_its_ordinal()
    {
        var masked = Procedure("SELECT s.ordinal FROM clinic.patient AS p CROSS APPLY STRING_SPLIT(p.email, N',', 1) AS s;");
        var plain = Procedure("SELECT s.ordinal FROM clinic.patient AS p CROSS APPLY STRING_SPLIT(p.note_plain, ',', 1) AS s;");

        Assert.Equal("1", Facets(plain, "ordinal")!.Min!.Value);
        Assert.NotEqual("1", Facets(masked, "ordinal")?.Min?.Value);
    }

    [Fact]
    public void Openjson_and_generate_series_given_a_masked_argument_mask_what_they_give()
    {
        var doc = Procedure("SELECT j.[type], g.value FROM clinic.patient AS p CROSS APPLY OPENJSON(p.note) AS j CROSS APPLY GENERATE_SERIES(1, p.rating) AS g;");

        Assert.Null(Facets(doc, "type")?.Values);
        Assert.Equal(TypeOnly(Facets(doc, "value")), Facets(doc, "value"));
    }

    [Fact]
    public void An_inline_function_given_a_masked_argument_masks_only_what_it_computes_from_it()
    {
        var masked = Procedure("SELECT f.x, f.seven FROM clinic.patient AS p CROSS APPLY clinic.seven(p.band) AS f;", more: Seven);
        var plain = Procedure("SELECT f.x, f.seven FROM clinic.patient AS p CROSS APPLY clinic.seven(p.band_plain) AS f;", more: Seven);

        Assert.Equal(Claims(Facets(plain, "seven")), Claims(Facets(masked, "seven")));
        Assert.NotNull(Facets(plain, "x")!.Values);
        Assert.Null(Facets(masked, "x")!.Values);
    }

    [Theory]
    [InlineData("json_query(note)")]
    [InlineData("json_query(note, '$.a')")]
    public void A_for_json_that_nests_json_query_of_a_masked_column_has_results_that_cannot_be_listed(string read)
    {
        var doc = Procedure($"SELECT id, {read} AS doc FROM clinic.patient FOR JSON PATH;");

        Assert.Null(Object(doc, "clinic.p").Results);
        Assert.Equal(IrUnknownReason.JsonShapeUnknown, Unknown(doc, "results").Reason);
    }

    [Fact]
    public void A_mask_added_without_the_table_s_schema_masks_a_table_of_that_name_in_any_schema()
    {
        var doc = Procedure(
            "SELECT code_plain FROM clinic.patient;",
            more: ("masks.sql", "ALTER TABLE patient ALTER COLUMN code_plain ADD MASKED WITH (FUNCTION = 'default()');"));

        Assert.False(Facets(doc, "code_plain")!.Length!.IsFixed);
    }
}

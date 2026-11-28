using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The one rule for what text of a <c>varchar</c> or <c>char</c> type gives back of a string (<see cref="CodePageText"/>). Why:
/// docs/decisions/0145-varchar-json-is-read-through-its-code-page.md; the server's side is observed, for every character of every
/// code page, in <c>CodePageObservationTests</c>.
/// </summary>
public class CodePageTextTests
{
    private static readonly IrFactSource Declared = new(IrFactSourceKind.Declared, "shop.badges.body");

    private static IrScalar Text(IrLength? length = null, IrMinLength? least = null, params string[] values) =>
        new(IrScalarKind.String, null, null, new IrFacets(
            Length: length,
            MinLength: least,
            Values: values.Length == 0 ? null : new IrValueSet([.. values.Select(v => new IrValue(v, IsString: true))], IrValueMatch.Exact, [Declared])));

    private static IrLength CodePoints(int n) => new(n, IsFixed: false, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, Declared);

    [Theory]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "café €", true)]
    [InlineData("SQL_Latin1_General_CP1_CI_AS", "Ω", false)]
    [InlineData("Cyrillic_General_CI_AS", "Жук", true)]
    [InlineData("Cyrillic_General_CI_AS", "café", false)]
    [InlineData("Japanese_CI_AS", "Ωアж", true)]
    [InlineData("Japanese_CI_AS", "€", false)]
    [InlineData("Latin1_General_100_CI_AS_SC_UTF8", "Ω😀ア", true)]
    [InlineData("Latin1_General_100_CI_AS", "😀", false)]
    [InlineData(null, "plain ascii \t\"", true)]
    [InlineData(null, "café", false)]
    [InlineData("No_Such_Collation", "café", false)]
    public void A_string_is_held_when_its_code_page_has_every_character_and_ascii_is_the_floor(string? collation, string text, bool held) =>
        Assert.Equal(held, CodePageText.Holds(collation, text));

    [Fact]
    public void Under_utf_8_a_lone_surrogate_is_not_held()
    {
        // As theory data, xUnit would turn the lone surrogate into U+FFFD.
        Assert.False(CodePageText.Holds("Latin1_General_100_CI_AS_SC_UTF8", "a\ud83d"));
        Assert.True(CodePageText.Holds("Latin1_General_100_CI_AS_SC_UTF8", "a\ud83d\ude00"));
    }

    [Fact]
    public void No_value_set_is_kept_as_none() => Assert.False(CodePageText.Keeps(null, "SQL_Latin1_General_CP1_CI_AS"));

    [Fact]
    public void A_value_set_held_keeps_every_fact()
    {
        var scalar = Text(CodePoints(5), null, "café", "bar");

        Assert.Same(scalar, CodePageText.Read(scalar, "SQL_Latin1_General_CP1_CI_AS"));
    }

    [Fact]
    public void A_value_set_not_held_is_dropped_its_least_length_kept_and_its_greatest_doubled()
    {
        var read = CodePageText.Read(Text(CodePoints(5), new IrMinLength(1, Declared) { Unit = IrLengthUnit.CodePoints }, "Ω"), "SQL_Latin1_General_CP1_CI_AS");

        Assert.Null(read.Facets?.Values);
        Assert.Equal(1, read.Facets?.MinLength?.Value);
        Assert.Equal((10, IrLengthUnit.CodePoints), (read.Facets?.Length?.Value, read.Facets?.Length?.Unit));
    }

    [Fact]
    public void Under_utf_8_a_greatest_length_stands()
    {
        var read = CodePageText.Read(Text(CodePoints(5)), "Latin1_General_100_CI_AS_SC_UTF8");

        Assert.Equal(5, read.Facets?.Length?.Value);
    }

    [Fact]
    public void A_greatest_length_in_bytes_is_not_known_after_a_code_page_and_one_in_utf_16_units_stands()
    {
        var bytes = CodePageText.Read(Text(new IrLength(5, IsFixed: true, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, Declared)), null);
        var units = CodePageText.Read(Text(new IrLength(5, IsFixed: false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Declared)), null);

        Assert.Null(bytes.Facets?.Length);
        Assert.Equal(5, units.Facets?.Length?.Value);
    }

    [Fact]
    public void Declared_json_is_read_through_every_object_type_it_refers_to()
    {
        var badge = new IrObjectType([new IrField("tier", Text(null, null, "Ω", "alpha"), IrPresence.Always, [])]);
        var (_, types) = CodePageText.Read(new IrRef("Badge"), new Dictionary<string, IrObjectType> { ["Badge"] = badge }.ToEquatableMap(t => t.Key, t => t.Value), "SQL_Latin1_General_CP1_CI_AS");

        Assert.Null(Assert.IsType<IrScalar>(Assert.Single(types["Badge"].Fields).Type).Facets?.Values);
    }
}

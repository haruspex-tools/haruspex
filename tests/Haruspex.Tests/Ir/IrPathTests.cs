using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>The IR's path syntax, and the one identifier rule the emitter and the paths share.</summary>
public class IrPathTests
{
    [Theory]
    [InlineData("results[0][].lines[].sku")]
    [InlineData("results[0].tags{}")]
    [InlineData("results[0].payload<json>.id")]
    [InlineData("<bom.part_tree>.part.label")]
    [InlineData("results[0][][\"unit price\"]")]
    [InlineData("results[1][].a$b")]
    [InlineData("results[0].pair[1]")]
    [InlineData("")]
    public void A_path_reads_back_as_it_was_written(string text)
    {
        Assert.True(IrPath.TryParse(text, out var segments, out var error), error);
        Assert.Equal(text, IrPath.Render(segments));
    }

    [Fact]
    public void A_leading_dot_is_allowed_and_dropped()
    {
        Assert.True(IrPath.TryParse(".lines[]", out var segments, out _));
        Assert.Equal("lines[]", IrPath.Render(segments));
    }

    [Theory]
    [InlineData("a..b", "'a..b' is not a path: at 1, a '.' must be followed by a key; write a key that is not an identifier as [\"key\"].")]
    [InlineData("a[x]", "'a[x]' is not a path: at 1, [ must be followed by ], a position such as 0, or a quoted key.")]
    [InlineData("a[\"x]", "'a[\"x]' is not a path: at 1, [\" must be followed by a JSON string and ].")]
    [InlineData("a<>", "'a<>' is not a path: at 1, < must be followed by json or an object's name, and >.")]
    [InlineData("a b", "'a b' is not a path: at 1, unexpected ' '.")]
    [InlineData("a{", "'a{' is not a path: at 1, unexpected '{'.")]
    public void A_malformed_path_says_where(string text, string expected)
    {
        Assert.False(IrPath.TryParse(text, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("a$b", true)]
    [InlineData("$", true)]
    [InlineData("_x1", true)]
    [InlineData("1x", false)]
    [InlineData("a-b", false)]
    [InlineData("a b", false)]
    [InlineData("é", false)]
    [InlineData("", false)]
    public void The_emitter_and_the_path_renderer_agree_on_identifiers(string key, bool identifier)
    {
        // The path writes a key as .key exactly when the emitter writes it without quotes; they used to disagree about $.
        Assert.Equal(identifier, IrIdentifiers.IsIdentifier(key));
        Assert.Equal(identifier, IrPath.Key(key) == "." + key);

        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrRef("PRow"), [])])],
            new Dictionary<string, IrObjectType> { ["PRow"] = new([new IrField(key, new IrScalar(IrScalarKind.Boolean, null, "bit"), IrPresence.Always, [])]) },
            []);
        var zod = ZodEmitter.Emit(doc);
        Assert.Equal(identifier, zod.Contains($"  {key}: z.boolean(),", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("delete")]
    [InlineData("class")]
    public void A_reserved_word_is_an_identifier_but_not_a_name(string word)
    {
        Assert.True(IrIdentifiers.IsIdentifier(word));
        Assert.True(IrIdentifiers.IsReserved(word));
        Assert.False(IrIdentifiers.IsReserved("String"));
    }
}

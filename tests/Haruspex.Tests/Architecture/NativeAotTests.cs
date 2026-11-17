using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Haruspex.Tests.Architecture;

/// <summary>
/// What Native AOT refuses, kept out of the source: the command is compiled ahead of time (CI's native jobs), where a call that
/// needs reflection is a warning, and warnings are errors. Every method of <c>JsonSerializer</c> writes or reads objects by
/// reflection unless given a source-generated context; <c>Utf8JsonWriter</c>, <c>JsonDocument</c> and <c>JsonEncodedText</c>
/// don't, and are what the source uses.
/// </summary>
public partial class NativeAotTests
{
    [Fact]
    public void No_code_in_src_serialises_json_by_reflection()
    {
        var found = Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetRelativePath(RepositoryPaths.Root, f).Replace('\\', '/'), Line: i + 1, Text: line)))
            .Where(l => Reflective().IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}");

        Assert.Empty(found);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("a \"quoted\" word")]
    [InlineData(@"back\slash")]
    [InlineData("line\nbreak\tand\rreturn")]
    [InlineData("\u0001\u001b\u007f")]
    [InlineData("<html> & 'apostrophes'")]
    [InlineData("\u00e9, \u00df, \u6771\u4eac, \U0001F600")]
    [InlineData("\u2028\u2029")]
    public void A_replacement_hint_quotes_text_as_the_json_serialiser_did(string text) =>
        Assert.Equal(
            JsonSerializer.Serialize(text, Relaxed),
            ReplacementHint.Json(text));

    /// <summary>The options the hint serialised with before, to compare with.</summary>
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A call of any method of <c>JsonSerializer</c>.</summary>
    [GeneratedRegex(@"(?<!\w)JsonSerializer\.\w+[(<]")]
    private static partial Regex Reflective();
}

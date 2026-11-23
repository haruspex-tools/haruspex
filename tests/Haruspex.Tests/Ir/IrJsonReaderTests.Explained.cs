using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the reader reads and refuses of a reason that says what makes it so (0.29, <see cref="IrNullReason.Detail"/>): the
/// label it takes, and a detail in a document labelled earlier. Why:
/// docs/decisions/0097-a-path-into-json-of-known-shape-is-walked-step-by-step.md.
/// </summary>
public partial class IrJsonReaderTests
{
    private const string Detail = "`$.address` may be left out, as the schema doesn't require it";

    /// <summary>The sample of everything, its first nullable field's first reason given <see cref="Detail"/>.</summary>
    private static IrDocument Explained()
    {
        var (name, type) = IrSamples.Everything.Types.OrderBy(t => t.Key, StringComparer.Ordinal).First(t => t.Value.Fields.Any(f => f.NullableBecause.Count > 0));
        var fields = type.Fields.ToList();
        var i = fields.FindIndex(f => f.NullableBecause.Count > 0);
        var field = fields[i];
        fields[i] = field with { NullableBecause = [field.NullableBecause[0] with { Detail = Detail }, .. field.NullableBecause.Skip(1)] };
        return IrSamples.Everything with { Types = IrSamples.Everything.Types.ToDictionary(t => t.Key, t => t.Key == name ? t.Value with { Fields = [.. fields] } : t.Value) };
    }

    [Fact]
    public void A_document_whose_reason_says_what_makes_it_so_is_labelled_0_29_and_reads_back_equal()
    {
        var document = Explained();
        var json = IrJsonWriter.Write(document);

        Assert.Equal("0.29", document.Version);
        Assert.Contains($"\"detail\": \"{Detail}\"", json, StringComparison.Ordinal);
        Assert.Equal(document, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_detail_in_a_document_labelled_earlier_is_refused_by_its_path()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(Explained()))!.AsObject();
        json["irVersion"] = IrSamples.Everything.Version;

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.EndsWith(".detail", e.Path, StringComparison.Ordinal);
        Assert.Contains("a reason's detail and a minimum length's unit are IR 0.29 or later", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_labelled_0_29_in_which_no_reason_says_more_is_refused()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Declared))!.AsObject();
        json["irVersion"] = IrDocument.ExplainedVersion;

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("this one has neither", e.Message, StringComparison.Ordinal);
    }

    /// <summary>The declared sample, its declared minimum in code points.</summary>
    private static IrDocument CodePointMinimum() =>
        IrSamples.Declared with
        {
            Types = IrSamples.Declared.Types.ToDictionary(t => t.Key, t => t.Value with
            {
                Fields = [.. t.Value.Fields.Select(f => f.Type is IrScalar { Facets.MinLength: { } least } scalar
                    ? f with { Type = scalar with { Facets = scalar.Facets with { MinLength = least with { Unit = IrLengthUnit.CodePoints } } } }
                    : f)],
            }),
        };

    [Fact]
    public void A_minimum_in_code_points_is_written_with_its_unit_labelled_0_29_and_reads_back_equal()
    {
        var document = CodePointMinimum();
        var json = IrJsonWriter.Write(document);

        Assert.Equal("0.29", document.Version);
        Assert.Contains("\"unit\": \"code-points\"", json, StringComparison.Ordinal);
        Assert.Equal(document, IrJsonReader.Read(json));
        Assert.Equal(json, IrJsonWriter.Write(IrJsonReader.Read(json)));
    }

    [Fact]
    public void A_minimum_in_utf_16_code_units_says_no_unit_and_one_that_says_it_or_bytes_is_refused()
    {
        Assert.Null(Field(JsonNode.Parse(IrJsonWriter.Write(IrSamples.Declared))!.AsObject(), "mode")["type"]!["minLength"]!["unit"]);
        foreach (var unit in new[] { "utf16", "bytes" })
        {
            var json = JsonNode.Parse(IrJsonWriter.Write(CodePointMinimum()))!.AsObject();
            var least = json["types"]!.AsObject().Select(t => t.Value!["fields"]!.AsArray().Select(f => f!["type"]!["minLength"]).OfType<JsonNode>().FirstOrDefault()).OfType<JsonNode>().First();
            least["unit"] = unit;

            var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
            Assert.EndsWith(".minLength.unit", e.Path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_minimum_in_code_points_in_a_document_labelled_0_28_is_refused_by_its_path()
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(CodePointMinimum()))!.AsObject();
        json["irVersion"] = IrDocument.DeclaredVersion;

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));

        Assert.EndsWith(".minLength.unit", e.Path, StringComparison.Ordinal);
    }
}

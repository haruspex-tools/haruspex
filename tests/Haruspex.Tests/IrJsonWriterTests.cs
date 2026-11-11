using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.Tests;

public class IrJsonWriterTests
{
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormats.Int32, "int");

    private static IrDocument Document() => new(
        new IrSource("tsql"),
        [
            new IrObject("b.second", "Second", IrObjectKind.Procedure, "b.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("SecondRow")), [])]),
            new IrObject("a.first", "First", IrObjectKind.Procedure, "a.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("FirstRow")), [])]),
        ],
        new Dictionary<string, IrObjectType>
        {
            ["SecondRow"] = new([new IrField("x", Int, IrPresence.Always, [])]),
            ["FirstRow"] = new([new IrField("y", Int, IrPresence.Always, [new IrNullReason(IrNullRules.NullableColumn, "a.t.y")])]),
        },
        [
            new IrUnknown("b.second", "results[0][].z", IrUnknownReasons.UnsupportedExpression, "z", "b.sql:2:1"),
            new IrUnknown("a.first", "results[0][].z", IrUnknownReasons.UnsupportedExpression, "z", "a.sql:2:1"),
        ]);

    [Fact]
    public void Starts_with_the_ir_version()
    {
        var json = IrJsonWriter.Write(Document());

        Assert.StartsWith("{\n  \"irVersion\": \"" + IrDocument.CurrentVersion + "\",\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_byte_for_byte_stable_with_lf_endings_and_no_byte_order_mark()
    {
        var first = IrJsonWriter.Write(Document());
        var second = IrJsonWriter.Write(Document());

        Assert.Equal(first, second);
        Assert.DoesNotContain('\r', first);
        Assert.False(first.StartsWith('﻿'));
        Assert.EndsWith("}\n", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Sorts_objects_types_and_unknowns_whatever_their_input_order()
    {
        var json = IrJsonWriter.Write(Document());

        Assert.True(json.IndexOf("\"a.first\"", StringComparison.Ordinal) < json.IndexOf("\"b.second\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"FirstRow\": {", StringComparison.Ordinal) < json.IndexOf("\"SecondRow\": {", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"a.sql:2:1\"", StringComparison.Ordinal) < json.IndexOf("\"b.sql:2:1\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Derives_nullable_from_the_reasons()
    {
        using var json = JsonDocument.Parse(IrJsonWriter.Write(Document()));
        var types = json.RootElement.GetProperty("types");

        var notNull = types.GetProperty("SecondRow").GetProperty("fields")[0];
        Assert.False(notNull.GetProperty("nullable").GetBoolean());
        Assert.False(notNull.TryGetProperty("nullableBecause", out _));

        var nullable = types.GetProperty("FirstRow").GetProperty("fields")[0];
        Assert.True(nullable.GetProperty("nullable").GetBoolean());
        Assert.Equal("nullable-column", nullable.GetProperty("nullableBecause")[0].GetProperty("rule").GetString());
    }
}

public class IrJsonWriterFacetTests
{
    private static string Field(IrScalar scalar)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("a.p", "P", IrObjectKind.Procedure, "a.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("PRow")), [])])],
            new Dictionary<string, IrObjectType> { ["PRow"] = new([new IrField("v", scalar, IrPresence.Always, [])]) },
            []);
        using var json = JsonDocument.Parse(IrJsonWriter.Write(doc));
        return json.RootElement.GetProperty("types").GetProperty("PRow").GetProperty("fields")[0].GetProperty("type").GetRawText();
    }

    private static readonly IrFactSource Type = new(IrFactSources.Type, "decimal(38,2)");
    private static readonly IrFactSource Check = new(IrFactSources.Check, "dbo.ck_v");

    [Fact]
    public void Writes_bounds_as_exact_json_numbers_with_their_sources()
    {
        var text = Field(new IrScalar(IrScalarKind.Number, IrFormats.Decimal, "decimal(38,2)", new IrFacets(
            Digits: new IrDigits(38, 2, Type),
            Min: new IrBound("0", false, Check),
            Max: new IrBound("999999999999999999999999999999999999.99", true, Type))));

        // Written from the exact text, never through a double.
        Assert.Contains("\"max\": {\n", text, StringComparison.Ordinal);
        Assert.Contains("\"value\": 999999999999999999999999999999999999.99,", text, StringComparison.Ordinal);
        Assert.Contains("\"digits\": {\n", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.Equal(38, root.GetProperty("digits").GetProperty("precision").GetInt32());
        Assert.False(root.GetProperty("min").GetProperty("inclusive").GetBoolean());
        Assert.Equal("dbo.ck_v", root.GetProperty("min").GetProperty("from").GetProperty("check").GetString());
        Assert.Equal("decimal(38,2)", root.GetProperty("max").GetProperty("from").GetProperty("type").GetString());
    }

    [Fact]
    public void Writes_lengths_with_their_unit_and_a_known_encoding()
    {
        using var max = JsonDocument.Parse(Field(new IrScalar(IrScalarKind.String, null, "varchar(5)", new IrFacets(
            Length: new IrLength(5, false, IrLengthUnit.Bytes, IrByteEncoding.Utf8, Type), MinLength: new IrMinLength(1, Check)))));
        using var fixedLength = JsonDocument.Parse(Field(new IrScalar(IrScalarKind.String, null, "nchar(5)", new IrFacets(
            Length: new IrLength(5, true, IrLengthUnit.Utf16, IrByteEncoding.Unknown, Type)))));

        Assert.Equal("bytes", max.RootElement.GetProperty("maxLength").GetProperty("unit").GetString());
        Assert.Equal("utf-8", max.RootElement.GetProperty("maxLength").GetProperty("encoding").GetString());
        Assert.Equal(1, max.RootElement.GetProperty("minLength").GetProperty("value").GetInt32());
        Assert.Equal("utf16", fixedLength.RootElement.GetProperty("length").GetProperty("unit").GetString());
        Assert.False(fixedLength.RootElement.GetProperty("length").TryGetProperty("encoding", out _));
    }

    [Fact]
    public void Writes_a_value_set_with_its_match_and_every_source()
    {
        using var strings = JsonDocument.Parse(Field(new IrScalar(IrScalarKind.String, null, "varchar(10)", new IrFacets(
            Values: new IrValueSet([new IrValue("open", true), new IrValue("1.50", true)], IrValueMatch.Collation, [Check])))));
        using var numbers = JsonDocument.Parse(Field(new IrScalar(IrScalarKind.Number, IrFormats.Int32, "int", new IrFacets(
            Values: new IrValueSet([new IrValue("-3", false), new IrValue("12345678901234567890", false)], IrValueMatch.Exact, [Check, new IrFactSource(IrFactSources.Check, "dbo.ck_w")])))));

        var s = strings.RootElement.GetProperty("values");
        Assert.Equal("collation", s.GetProperty("match").GetString());
        Assert.Equal(["open", "1.50"], s.GetProperty("items").EnumerateArray().Select(i => i.GetString()));
        var n = numbers.RootElement.GetProperty("values");
        Assert.Equal("exact", n.GetProperty("match").GetString());
        Assert.Equal(["-3", "12345678901234567890"], n.GetProperty("items").EnumerateArray().Select(i => i.GetRawText()));
        Assert.Equal(2, n.GetProperty("from").GetArrayLength());
    }

    [Fact]
    public void Writes_no_facets_when_none_are_known() =>
        Assert.DoesNotContain("from", Field(new IrScalar(IrScalarKind.Number, IrFormats.Int32, "int", new IrFacets())), StringComparison.Ordinal);

    [Theory]
    [InlineData("1e5")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData("NaN")]
    public void Refuses_a_bound_that_is_not_plain_decimal_text(string value) =>
        Assert.Throws<ArgumentException>(() => Field(new IrScalar(IrScalarKind.Number, null, "float", new IrFacets(Min: new IrBound(value, true, Check)))));
}

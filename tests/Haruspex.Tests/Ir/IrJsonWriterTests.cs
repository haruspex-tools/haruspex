using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

public class IrJsonWriterTests
{
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static IrDocument Document() => new(
        new IrSource("tsql"),
        [
            new IrObject("b.second", "Second", IrObjectKind.Procedure, "b.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("SecondRow")), [])]),
            new IrObject("a.first", "First", IrObjectKind.Procedure, "a.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("FirstRow")), [])]),
        ],
        new Dictionary<string, IrObjectType>
        {
            ["SecondRow"] = new([new IrField("x", Int, IrPresence.Always, [])]),
            ["FirstRow"] = new([new IrField("y", Int, IrPresence.Always, [new IrNullReason(IrNullRule.NullableColumn, "a.t.y")])]),
        },
        [
            new IrUnknown("b.second", "results[0][].z", IrUnknownReason.UnsupportedExpression, "z", "b.sql:2:1"),
            new IrUnknown("a.first", "results[0][].z", IrUnknownReason.UnsupportedExpression, "z", "a.sql:2:1"),
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

using System.Text.Json;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>The type nodes IR 0.3 added for the JSON constructors: tuples, records, unions, and what arrays now say.</summary>
public class IrJsonNodeTests
{
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");
    private static readonly IrScalar Text = new(IrScalarKind.String, null, "nvarchar(10)");
    private static readonly IrNullReason Because = new(IrNullRule.NullableColumn, "a.t.c");

    private static JsonElement Field(IrType type)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("a.p", "P", IrObjectKind.Procedure, "a.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrArray(new IrRef("PRow")), [])])],
            new Dictionary<string, IrObjectType> { ["PRow"] = new([new IrField("v", type, IrPresence.Always, [])]) },
            []);
        using var json = JsonDocument.Parse(IrJsonWriter.Write(doc));
        return json.RootElement.GetProperty("types").GetProperty("PRow").GetProperty("fields")[0].GetProperty("type").Clone();
    }

    [Fact]
    public void The_newest_version_is_0_32() => Assert.Equal("0.32", IrDocument.CurrentVersion);

    [Fact]
    public void A_plain_array_is_written_as_before()
    {
        var array = Field(new IrArray(Int));

        Assert.Equal(["kind", "items"], array.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void An_array_says_when_its_items_may_be_null_and_how_many_there_are()
    {
        var array = Field(new IrArray(Int) { ItemsNullableBecause = [Because], MinItems = 1, MaxItems = 3 });

        Assert.Equal(["kind", "items", "itemsNullable", "itemsNullableBecause", "minItems", "maxItems"], array.EnumerateObject().Select(p => p.Name));
        Assert.True(array.GetProperty("itemsNullable").GetBoolean());
        Assert.Equal("nullable-column", array.GetProperty("itemsNullableBecause")[0].GetProperty("rule").GetString());
        Assert.Equal(1, array.GetProperty("minItems").GetInt32());
        Assert.Equal(3, array.GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public void A_tuple_lists_each_element_with_its_nullability()
    {
        var tuple = Field(new IrTuple([new IrElement(Int, []), new IrElement(Text, [Because])]));

        Assert.Equal("tuple", tuple.GetProperty("kind").GetString());
        var items = tuple.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("number", items[0].GetProperty("type").GetProperty("kind").GetString());
        Assert.False(items[0].GetProperty("nullable").GetBoolean());
        Assert.False(items[0].TryGetProperty("nullableBecause", out _));
        Assert.True(items[1].GetProperty("nullable").GetBoolean());
        Assert.Equal("a.t.c", items[1].GetProperty("nullableBecause")[0].GetProperty("at").GetString());
    }

    [Fact]
    public void An_empty_tuple_is_written_with_no_items() =>
        Assert.Equal(0, Field(new IrTuple([])).GetProperty("items").GetArrayLength());

    [Fact]
    public void A_record_has_string_keys_and_one_value_type()
    {
        var record = Field(new IrRecord(Int, [Because]));

        Assert.Equal("record", record.GetProperty("kind").GetString());
        Assert.Equal("number", record.GetProperty("values").GetProperty("kind").GetString());
        Assert.True(record.GetProperty("valuesNullable").GetBoolean());
        Assert.Single(record.GetProperty("valuesNullableBecause").EnumerateArray());
        Assert.False(Field(new IrRecord(Int, [])).GetProperty("valuesNullable").GetBoolean());
    }

    [Fact]
    public void A_union_lists_its_variants_in_order()
    {
        var union = Field(new IrUnion([Int, Text]));

        Assert.Equal("union", union.GetProperty("kind").GetString());
        Assert.Equal(["number", "string"], union.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("kind").GetString()));
    }

    [Fact]
    public void Empty_aggregate_is_a_null_rule() => Assert.Equal(IrNullRule.EmptyAggregate, IrNullRule.EmptyAggregate);
}

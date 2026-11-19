using Haruspex.Emit.JsonSchema;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>A table type's row in the JSON Schema document: what a client sends, so strict whatever the objects are.</summary>
public partial class JsonSchemaEmitterTests
{
    [Fact]
    public void A_table_type_s_row_forbids_other_keys_and_requires_every_column()
    {
        var row = Definition(Emit(Ir.IrSamples.Whole, new JsonSchemaOptions { Objects = JsonSchemaObjects.Strip }), "LineListRow");

        Assert.False(row["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["sku", "note"], row["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.StartsWith("A row of the table type sales.line_list", row["description"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}

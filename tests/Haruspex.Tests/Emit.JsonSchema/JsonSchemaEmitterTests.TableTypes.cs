using System.Text.RegularExpressions;
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

    [Fact]
    public void A_table_type_s_row_describes_its_values_as_a_client_sends_them_not_as_for_json_writes_them()
    {
        var row = Definition(Emit(Fixture.Named("till").Ir), "ReadingListRow")["properties"]!;
        var takenAt = new Regex(row["taken_at"]!["pattern"]!.GetValue<string>(), RegexOptions.None, TimeSpan.FromSeconds(1));

        // A datetime2(0) in whole seconds: a fraction, which the server would round without an error, is refused.
        Assert.Matches(takenAt, "2026-11-19T10:20:30");
        Assert.DoesNotMatch(takenAt, "2026-11-19T10:20:30.7");

        // A bigint as a number or the string of its digits, as the parameters' Zod takes it.
        Assert.Equal(["integer", "string", "null"], row["total"]!["anyOf"]!.AsArray().Select(s => s!["type"]!.GetValue<string>()));

        // char(3) at most 3: a shorter value sent is padded, so no minimum.
        Assert.Null(row["code"]!["minLength"]);
        Assert.Equal(3, row["code"]!["maxLength"]!.GetValue<int>());
    }
}
